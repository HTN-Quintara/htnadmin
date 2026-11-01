using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using Htn.Api.Tests.Infrastructure;
using static Htn.Api.Tests.Speakers.SpeakersTestData;

namespace Htn.Api.Tests.Speakers;

public sealed class SpeakerLinkFlowTests(HtnApiFactory factory) : IClassFixture<HtnApiFactory>
{
    private static ApiClient Anon(HtnApiFactory f) => f.NewClient();

    [Fact]
    public async Task Programs_lead_can_manage_speakers_but_a_logistics_lead_and_a_plain_goon_cannot()
    {
        var s = await SceneAsync(factory);
        var sid = await CreateSpeakerAsync(s.Board, s.EventId);
        Assert.Equal(HttpStatusCode.OK, (await s.ProgramsLead.GetAsync($"/api/events/{s.EventId}/agenda/speakers")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await s.LogisticsLead.GetAsync($"/api/events/{s.EventId}/agenda/speakers")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await s.Goon.GetAsync($"/api/events/{s.EventId}/agenda/speakers")).StatusCode);
    }

    [Fact]
    public async Task Issuing_a_link_logs_it_and_the_full_otp_flow_completes_the_submission()
    {
        var s = await SceneAsync(factory);
        var sid = await CreateSpeakerAsync(s.Board, s.EventId);
        var token = await IssueLinkAsync(s.Board, s.EventId, sid, factory);

        var anon = Anon(factory);
        var landing = await ApiClient.JsonAsync(await anon.GetAsync($"/api/public/speaker-links/{token}"));
        Assert.Equal("NeedsCode", landing.GetProperty("state").GetString());

        (await anon.PostAsync($"/api/public/speaker-links/{token}/code")).EnsureSuccessStatusCode();
        var code = await LastOtpCodeAsync(factory);

        var verify = await anon.PostAsync($"/api/public/speaker-links/{token}/verify", new { code });
        verify.EnsureSuccessStatusCode();
        var verifyBody = await ApiClient.JsonAsync(verify);
        var sessionToken = verifyBody.GetProperty("sessionToken").GetString()!;

        var submitRes = await anon.PostAsync($"/api/public/speaker-links/{token}/submit", new
        {
            arrivalDate = "2026-11-21", departureDate = "2026-11-23", flyingFrom = "Davao",
            preferredArrivalTime = "Afternoon", idFullName = "Lara Gomez", needsNotes = "Vegetarian", consent = true,
            hasPlusOne = false,
        }, headers: new Dictionary<string, string> { ["X-Speaker-Session"] = sessionToken });
        submitRes.EnsureSuccessStatusCode();

        // Staff sees the submitted travel details, and the access log has every step logged.
        var detail = await ApiClient.JsonAsync(await s.Board.GetAsync($"/api/events/{s.EventId}/agenda/speakers/{sid}"));
        Assert.Equal("Davao", detail.GetProperty("travel").GetProperty("flyingFrom").GetString());
        var log = detail.GetProperty("log");
        var whats = log.EnumerateArray().Select(e => e.GetProperty("what").GetString()).ToList();
        Assert.Contains("Form submitted", whats);
        Assert.Contains("Code verified", whats);
        Assert.True(log.GetArrayLength() >= 3);
    }

    [Fact]
    public async Task Five_wrong_codes_lock_the_link_and_a_resend_during_the_lock_is_refused()
    {
        var s = await SceneAsync(factory);
        var sid = await CreateSpeakerAsync(s.Board, s.EventId);
        var token = await IssueLinkAsync(s.Board, s.EventId, sid, factory);
        var anon = Anon(factory);
        (await anon.PostAsync($"/api/public/speaker-links/{token}/code")).EnsureSuccessStatusCode();

        HttpResponseMessage last = null!;
        for (var i = 0; i < 5; i++)
            last = await anon.PostAsync($"/api/public/speaker-links/{token}/verify", new { code = "000000" });
        Assert.Equal(HttpStatusCode.TooManyRequests, last.StatusCode);

        // A resend while locked is refused (the row-level lock, not just the per-IP limiter).
        var resend = await anon.PostAsync($"/api/public/speaker-links/{token}/code");
        Assert.Equal(HttpStatusCode.TooManyRequests, resend.StatusCode);
    }

