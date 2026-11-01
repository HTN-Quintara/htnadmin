using System.Net;
using Htn.Api.Features.Sponsors;
using Htn.Api.Tests.Infrastructure;
using Htn.Domain.Finance;
using Microsoft.EntityFrameworkCore;
using static Htn.Api.Tests.Sponsors.SponsorsTestData;

namespace Htn.Api.Tests.Sponsors;

public sealed class SponsorsEndpointsTests(HtnApiFactory factory) : IClassFixture<HtnApiFactory>
{
    [Fact]
    public async Task Creating_then_confirming_snapshots_the_tier_perks()
    {
        var s = await SceneAsync(factory);
        var tierId = await CreateTierAsync(s.Board, s.EventId);
        var sid = await CreateSponsorAsync(s.Board, s.EventId, new
        {
            kind = "Organization", name = "ToteWorks " + Guid.NewGuid().ToString("N")[..6],
            tierId, cashAmount = 150000m, compedSeats = 2,
        });

        var confirm = await s.Board.PostAsync($"/api/events/{s.EventId}/sponsors/{sid}/confirm");
        confirm.EnsureSuccessStatusCode();
        var detail = await ApiClient.JsonAsync(confirm);
        Assert.Equal("Confirmed", detail.Sponsor().GetProperty("status").GetString());
        Assert.Equal(2, detail.GetProperty("perks").GetArrayLength());
    }

