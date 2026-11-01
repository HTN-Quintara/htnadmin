using System.Net;
using Htn.Api.Tests.Infrastructure;
using Htn.Domain.Finance;
using Microsoft.EntityFrameworkCore;
using static Htn.Api.Tests.Events.EventTestData;
using static Htn.Api.Tests.Finance.FinanceTestData;

namespace Htn.Api.Tests.Finance;

/// <summary>
/// "Transfers between funds are recorded on both sides", and "when an event closes, its surplus transfers to the
/// Organization fund". Both are the one case in the ledger that writes two rows for one movement.
/// </summary>
public sealed class TransferAndSweepTests(HtnApiFactory factory) : IClassFixture<HtnApiFactory>
{
    [Fact]
    public async Task A_transfer_writes_both_sides_and_moves_nothing_overall()
    {
        var s = await SceneAsync(factory);
        var eventFund = await EventFundAsync(s, "3000.00");
        var before = await BalanceAsync(s, Funds.OrganizationId);

        var res = await s.CfoClient.PostAsync("/api/finance/ledger/transfers", new
        {
            fromFundId = eventFund,
            toFundId = Funds.OrganizationId,
            entryDate = "2026-10-20",
            amount = 1_000.00m,
            description = "Float back to the organization",
        });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var outLeg = await ApiClient.JsonAsync(res);
        var group = outLeg.GetProperty("transferGroupId").GetGuid();
        Assert.Equal("Out", outLeg.GetProperty("direction").GetString());

        var legs = await factory.DbAsync(db => db.LedgerEntries.AsNoTracking()
            .Where(e => e.TransferGroupId == group).ToListAsync());
        Assert.Equal(2, legs.Count);
        Assert.Single(legs, l => l.Direction == LedgerDirection.In);
        Assert.Single(legs, l => l.Direction == LedgerDirection.Out);
        Assert.Single(legs.Select(l => l.Amount).Distinct());
        Assert.Single(legs.Select(l => l.EntryDate).Distinct());
        Assert.Equal(2, legs.Select(l => l.FundId).Distinct().Count());
        // Money moved; none appeared.
        Assert.Equal(0m, legs.Sum(l => l.SignedAmount));

        Assert.Equal(before + 1_000.00m, await BalanceAsync(s, Funds.OrganizationId));
        Assert.Equal(2_000.00m, await BalanceAsync(s, eventFund));
    }

    [Fact]
    public async Task Voiding_one_leg_of_a_transfer_takes_the_other_with_it()
    {
        var s = await SceneAsync(factory);
        var eventFund = await EventFundAsync(s, "3000.00");
        var res = await s.CfoClient.PostAsync("/api/finance/ledger/transfers", new
        {
            fromFundId = eventFund, toFundId = Funds.OrganizationId, entryDate = "2026-10-20",
            amount = 500.00m, description = "Wrong direction",
        });
        var leg = await ApiClient.JsonAsync(res);
        var group = leg.GetProperty("transferGroupId").GetGuid();
        var legId = leg.GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.OK,
            (await s.CfoClient.PostAsync($"/api/finance/ledger/{legId}/void", new { reason = "Wrong direction" })).StatusCode);

