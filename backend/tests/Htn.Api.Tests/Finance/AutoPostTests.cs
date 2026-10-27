using System.Net;
using Htn.Api.Tests.Infrastructure;
using Htn.Domain.Finance;
using Htn.Domain.Organization;
using Microsoft.EntityFrameworkCore;
using static Htn.Api.Tests.Finance.FinanceTestData;
using static Htn.Api.Tests.Registrations.RegistrationTestData;

namespace Htn.Api.Tests.Finance;

/// <summary>
/// "Ticket, merch, sponsor and membership payments auto-post when marked paid." This covers the one source that
/// exists today, and it is the riskiest part of the slice because it changes a code path Phase 3 already ships:
/// posting happens inline, in the same transaction as the payment, so a balance is never momentarily wrong.
/// </summary>
public sealed class AutoPostTests(HtnApiFactory factory) : IClassFixture<HtnApiFactory>
{
    [Fact]
    public async Task Marking_a_registration_paid_posts_exactly_one_entry_to_the_events_fund()
    {
        var s = await SceneAsync(factory);
        var ev = await PublishedAsync(s.BoardClient);
        var (pid, regId, _) = await RegisterOneAsync(ev);

        Assert.Equal(HttpStatusCode.OK, (await s.BoardClient.PostAsync(
            $"/api/events/{ev.Id}/registrations/mark-paid",
            new { ids = new[] { regId }, method = "GCash", paymentAccountId = s.AccountId })).StatusCode);

        var payment = await factory.DbAsync(db => db.Payments.AsNoTracking().SingleAsync(p => p.RegistrationId == regId));
        var entries = await factory.DbAsync(db => db.LedgerEntries.AsNoTracking()
            .Where(e => e.Source == LedgerSource.TicketPayment && e.SourceId == payment.Id).ToListAsync());

        var entry = Assert.Single(entries);
        Assert.Equal(LedgerStatus.Posted, entry.Status);                 // money straight away, never pending
        Assert.Equal(LedgerDirection.In, entry.Direction);
        Assert.Equal(payment.Amount, entry.Amount);
        Assert.Equal(LedgerCategories.TicketsId, entry.CategoryId);
        Assert.Equal(s.AccountId, entry.PaymentAccountId);
        Assert.Contains(pid, entry.Description, StringComparison.Ordinal);
        Assert.NotNull(entry.ApprovedAt);

        // The event got its own fund, and the fund holds the money.
        var fund = await factory.DbAsync(db => db.Funds.AsNoTracking().SingleAsync(f => f.EventId == ev.Id));
        Assert.Equal(fund.Id, entry.FundId);
        Assert.Equal(FundKind.Event, fund.Kind);

        var audit = await factory.DbAsync(db => db.AuditEntries.AsNoTracking()
            .SingleAsync(a => a.Action == "ledger.auto_post" && a.EntityId == entry.Id.ToString()));
        Assert.NotNull(audit);
    }

    [Fact]
    public async Task Marking_several_paid_at_once_posts_one_entry_each_into_a_single_event_fund()
    {
        var s = await SceneAsync(factory);
        var ev = await PublishedAsync(s.BoardClient);
        var a = await RegisterOneAsync(ev);
        var b = await RegisterOneAsync(ev);
        var c = await RegisterOneAsync(ev);

        Assert.Equal(HttpStatusCode.OK, (await s.BoardClient.PostAsync(
            $"/api/events/{ev.Id}/registrations/mark-paid",
            new { ids = new[] { a.RegId, b.RegId, c.RegId }, method = "GCash", paymentAccountId = s.AccountId })).StatusCode);

        // One fund for the event, not one per payment: the first of the batch creates it and the rest reuse it.
        var funds = await factory.DbAsync(db => db.Funds.AsNoTracking().CountAsync(f => f.EventId == ev.Id));
        Assert.Equal(1, funds);

        var fundId = await factory.DbAsync(db => db.Funds.AsNoTracking()
            .Where(f => f.EventId == ev.Id).Select(f => f.Id).SingleAsync());
        var entries = await factory.DbAsync(db => db.LedgerEntries.AsNoTracking()
            .Where(e => e.FundId == fundId && e.Source == LedgerSource.TicketPayment).ToListAsync());
        Assert.Equal(3, entries.Count);
        Assert.All(entries, e => Assert.Equal(LedgerStatus.Posted, e.Status));
        Assert.Equal(3, entries.Select(e => e.SourceId).Distinct().Count());
    }