    [Fact]
    public async Task A_sponsor_needs_a_contribution()
    {
        var s = await SceneAsync(factory);
        var res = await s.Board.PostAsync($"/api/events/{s.EventId}/sponsors", new
        {
            kind = "Organization", name = "Nothing Co", // no cash, no in-kind
        });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Recording_cash_auto_posts_it_to_the_event_fund()
    {
        var s = await SceneAsync(factory);
        var sid = await CreateSponsorAsync(s.Board, s.EventId, new
        {
            kind = "Organization", name = "SecureBank " + Guid.NewGuid().ToString("N")[..6], cashAmount = 80000m,
        });
        (await s.Board.PostAsync($"/api/events/{s.EventId}/sponsors/{sid}/confirm")).EnsureSuccessStatusCode();

        var cash = await CashReceivedAsync(s.Board, s.EventId, sid, s.AccountId);
        cash.EnsureSuccessStatusCode();
        Assert.True((await ApiClient.JsonAsync(cash)).Sponsor().GetProperty("cashReceived").GetBoolean());

        var posted = await factory.DbAsync(db => db.LedgerEntries.AsNoTracking()
            .AnyAsync(e => e.Source == LedgerSource.SponsorPayment && e.SourceId == sid
                           && e.Status == LedgerStatus.Posted && e.Amount == 80000m));
        Assert.True(posted, "the sponsor's cash should be a posted ledger entry keyed to the sponsor");
    }

    [Fact]
    public async Task Recording_cash_again_is_refused()
    {
        var s = await SceneAsync(factory);
        var sid = await CreateSponsorAsync(s.Board, s.EventId, new
        {
            kind = "Organization", name = "Twice Co " + Guid.NewGuid().ToString("N")[..6], cashAmount = 5000m,
        });
        (await s.Board.PostAsync($"/api/events/{s.EventId}/sponsors/{sid}/confirm")).EnsureSuccessStatusCode();
        (await CashReceivedAsync(s.Board, s.EventId, sid, s.AccountId)).EnsureSuccessStatusCode();

        var again = await CashReceivedAsync(s.Board, s.EventId, sid, s.AccountId);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal(SponsorErrorCodes.AlreadyReceived, (await ApiClient.JsonAsync(again)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Reversing_cash_in_an_open_month_voids_the_entry_and_lets_it_be_recorded_again()
    {
        var s = await SceneAsync(factory);
        var sid = await CreateSponsorAsync(s.Board, s.EventId, new
        {
            kind = "Organization", name = "Redo Co " + Guid.NewGuid().ToString("N")[..6], cashAmount = 12000m,
        });
        (await s.Board.PostAsync($"/api/events/{s.EventId}/sponsors/{sid}/confirm")).EnsureSuccessStatusCode();
        (await CashReceivedAsync(s.Board, s.EventId, sid, s.AccountId)).EnsureSuccessStatusCode();

        var reverse = await s.Board.PostAsync($"/api/events/{s.EventId}/sponsors/{sid}/cash-received/reverse",
            new { reason = "Wrong account" });
        reverse.EnsureSuccessStatusCode();
        Assert.False((await ApiClient.JsonAsync(reverse)).Sponsor().GetProperty("cashReceived").GetBoolean());

        var voided = await factory.DbAsync(db => db.LedgerEntries.AsNoTracking()
            .CountAsync(e => e.Source == LedgerSource.SponsorPayment && e.SourceId == sid && e.Status == LedgerStatus.Voided));
        Assert.Equal(1, voided);

        // The slot is free again (voided rows drop out of the unique index), so it can be re-recorded.
        (await CashReceivedAsync(s.Board, s.EventId, sid, s.AccountId)).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task A_sponsor_with_posted_cash_cannot_be_voided()
    {
        var s = await SceneAsync(factory);
        var sid = await CreateSponsorAsync(s.Board, s.EventId, new
        {
            kind = "Organization", name = "Locked Co " + Guid.NewGuid().ToString("N")[..6], cashAmount = 3000m,
        });
        (await s.Board.PostAsync($"/api/events/{s.EventId}/sponsors/{sid}/confirm")).EnsureSuccessStatusCode();
        (await CashReceivedAsync(s.Board, s.EventId, sid, s.AccountId)).EnsureSuccessStatusCode();

        var void1 = await s.Board.PostAsync($"/api/events/{s.EventId}/sponsors/{sid}/void", new { reason = "Mistake" });
        Assert.Equal(HttpStatusCode.Conflict, void1.StatusCode);
        Assert.Equal(SponsorErrorCodes.CashPosted, (await ApiClient.JsonAsync(void1)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Duplicate_sponsor_names_are_refused()
    {
        var s = await SceneAsync(factory);
        var name = "Acme " + Guid.NewGuid().ToString("N")[..6];
        await CreateSponsorAsync(s.Board, s.EventId, new { kind = "Organization", name, cashAmount = 1000m });
        var dup = await s.Board.PostAsync($"/api/events/{s.EventId}/sponsors",
            new { kind = "Organization", name = name.ToUpperInvariant(), cashAmount = 2000m });
        Assert.Equal(HttpStatusCode.Conflict, dup.StatusCode);
        Assert.Equal(SponsorErrorCodes.DuplicateName, (await ApiClient.JsonAsync(dup)).GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("Acme, Inc.")]
    [InlineData("UP Diliman")]
    [InlineData("(ABC) Corp.")]
    [InlineData("DICT Region 1")]
    [InlineData("Ayala Land, Inc.")]
    public async Task The_attendee_company_key_expression_matches_NameKey(string name)
    {
        // The generated company_key column and NameKey.Of must produce the same key, or a tagged partner's
        // registrant count silently misses. This guards that invariant for punctuated names.
        var computed = await factory.DbAsync(db => db.Database
            .SqlQuery<string?>($"SELECT nullif(btrim(regexp_replace(lower(btrim({name})), '[^a-z0-9]+', ' ', 'g')), '') AS \"Value\"")
            .SingleAsync());
        Assert.Equal(Htn.Domain.Common.NameKey.Of(name), computed);
    }

    [Fact]
    public async Task An_individual_may_be_anonymous()
    {
        var s = await SceneAsync(factory);
        var sid = await CreateSponsorAsync(s.Board, s.EventId, new
        {
            kind = "Individual", name = "Private Donor " + Guid.NewGuid().ToString("N")[..6],
            isAnonymous = true, cashAmount = 10000m,
        });
        var detail = await ApiClient.JsonAsync(await s.Board.GetAsync($"/api/events/{s.EventId}/sponsors/{sid}"));
        Assert.True(detail.Sponsor().GetProperty("isAnonymous").GetBoolean());
    }
}