        var legs = await factory.DbAsync(db => db.LedgerEntries.AsNoTracking()
            .Where(e => e.TransferGroupId == group).ToListAsync());
        Assert.Equal(2, legs.Count);
        Assert.All(legs, l => Assert.Equal(LedgerStatus.Voided, l.Status));
        Assert.All(legs, l => Assert.Equal("Wrong direction", l.VoidReason));
    }

    [Fact]
    public async Task A_transfer_needs_two_different_funds()
    {
        var s = await SceneAsync(factory);
        var res = await s.CfoClient.PostAsync("/api/finance/ledger/transfers", new
        {
            fromFundId = Funds.OrganizationId, toFundId = Funds.OrganizationId,
            entryDate = "2026-10-20", amount = 100.00m, description = "Nowhere",
        });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var problem = await ApiClient.JsonAsync(res);
        Assert.True(problem.GetProperty("errors").TryGetProperty("toFundId", out _));
    }

    [Fact]
    public async Task Sweeping_an_event_fund_moves_the_exact_surplus_once()
    {
        var s = await SceneAsync(factory);
        var eventFund = await EventFundAsync(s, "4200.50");
        var orgBefore = await BalanceAsync(s, Funds.OrganizationId);

        var res = await s.CfoClient.PostAsync($"/api/finance/funds/{eventFund}/sweep", new { entryDate = "2026-10-31" });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var fund = await ApiClient.JsonAsync(res);
        Assert.True(fund.GetProperty("isSwept").GetBoolean());
        Assert.Equal(0m, fund.GetProperty("balance").GetDecimal());
        Assert.False(fund.GetProperty("canSweep").GetBoolean());

        Assert.Equal(orgBefore + 4_200.50m, await BalanceAsync(s, Funds.OrganizationId));

        var group = await factory.DbAsync(db => db.Funds.AsNoTracking()
            .Where(f => f.Id == eventFund).Select(f => f.SweptByTransferGroupId!.Value).SingleAsync());
        var legs = await factory.DbAsync(db => db.LedgerEntries.AsNoTracking()
            .Where(e => e.TransferGroupId == group).ToListAsync());
        Assert.Equal(2, legs.Count);
        Assert.All(legs, l => Assert.Equal(LedgerSource.SurplusSweep, l.Source));
        Assert.All(legs, l => Assert.Equal(new DateOnly(2026, 10, 31), l.EntryDate));

        // Nothing left to sweep, and never twice.
        var again = await s.CfoClient.PostAsync($"/api/finance/funds/{eventFund}/sweep");
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("no_surplus", (await ApiClient.JsonAsync(again)).GetProperty("code").GetString());

        // Scoped to this fund: another test in the class sweeps one of its own.
        var audit = await factory.DbAsync(db => db.AuditEntries.AsNoTracking()
            .SingleAsync(a => a.Action == "finance.fund_sweep" && a.EntityId == eventFund.ToString()));
        Assert.Equal(s.Cfo.UserId, audit.ActorId);
    }

    [Fact]
    public async Task An_empty_event_fund_has_nothing_to_sweep_and_the_organization_fund_is_never_swept()
    {
        var s = await SceneAsync(factory);
        var empty = await EventFundAsync(s, "100.00", spendItAll: true);

        var nothing = await s.CfoClient.PostAsync($"/api/finance/funds/{empty}/sweep");
        Assert.Equal(HttpStatusCode.Conflict, nothing.StatusCode);
        Assert.Equal("no_surplus", (await ApiClient.JsonAsync(nothing)).GetProperty("code").GetString());

        var org = await s.CfoClient.PostAsync($"/api/finance/funds/{Funds.OrganizationId}/sweep");
        Assert.Equal(HttpStatusCode.Conflict, org.StatusCode);
        Assert.Equal("no_surplus", (await ApiClient.JsonAsync(org)).GetProperty("code").GetString());
    }


    [Fact]
    public async Task A_surplus_transfer_cannot_be_voided_because_the_sweep_can_never_be_unstamped()
    {
        var s = await SceneAsync(factory);
        var eventFund = await EventFundAsync(s, "1800.00");
        await s.CfoClient.PostAsync($"/api/finance/funds/{eventFund}/sweep", new { entryDate = "2026-10-31" });

        var legId = await factory.DbAsync(db => db.LedgerEntries.AsNoTracking()
            .Where(e => e.Source == LedgerSource.SurplusSweep && e.FundId == eventFund)
            .Select(e => e.Id).SingleAsync());

        var refused = await s.CfoClient.PostAsync($"/api/finance/ledger/{legId}/void", new { reason = "Wrong fund" });
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal("sweep_not_voidable", (await ApiClient.JsonAsync(refused)).GetProperty("code").GetString());

        // Both legs are untouched, so the money is still where the sweep put it.
        var legs = await factory.DbAsync(db => db.LedgerEntries.AsNoTracking()
            .Where(e => e.Source == LedgerSource.SurplusSweep && e.FundId == eventFund).ToListAsync());
        Assert.NotEmpty(legs);
        Assert.All(legs, l => Assert.Equal(LedgerStatus.Posted, l.Status));
    }

    // ---------- Helpers ----------

    /// <summary>A fresh event with its own fund, holding <paramref name="amount"/> (optionally spent back to zero).</summary>
    private async Task<Guid> EventFundAsync(FinanceScene s, string amount, bool spendItAll = false)
    {
        var eventId = await OnSaleAsync(s.BoardClient);
        // The fund appears the moment money touches the event; a manual entry is enough to create it here.
        await factory.DbAsync(async db =>
        {
            var ev = await db.Events.AsNoTracking().SingleAsync(e => e.Id == eventId);
            db.Funds.Add(Fund.ForEvent(ev, DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
            return 0;
        });
        var fundId = await factory.DbAsync(db => db.Funds.AsNoTracking()
            .Where(f => f.EventId == eventId).Select(f => f.Id).SingleAsync());

        await AddEntryOkAsync(s.CfoClient, fundId, s.AccountId, s.TicketsCategoryId, "2026-10-05", "In", amount, "Ticket sales");
        if (spendItAll)
            await AddEntryOkAsync(s.CfoClient, fundId, s.AccountId, s.TicketsCategoryId, "2026-10-06", "Out", amount, "All spent");
        return fundId;
    }

    private static async Task<decimal> BalanceAsync(FinanceScene s, Guid fundId)
    {
        var res = await s.CfoClient.GetAsync("/api/finance/funds");
        res.EnsureSuccessStatusCode();
        return (await ApiClient.JsonAsync(res)).EnumerateArray()
            .Single(f => f.GetProperty("id").GetGuid() == fundId)
            .GetProperty("balance").GetDecimal();
    }
}