    [Fact]
    public async Task A_comp_and_a_free_registration_move_no_money_so_nothing_is_posted()
    {
        var s = await SceneAsync(factory);
        var ev = await PublishedAsync(s.BoardClient, ticket: new { name = "Free", price = 0, quantity = 50 });
        await RegisterOneAsync(ev);

        // A zero total is paid automatically at checkout, and posts nothing.
        var paidEvent = await PublishedAsync(s.BoardClient);
        var comp = await RegisterOneAsync(paidEvent);
        Assert.Equal(HttpStatusCode.OK, (await s.BoardClient.PostAsync(
            $"/api/events/{paidEvent.Id}/registrations/mark-paid",
            new { ids = new[] { comp.RegId }, method = "Comp", reason = "Speaker's guest" })).StatusCode);

        // No fund was conjured for either event, because neither ever took money — which is also why there is
        // nothing to post.
        Assert.Equal(0, await factory.DbAsync(db => db.Funds.AsNoTracking()
            .CountAsync(f => f.EventId == ev.Id || f.EventId == paidEvent.Id)));
        Assert.Equal(0, await factory.DbAsync(db => db.LedgerEntries.AsNoTracking()
            .CountAsync(e => e.Source == LedgerSource.TicketPayment
                             && db.Funds.Any(f => f.Id == e.FundId && (f.EventId == ev.Id || f.EventId == paidEvent.Id)))));
    }

    [Fact]
    public async Task Reverting_a_payment_in_an_open_month_voids_its_entry()
    {
        var s = await SceneAsync(factory);
        var ev = await PublishedAsync(s.BoardClient);
        var (_, regId, _) = await RegisterOneAsync(ev);
        await s.BoardClient.PostAsync($"/api/events/{ev.Id}/registrations/mark-paid",
            new { ids = new[] { regId }, method = "GCash", paymentAccountId = s.AccountId });

        Assert.Equal(HttpStatusCode.OK, (await s.BoardClient.PostAsync(
            $"/api/events/{ev.Id}/registrations/{regId}/revert-payment", new { reason = "Payment bounced" })).StatusCode);

        // Scoped to this event's fund: the class shares one database with the other auto-post tests.
        var fundId = await FundAsync(ev.Id);
        var entry = await factory.DbAsync(db => db.LedgerEntries.AsNoTracking()
            .SingleAsync(e => e.FundId == fundId && e.Source == LedgerSource.TicketPayment));
        Assert.Equal(LedgerStatus.Voided, entry.Status);
        Assert.Equal("Payment bounced", entry.VoidReason);

        // The fund is back to nothing, and no contra entry was needed.
        Assert.Equal(0m, await BalanceAsync(s, fundId));
        Assert.Equal(0, await factory.DbAsync(db => db.LedgerEntries.AsNoTracking()
            .CountAsync(e => e.FundId == fundId && e.Source == LedgerSource.Contra)));
    }

    [Fact]
    public async Task Reverting_a_payment_in_a_closed_month_offsets_it_with_a_contra_entry_instead()
    {
        var s = await SceneAsync(factory);
        var ev = await PublishedAsync(s.BoardClient);
        var (_, regId, _) = await RegisterOneAsync(ev);
        await s.BoardClient.PostAsync($"/api/events/{ev.Id}/registrations/mark-paid",
            new { ids = new[] { regId }, method = "GCash", paymentAccountId = s.AccountId });

        var fundId = await FundAsync(ev.Id);
        var entry = await factory.DbAsync(db => db.LedgerEntries.AsNoTracking()
            .SingleAsync(e => e.FundId == fundId && e.Source == LedgerSource.TicketPayment));

        // Close the month the payment landed in.
        await ReconcileAllAsync(s.CfoClient, entry.EntryDate.Year, entry.EntryDate.Month);
        var closed = await s.CfoClient.PostAsync($"/api/finance/periods/{entry.EntryDate.Year}/{entry.EntryDate.Month}/close");
        Assert.Equal(HttpStatusCode.OK, closed.StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await s.BoardClient.PostAsync(
            $"/api/events/{ev.Id}/registrations/{regId}/revert-payment", new { reason = "Payment bounced" })).StatusCode);

        // The original is untouched — a closed month is settled — and an opposite entry cancels it out.
        var original = await factory.DbAsync(db => db.LedgerEntries.AsNoTracking().SingleAsync(e => e.Id == entry.Id));
        Assert.Equal(LedgerStatus.Posted, original.Status);
        Assert.NotNull(original.MonthCloseId);

