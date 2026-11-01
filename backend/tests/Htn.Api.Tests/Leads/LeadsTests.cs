using System.Net;
using System.Text.Json;
using Htn.Api.Features.Leads;
using Htn.Api.Tests.Infrastructure;
using Htn.Domain.Organization;
using Microsoft.EntityFrameworkCore;
using static Htn.Api.Tests.Committees.CommitteeTestData;
using static Htn.Api.Tests.Events.EventTestData;

namespace Htn.Api.Tests.Leads;

public sealed class LeadsTests(HtnApiFactory factory) : IClassFixture<HtnApiFactory>
{
    private sealed record Scene(ApiClient Board, ApiClient Goon, Guid EventId);

    private static async Task<Scene> SceneAsync(HtnApiFactory f)
    {
        var (_, board) = await SignedInUserAsync(f, PersonLevel.Board);
        var (_, goon) = await SignedInUserAsync(f, PersonLevel.Goon);
        var eventId = await OnSaleAsync(board);
        return new Scene(board, goon, eventId);
    }

    private static async Task<Guid> AddLeadAsync(ApiClient board, Guid eventId, object body)
    {
        var res = await board.PostAsync($"/api/events/{eventId}/leads", body);
        if (res.StatusCode != HttpStatusCode.OK)
            throw new InvalidOperationException($"Add lead failed: {res.StatusCode} {await res.Content.ReadAsStringAsync()}");
        var board2 = await ApiClient.JsonAsync(res);
        // newest Prospect card
        return board2.GetProperty("columns").EnumerateArray()
            .First(c => c.GetProperty("status").GetString() == "Prospect")
            .GetProperty("cards").EnumerateArray().Last().GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task A_goon_cannot_touch_leads()
    {
        var s = await SceneAsync(factory);
        Assert.Equal(HttpStatusCode.Forbidden, (await s.Goon.GetAsync($"/api/events/{s.EventId}/leads")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await s.Goon.PostAsync($"/api/events/{s.EventId}/leads", new { kind = "Sponsor", subject = "X" })).StatusCode);
    }

    [Fact]
    public async Task The_board_renders_five_columns_with_stats()
    {
        var s = await SceneAsync(factory);
        var board = await ApiClient.JsonAsync(await s.Board.GetAsync($"/api/events/{s.EventId}/leads?kind=Sponsor"));
        Assert.Equal(5, board.GetProperty("columns").GetArrayLength());
        Assert.Equal(3, board.GetProperty("stats").GetArrayLength());
    }

    [Fact]
    public async Task A_lead_walks_the_pipeline_and_declining_needs_a_reason()
    {
        var s = await SceneAsync(factory);
        var id = await AddLeadAsync(s.Board, s.EventId, new { kind = "Sponsor", subject = "Globe Business", askAmount = 100000m, askNote = "Gold" });

        (await s.Board.PostAsync($"/api/events/{s.EventId}/leads/{id}/status", new { status = "Contacted" })).EnsureSuccessStatusCode();

        var noReason = await s.Board.PostAsync($"/api/events/{s.EventId}/leads/{id}/status", new { status = "Declined" });
        Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);

        (await s.Board.PostAsync($"/api/events/{s.EventId}/leads/{id}/status", new { status = "Declined", reason = "Budget used" })).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Confirming_a_sponsor_lead_with_a_cash_ask_creates_the_sponsor()
    {
        var s = await SceneAsync(factory);
        var id = await AddLeadAsync(s.Board, s.EventId, new { kind = "Sponsor", subject = "SecureBank " + Guid.NewGuid().ToString("N")[..6], askAmount = 80000m });

        var res = await s.Board.PostAsync($"/api/events/{s.EventId}/leads/{id}/confirm");
        res.EnsureSuccessStatusCode();
        var result = await ApiClient.JsonAsync(res);
        var sponsorId = result.GetProperty("sponsorId").GetGuid();
        Assert.NotEqual(Guid.Empty, sponsorId);

        var linked = await factory.DbAsync(db => db.Sponsors.AsNoTracking().AnyAsync(sp => sp.Id == sponsorId && sp.EventId == s.EventId));
        Assert.True(linked, "the sponsor should exist, created from the lead");
    }

    [Fact]
    public async Task Confirming_a_speaker_lead_creates_the_speaker()
    {
        var s = await SceneAsync(factory);
        var email = $"lara.{Guid.NewGuid():N}@dict.gov.ph";
        var id = await AddLeadAsync(s.Board, s.EventId, new { kind = "Speaker", subject = "Dr. Lara Gomez", contactEmail = email });

        var res = await s.Board.PostAsync($"/api/events/{s.EventId}/leads/{id}/confirm");
        res.EnsureSuccessStatusCode();
        var speakerId = (await ApiClient.JsonAsync(res)).GetProperty("speakerId").GetGuid();
        Assert.NotEqual(Guid.Empty, speakerId);

        var speakers = await ApiClient.JsonAsync(await s.Board.GetAsync($"/api/events/{s.EventId}/speakers"));
        Assert.Contains(speakers.EnumerateArray(), sp => sp.GetProperty("id").GetGuid() == speakerId && sp.GetProperty("status").GetString() == "Confirmed");
    }

    [Fact]
    public async Task Confirming_a_speaker_lead_without_an_email_is_refused()
    {
        var s = await SceneAsync(factory);
        var id = await AddLeadAsync(s.Board, s.EventId, new { kind = "Speaker", subject = "No Email Speaker" });
        var res = await s.Board.PostAsync($"/api/events/{s.EventId}/leads/{id}/confirm");
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task A_speaker_can_be_created_directly()
    {
        var s = await SceneAsync(factory);
        var res = await s.Board.PostAsync($"/api/events/{s.EventId}/speakers",
            new { name = "Jon Rivera", email = $"jon.{Guid.NewGuid():N}@cloudcorp.ph" });
        res.EnsureSuccessStatusCode();
        Assert.Equal("Invited", (await ApiClient.JsonAsync(res)).GetProperty("status").GetString());
    }
}