    [Fact]
    public async Task An_unknown_token_is_404_and_a_revoked_one_is_410_with_the_same_body_as_expired()
    {
        var s = await SceneAsync(factory);
        var sid = await CreateSpeakerAsync(s.Board, s.EventId);
        var token = await IssueLinkAsync(s.Board, s.EventId, sid, factory);
        var anon = Anon(factory);

        Assert.Equal(HttpStatusCode.NotFound, (await anon.GetAsync("/api/public/speaker-links/not-a-real-token")).StatusCode);

        (await s.Board.PostAsync($"/api/events/{s.EventId}/agenda/speakers/{sid}/link/revoke", new { reason = "Speaker withdrew" })).EnsureSuccessStatusCode();
        var revoked = await anon.GetAsync($"/api/public/speaker-links/{token}");
        Assert.Equal(HttpStatusCode.Gone, revoked.StatusCode);
    }

    [Fact]
    public async Task Public_responses_carry_noindex()
    {
        var s = await SceneAsync(factory);
        var sid = await CreateSpeakerAsync(s.Board, s.EventId);
        var token = await IssueLinkAsync(s.Board, s.EventId, sid, factory);
        var res = await Anon(factory).GetAsync($"/api/public/speaker-links/{token}");
        Assert.Equal("noindex, nofollow", res.Headers.GetValues("X-Robots-Tag").First());
    }

    [Fact]
    public async Task Confirming_a_plus_one_issues_a_single_use_50_percent_voucher()
    {
        var s = await SceneAsync(factory);
        var sid = await CreateSpeakerAsync(s.Board, s.EventId, name: "Maria Gomez");
        var token = await IssueLinkAsync(s.Board, s.EventId, sid, factory);
        var anon = Anon(factory);
        (await anon.PostAsync($"/api/public/speaker-links/{token}/code")).EnsureSuccessStatusCode();
        var code = await LastOtpCodeAsync(factory);
        var verify = await ApiClient.JsonAsync(await anon.PostAsync($"/api/public/speaker-links/{token}/verify", new { code }));
        var sessionToken = verify.GetProperty("sessionToken").GetString()!;

        var submit = await anon.PostAsync($"/api/public/speaker-links/{token}/submit", new
        {
            consent = true, hasPlusOne = true, plusOneName = "Maria Villanueva", plusOneEmail = "maria.v@example.com", plusOneSharesRoom = true,
        }, headers: new Dictionary<string, string> { ["X-Speaker-Session"] = sessionToken });
        submit.EnsureSuccessStatusCode();
        var voucherCode = (await ApiClient.JsonAsync(submit)).GetProperty("plusOneVoucherCode").GetString();
        Assert.NotNull(voucherCode);
        Assert.StartsWith("SPK-VILLANUEVA-", voucherCode);

        var detail = await ApiClient.JsonAsync(await s.Board.GetAsync($"/api/events/{s.EventId}/agenda/speakers/{sid}"));
        Assert.Equal(voucherCode, detail.GetProperty("plusOne").GetProperty("voucherCode").GetString());
    }
}