        var contra = await factory.DbAsync(db => db.LedgerEntries.AsNoTracking()
            .SingleAsync(e => e.FundId == fundId && e.Source == LedgerSource.Contra));
        Assert.Equal(entry.Id, contra.ContraOfEntryId);
        Assert.Equal(LedgerDirection.Out, contra.Direction);
        Assert.Equal(original.Amount, contra.Amount);
        Assert.Equal(0m, original.SignedAmount + contra.SignedAmount);
        Assert.Contains("Payment bounced", contra.Description, StringComparison.Ordinal);
        // Dated out of the closed month, into the first one still open.
        Assert.True(contra.EntryDate >= new DateOnly(entry.EntryDate.Year, entry.EntryDate.Month, 1).AddMonths(1));
        Assert.Equal(0m, await BalanceAsync(s, fundId));
    }

    [Fact]
    public async Task A_payment_is_never_posted_twice_even_if_it_is_reverted_and_taken_again()
    {
        var s = await SceneAsync(factory);
        var ev = await PublishedAsync(s.BoardClient);
        var (_, regId, _) = await RegisterOneAsync(ev);
        var path = $"/api/events/{ev.Id}/registrations";

        await s.BoardClient.PostAsync($"{path}/mark-paid", new { ids = new[] { regId }, method = "GCash", paymentAccountId = s.AccountId });
        await s.BoardClient.PostAsync($"{path}/{regId}/revert-payment", new { reason = "Wrong row" });
        // Paying again makes a new payment row, so a new live entry is correct — the index only forbids two live
        // entries for the *same* payment.
        await s.BoardClient.PostAsync($"{path}/mark-paid", new { ids = new[] { regId }, method = "GCash", paymentAccountId = s.AccountId });

        var fundId = await FundAsync(ev.Id);
        var live = await factory.DbAsync(db => db.LedgerEntries.AsNoTracking()
            .CountAsync(e => e.FundId == fundId && e.Source == LedgerSource.TicketPayment && e.Status != LedgerStatus.Voided));
        Assert.Equal(1, live);
        var all = await factory.DbAsync(db => db.LedgerEntries.AsNoTracking()
            .CountAsync(e => e.FundId == fundId && e.Source == LedgerSource.TicketPayment));
        Assert.Equal(2, all);
    }


    [Fact]
    public async Task A_payment_taken_after_the_books_close_lands_in_the_next_open_month()
    {
        var s = await SceneAsync(factory);
        var ev = await PublishedAsync(s.BoardClient);
        var (_, firstId, _) = await RegisterOneAsync(ev);
        await s.BoardClient.PostAsync($"/api/events/{ev.Id}/registrations/mark-paid",
            new { ids = new[] { firstId }, method = "GCash", paymentAccountId = s.AccountId });

        var fundId = await FundAsync(ev.Id);
        var first = await factory.DbAsync(db => db.LedgerEntries.AsNoTracking()
            .SingleAsync(e => e.FundId == fundId && e.Source == LedgerSource.TicketPayment));
        await ReconcileAllAsync(s.CfoClient, first.EntryDate.Year, first.EntryDate.Month);
        Assert.Equal(HttpStatusCode.OK,
            (await s.CfoClient.PostAsync($"/api/finance/periods/{first.EntryDate.Year}/{first.EntryDate.Month}/close")).StatusCode);

        // The desk keeps taking money after the books close; the entry simply lands in the next open month.
        var (_, secondId, _) = await RegisterOneAsync(ev);
        Assert.Equal(HttpStatusCode.OK, (await s.BoardClient.PostAsync(
            $"/api/events/{ev.Id}/registrations/mark-paid",
            new { ids = new[] { secondId }, method = "GCash", paymentAccountId = s.AccountId })).StatusCode);

        var entries = await factory.DbAsync(db => db.LedgerEntries.AsNoTracking()
            .Where(e => e.FundId == fundId && e.Source == LedgerSource.TicketPayment)
            .OrderBy(e => e.EntryDate).ToListAsync());
        Assert.Equal(2, entries.Count);
        Assert.Equal(new DateOnly(first.EntryDate.Year, first.EntryDate.Month, 1).AddMonths(1), entries[1].EntryDate);
        Assert.Equal(LedgerStatus.Posted, entries[1].Status);
    }


    // ---------- Helpers ----------

    private async Task<(string Pid, Guid RegId, string Email)> RegisterOneAsync(Registrations.OnSaleEvent ev)
    {
        var email = HtnApiFactory.UniqueEmail("fin");
        var created = await RegisterOkAsync(factory.NewClient(), ev.Slug, Body(ev.TicketId, [Person(email)]));
        var pid = created.GetProperty("participantIds")[0].GetString()!;
        return (pid, await RegistrationIdAsync(factory, pid), email);
    }

    private Task<Guid> FundAsync(Guid eventId) =>
        factory.DbAsync(db => db.Funds.AsNoTracking().Where(f => f.EventId == eventId).Select(f => f.Id).SingleAsync());

    private static async Task<decimal> BalanceAsync(FinanceScene s, Guid fundId)
    {
        var res = await s.CfoClient.GetAsync("/api/finance/funds");
        res.EnsureSuccessStatusCode();
        return (await ApiClient.JsonAsync(res)).EnumerateArray()
            .Single(f => f.GetProperty("id").GetGuid() == fundId).GetProperty("balance").GetDecimal();
    }
}
