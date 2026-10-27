using System.Net;
using Htn.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using static Htn.Api.Tests.Finance.FinanceTestData;

namespace Htn.Api.Tests.Finance;

/// <summary>
/// "A month can only be closed when everything is balanced and nothing is pending."
/// <para>
/// One test, walked end to end on purpose: a month close is global state with an ordering rule across months, so
/// separate tests sharing this class's database would interfere with each other rather than with reality.
/// </para>
/// </summary>
public sealed class MonthCloseTests(HtnApiFactory factory) : IClassFixture<HtnApiFactory>
{
    [Fact]
    public async Task A_month_closes_only_when_it_is_settled_and_then_freezes()
    {
        var s = await SceneAsync(factory);
        var cfo = s.CfoClient;

        // October: one posted entry and one the assistant added, which is not money yet.
        await AddEntryOkAsync(cfo, s.OrgFundId, s.AccountId, s.TicketsCategoryId, "2026-10-05", "In", "2500.00", "Ticket sales");
        var pendingId = await AddEntryOkAsync(s.AssistantClient, s.OrgFundId, s.AccountId, s.TicketsCategoryId,
            "2026-10-06", "Out", "100.00", "Snacks");

        // 1. Blocked while anything is pending.
        var pending = await cfo.PostAsync("/api/finance/periods/2026/10/close", new { note = "try" });
        Assert.Equal(HttpStatusCode.Conflict, pending.StatusCode);
        Assert.Equal("month_has_pending", (await ApiClient.JsonAsync(pending)).GetProperty("code").GetString());

        await cfo.PostAsync($"/api/finance/ledger/{pendingId}/void", new { reason = "Not ours after all" });

        // 2. Blocked while an account is unreconciled, and the message names it.
        var unbalanced = await cfo.PostAsync("/api/finance/periods/2026/10/close");
        Assert.Equal(HttpStatusCode.Conflict, unbalanced.StatusCode);
        var problem = await ApiClient.JsonAsync(unbalanced);
        Assert.Equal("month_unbalanced", problem.GetProperty("code").GetString());
        Assert.Contains("GCash", problem.GetProperty("detail").GetString()!, StringComparison.Ordinal);

        // 3. Reconciling to the wrong figure does not count as balanced.
        var wrong = await cfo.PostAsync($"/api/finance/accounts/{s.AccountId}/reconcile",
            new { year = 2026, month = 10, statementBalance = 9_999.00m });
        Assert.Equal(HttpStatusCode.OK, wrong.StatusCode);
        Assert.False((await ApiClient.JsonAsync(wrong)).GetProperty("isBalanced").GetBoolean());
        Assert.Equal("month_unbalanced",
            (await ApiClient.JsonAsync(await cfo.PostAsync("/api/finance/periods/2026/10/close"))).GetProperty("code").GetString());

        // 4. Reconciled to the book balance: now it closes. Re-reconciling supersedes, never edits.
        await ReconcileAllAsync(cfo, 2026, 10);
        var live = await factory.DbAsync(db => db.AccountReconciliations.AsNoTracking()
            .CountAsync(r => r.PaymentAccountId == s.AccountId && r.Year == 2026 && r.Month == 10 && r.SupersededAt == null));
        Assert.Equal(1, live);
        var superseded = await factory.DbAsync(db => db.AccountReconciliations.AsNoTracking()
            .CountAsync(r => r.PaymentAccountId == s.AccountId && r.Year == 2026 && r.Month == 10 && r.SupersededAt != null));
        Assert.Equal(1, superseded);

        var closed = await cfo.PostAsync("/api/finance/periods/2026/10/close", new { note = "All tied out" });
        Assert.Equal(HttpStatusCode.OK, closed.StatusCode);
        var period = await ApiClient.JsonAsync(closed);
        Assert.True(period.GetProperty("isClosed").GetBoolean());
        Assert.Equal("October 2026", period.GetProperty("label").GetString());
        Assert.True(period.GetProperty("canReopen").GetBoolean());
        Assert.False(period.GetProperty("canClose").GetBoolean());

        // Closing stamped the posted entries, which is what freezes them.
        var stamped = await factory.DbAsync(db => db.LedgerEntries.AsNoTracking()
            .CountAsync(e => e.MonthCloseId != null));
        Assert.Equal(1, stamped);

        // 5. Closing again is a conflict.
        Assert.Equal("month_already_closed",
            (await ApiClient.JsonAsync(await cfo.PostAsync("/api/finance/periods/2026/10/close"))).GetProperty("code").GetString());

        // 6. Nothing new may be dated into it, and the frozen entry cannot be voided.
        var late = await AddEntryAsync(cfo, s.OrgFundId, s.AccountId, s.TicketsCategoryId, "2026-10-20", "Out", "50.00");
        Assert.Equal(HttpStatusCode.Conflict, late.StatusCode);
        Assert.Equal("month_closed", (await ApiClient.JsonAsync(late)).GetProperty("code").GetString());

        var frozenId = await factory.DbAsync(db => db.LedgerEntries.AsNoTracking()
            .Where(e => e.MonthCloseId != null).Select(e => e.Id).SingleAsync());
        var frozen = await cfo.PostAsync($"/api/finance/ledger/{frozenId}/void", new { reason = "Too late" });
        Assert.Equal(HttpStatusCode.Conflict, frozen.StatusCode);
        Assert.Equal("month_closed", (await ApiClient.JsonAsync(frozen)).GetProperty("code").GetString());

        // A correcting entry dated in an open month is the way to fix it.
        Assert.Equal(HttpStatusCode.OK,
            (await AddEntryAsync(cfo, s.OrgFundId, s.AccountId, s.TicketsCategoryId, "2026-11-02", "Out", "2500.00", "Correction")).StatusCode);

        // 7. Months close in order: November is blocked by nothing now, but reopening October is refused once
        //    November is closed.
        await ReconcileAllAsync(cfo, 2026, 11);
        Assert.Equal(HttpStatusCode.OK, (await cfo.PostAsync("/api/finance/periods/2026/11/close")).StatusCode);

        var outOfOrder = await cfo.PostAsync("/api/finance/periods/2026/10/reopen", new { reason = "A late invoice" });
        Assert.Equal(HttpStatusCode.Conflict, outOfOrder.StatusCode);
        Assert.Equal("reopen_latest_only", (await ApiClient.JsonAsync(outOfOrder)).GetProperty("code").GetString());

        // 8. Reopening the latest month needs a reason, and releases its entries.
        Assert.Equal(HttpStatusCode.BadRequest,
            (await cfo.PostAsync("/api/finance/periods/2026/11/reopen", new { reason = "" })).StatusCode);
        var reopened = await cfo.PostAsync("/api/finance/periods/2026/11/reopen", new { reason = "A supplier invoice arrived late" });
        Assert.Equal(HttpStatusCode.OK, reopened.StatusCode);
        Assert.False((await ApiClient.JsonAsync(reopened)).GetProperty("isClosed").GetBoolean());

        // 9. Every one of those steps is in the audit log.
        var actions = await factory.DbAsync(db => db.AuditEntries.AsNoTracking()
            .Where(a => a.Action.StartsWith("finance.")).Select(a => a.Action).ToListAsync());
        Assert.Contains("finance.month_close", actions);
        Assert.Contains("finance.month_reopen", actions);
        Assert.Contains("finance.account_reconcile", actions);
    }
}
