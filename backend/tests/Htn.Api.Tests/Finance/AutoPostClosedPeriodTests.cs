using System.Net;
using Htn.Api.Tests.Infrastructure;
using Htn.Domain.Finance;
using Microsoft.EntityFrameworkCore;
using static Htn.Api.Tests.Finance.FinanceTestData;
using static Htn.Api.Tests.Registrations.RegistrationTestData;

namespace Htn.Api.Tests.Finance;

/// <summary>
/// Automatic posting around a certified period. Its own class, so its own database: it closes the current month, and
/// a month close is global state that would change what every other test in a shared database sees.
/// </summary>
public sealed class AutoPostClosedPeriodTests(HtnApiFactory factory) : IClassFixture<HtnApiFactory>
{
    [Fact]
    public async Task An_automatic_post_never_lands_in_a_month_before_a_certified_close()
    {
        var s = await SceneAsync(factory);
        var ev = await PublishedAsync(s.BoardClient);

        // Close this month, so every month before it is certified-or-earlier territory.
        var today = Htn.Domain.Common.ManilaTime.ManilaDate(DateTimeOffset.UtcNow);
        await ReconcileAllAsync(s.CfoClient, today.Year, today.Month);
        Assert.Equal(HttpStatusCode.OK,
            (await s.CfoClient.PostAsync($"/api/finance/periods/{today.Year}/{today.Month}/close")).StatusCode);

        // A payment received two months ago, marked paid only now. Its own month was never closed — no entries, so no
        // close row — but posting into it would change a balance that has already been signed off.
        var (_, regId, _) = await RegisterOneAsync(ev);
        var backdated = DateTimeOffset.UtcNow.AddMonths(-2);
        Assert.Equal(HttpStatusCode.OK, (await s.BoardClient.PostAsync(
            $"/api/events/{ev.Id}/registrations/mark-paid",
            new { ids = new[] { regId }, method = "GCash", paymentAccountId = s.AccountId, receivedAt = backdated })).StatusCode);

        var fundId = await FundAsync(ev.Id);
        var entry = await factory.DbAsync(db => db.LedgerEntries.AsNoTracking()
            .SingleAsync(e => e.FundId == fundId && e.Source == LedgerSource.TicketPayment));

        // It landed after the closed month, not in the month it was received.
        var afterClose = new DateOnly(today.Year, today.Month, 1).AddMonths(1);
        Assert.True(entry.EntryDate >= afterClose,
            $"expected the entry to land on or after {afterClose}, but it landed on {entry.EntryDate}");
        Assert.Equal(LedgerStatus.Posted, entry.Status);
        Assert.Null(entry.MonthCloseId);
    }

    private async Task<(string Pid, Guid RegId, string Email)> RegisterOneAsync(Registrations.OnSaleEvent ev)
    {
        var email = HtnApiFactory.UniqueEmail("fin");
        var created = await RegisterOkAsync(factory.NewClient(), ev.Slug, Body(ev.TicketId, [Person(email)]));
        var pid = created.GetProperty("participantIds")[0].GetString()!;
        return (pid, await RegistrationIdAsync(factory, pid), email);
    }

    private Task<Guid> FundAsync(Guid eventId) =>
        factory.DbAsync(db => db.Funds.AsNoTracking().Where(f => f.EventId == eventId).Select(f => f.Id).SingleAsync());
}