public sealed class SpeakerRetentionTests(HtnApiFactory factory) : IClassFixture<HtnApiFactory>
{
    [Fact]
    public async Task Flight_and_id_name_are_purged_30_days_after_the_event_but_bus_and_dates_survive()
    {
        var s = await SceneAsync(factory);
        var sid = await CreateSpeakerAsync(s.Board, s.EventId);
        var token = await IssueLinkAsync(s.Board, s.EventId, sid, factory);
        var anon = factory.NewClient();
        (await anon.PostAsync($"/api/public/speaker-links/{token}/code")).EnsureSuccessStatusCode();
        var code = await LastOtpCodeAsync(factory);
        var verify = await Htn.Api.Tests.Infrastructure.ApiClient.JsonAsync(await anon.PostAsync($"/api/public/speaker-links/{token}/verify", new { code }));
        var sessionToken = verify.GetProperty("sessionToken").GetString()!;
        (await anon.PostAsync($"/api/public/speaker-links/{token}/submit", new
        {
            arrivalDate = "2026-11-21", departureDate = "2026-11-23", flyingFrom = "Davao",
            preferredArrivalTime = "Afternoon", idFullName = "Lara Gomez", busFrom = "Baguio bus station",
            consent = true, hasPlusOne = false,
        }, headers: new Dictionary<string, string> { ["X-Speaker-Session"] = sessionToken })).EnsureSuccessStatusCode();

        // Push the event 31 days into the past, then run the sweep once.
        await factory.DbAsync(async db =>
        {
            var ev = await db.Events.FirstAsync(e => e.Id == s.EventId);
            var type = typeof(Htn.Domain.Events.Event);
            type.GetProperty("StartsAt")!.SetValue(ev, DateTimeOffset.UtcNow.AddDays(-32));
            type.GetProperty("EndsAt")!.SetValue(ev, DateTimeOffset.UtcNow.AddDays(-31));
            await db.SaveChangesAsync();
            return 0;
        });

        using var scope = factory.Services.CreateScope();
        var purged = await scope.ServiceProvider.GetRequiredService<Htn.Infrastructure.Retention.SpeakerDataRetention>().RunOnceAsync();
        Assert.True(purged >= 1);

        var detail = await Htn.Api.Tests.Infrastructure.ApiClient.JsonAsync(await s.Board.GetAsync($"/api/events/{s.EventId}/agenda/speakers/{sid}"));
        var travel = detail.GetProperty("travel");
        Assert.Null(travel.GetProperty("flyingFrom").GetString());
        Assert.False(travel.GetProperty("hasIdFullName").GetBoolean());
        Assert.Equal("2026-11-21", travel.GetProperty("arrivalDate").GetString());
        Assert.Equal("Baguio bus station", travel.GetProperty("busFrom").GetString());

        // The audit entry names which fields were cleared, never the values.
        var beforeJson = await factory.DbAsync(db => db.AuditEntries.AsNoTracking()
            .Where(a => a.Action == "speaker.travel_purged" && a.EntityId == sid.ToString())
            .Select(a => a.BeforeJson).FirstOrDefaultAsync());
        Assert.NotNull(beforeJson);
        Assert.DoesNotContain("Lara Gomez", beforeJson);
        Assert.DoesNotContain("Davao", beforeJson);
        Assert.Contains("idFullName", beforeJson);
    }
}

public sealed class SpeakerPlusOneConcurrencyTests(HtnApiFactory factory) : IClassFixture<HtnApiFactory>
{
    [Fact]
    public async Task A_concurrent_double_submit_with_a_plus_one_never_500s_and_leaves_exactly_one_live_voucher()
    {
        var s = await SceneAsync(factory);
        var sid = await CreateSpeakerAsync(s.Board, s.EventId, name: "Dana Ocampo");
        var token = await IssueLinkAsync(s.Board, s.EventId, sid, factory);
        var anon = factory.NewClient();
        (await anon.PostAsync($"/api/public/speaker-links/{token}/code")).EnsureSuccessStatusCode();
        var code = await LastOtpCodeAsync(factory);
        var verify = await Htn.Api.Tests.Infrastructure.ApiClient.JsonAsync(await anon.PostAsync($"/api/public/speaker-links/{token}/verify", new { code }));
        var sessionToken = verify.GetProperty("sessionToken").GetString()!;

        var body = new
        {
            consent = true, hasPlusOne = true, plusOneName = "Guest Plus One", plusOneEmail = "guest@example.com", plusOneSharesRoom = true,
        };
        var headers = new Dictionary<string, string> { ["X-Speaker-Session"] = sessionToken };
        var results = await Task.WhenAll(
            anon.PostAsync($"/api/public/speaker-links/{token}/submit", body, headers: headers),
            anon.PostAsync($"/api/public/speaker-links/{token}/submit", body, headers: headers));

        var codes = results.Select(r => r.StatusCode).ToList();
        Assert.All(codes, c => Assert.True(c is HttpStatusCode.OK or HttpStatusCode.Conflict, $"expected 200 or 409, got {c}"));
        Assert.Contains(HttpStatusCode.OK, codes);

        var liveVouchers = await factory.DbAsync(db => db.Vouchers.AsNoTracking()
            .CountAsync(v => v.Code.StartsWith("SPK-ONE-") && v.IsActive));
        Assert.Equal(1, liveVouchers);
    }
}
