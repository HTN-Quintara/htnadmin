using Htn.Api.Common;
using Htn.Domain.Common;
using Htn.Domain.Finance;
using Htn.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Htn.Api.Features.Finance;

/// <summary>The <c>code</c> extension on every finance problem response, mirrored in the frontend's error messages.</summary>
public static class FinanceErrorCodes
{
    public const string MonthClosed = "month_closed";
    public const string MonthHasPending = "month_has_pending";
    public const string MonthUnbalanced = "month_unbalanced";
    public const string PreviousMonthOpen = "previous_month_open";
    public const string MonthAlreadyClosed = "month_already_closed";
    public const string MonthNotClosed = "month_not_closed";
    public const string ReopenLatestOnly = "reopen_latest_only";
    public const string EntryNotPending = "entry_not_pending";
    public const string EntryVoided = "entry_voided";
    public const string AlreadyPosted = "already_posted";
    public const string SelfApproval = "self_approval";
    public const string ReceiptRequired = "receipt_required";
    public const string UnsupportedType = "unsupported_type";
    public const string TransferSameFund = "transfer_same_fund";
    public const string FundClosed = "fund_closed";
    public const string NoSurplus = "no_surplus";
    public const string SweepNotVoidable = "sweep_not_voidable";
    public const string NotReconcilable = "not_reconcilable";
    public const string UnknownCategory = "unknown_category";
    public const string CategoryInactive = "category_inactive";
    public const string UnknownAccount = "unknown_account";
    public const string AccountInactive = "account_inactive";
    public const string UnknownFund = "unknown_fund";
    public const string RequestDecided = "request_decided";
    public const string RequestNotApproved = "request_not_approved";
    public const string RequestAlreadyPaid = "request_already_paid";
}

/// <summary>Audit action strings, <c>&lt;entity&gt;.&lt;verb&gt;</c> like the rest of the system.</summary>
public static class FinanceAuditActions
{
    public const string Prefix = "finance.";
    public const string LedgerPrefix = "ledger.";

    public const string LedgerCreate = "ledger.create";
    public const string LedgerApprove = "ledger.approve";
    public const string LedgerVoid = "ledger.void";
    public const string LedgerTransfer = "ledger.transfer";
    public const string LedgerAutoPost = "ledger.auto_post";
    public const string LedgerContra = "ledger.contra";
    public const string MonthClose = "finance.month_close";
    public const string MonthReopen = "finance.month_reopen";
    public const string AccountReconcile = "finance.account_reconcile";
    public const string FundSweep = "finance.fund_sweep";
    public const string BudgetSave = "finance.budget_save";
    public const string ExportLedger = "finance.export_ledger";
    public const string ExportAudit = "finance.export_audit";
    public const string Backfill = "finance.backfill";
    public const string RequestCreate = "expense_request.create";
    public const string RequestApprove = "expense_request.approve";
    public const string RequestReject = "expense_request.reject";
    public const string RequestPay = "expense_request.pay";
    public const string RequestCancel = "expense_request.cancel";

    /// <summary>Every action the finance audit tab shows: the ledger, the periods, the budgets and the requests.</summary>
    public static readonly IReadOnlyList<string> All =
    [
        LedgerCreate, LedgerApprove, LedgerVoid, LedgerTransfer, LedgerAutoPost, LedgerContra,
        MonthClose, MonthReopen, AccountReconcile, FundSweep, BudgetSave, ExportLedger, ExportAudit, Backfill,
        RequestCreate, RequestApprove, RequestReject, RequestPay, RequestCancel,
    ];
}

/// <summary>
/// The derived numbers. Nothing in finance stores a balance: every figure here is a sum over
/// <c>ledger_entries WHERE status = 'Posted'</c>, so a pending entry is not money yet and a voided one never was.
/// </summary>
public static class FinanceOps
{
    /// <summary>Posted entries only — the one predicate every balance in the system agrees on.</summary>
    public static IQueryable<LedgerEntry> Posted(HtnDbContext db) =>
        db.LedgerEntries.AsNoTracking().Where(e => e.Status == LedgerStatus.Posted);

    /// <summary>The signed sum of a fund's posted entries.</summary>
    public static async Task<decimal> FundBalanceAsync(HtnDbContext db, Guid fundId, CancellationToken ct) =>
        await Posted(db).Where(e => e.FundId == fundId)
            .SumAsync(e => e.Direction == LedgerDirection.In ? e.Amount : -e.Amount, ct);

    /// <summary>Fund id → balance, for every fund at once.</summary>
    public static async Task<Dictionary<Guid, decimal>> FundBalancesAsync(HtnDbContext db, CancellationToken ct) =>
        await Posted(db)
            .GroupBy(e => e.FundId)
            .Select(g => new { FundId = g.Key, Balance = g.Sum(e => e.Direction == LedgerDirection.In ? e.Amount : -e.Amount) })
            .ToDictionaryAsync(r => r.FundId, r => r.Balance, ct);

    /// <summary>
    /// What the books say an account holds: its opening balance plus the signed sum of its posted entries up to
    /// <paramref name="asOf"/>. This is the figure the CFO compares with the statement or the counted cash.
    /// </summary>
    public static async Task<Dictionary<Guid, decimal>> BookBalancesAsync(HtnDbContext db, DateOnly asOf, CancellationToken ct)
    {
        var movements = await Posted(db)
            .Where(e => e.PaymentAccountId != null && e.EntryDate <= asOf)
            .GroupBy(e => e.PaymentAccountId!.Value)
            .Select(g => new { AccountId = g.Key, Sum = g.Sum(e => e.Direction == LedgerDirection.In ? e.Amount : -e.Amount) })
            .ToDictionaryAsync(r => r.AccountId, r => r.Sum, ct);

        var accounts = await db.PaymentAccounts.AsNoTracking()
            .Select(a => new { a.Id, a.OpeningBalance }).ToListAsync(ct);

        return accounts.ToDictionary(a => a.Id, a => a.OpeningBalance + movements.GetValueOrDefault(a.Id));
    }

    public static async Task<decimal> BookBalanceAsync(HtnDbContext db, Guid accountId, DateOnly asOf, CancellationToken ct)
    {
        var opening = await db.PaymentAccounts.AsNoTracking()
            .Where(a => a.Id == accountId).Select(a => a.OpeningBalance).SingleAsync(ct);
        var movements = await Posted(db)
            .Where(e => e.PaymentAccountId == accountId && e.EntryDate <= asOf)
            .SumAsync(e => e.Direction == LedgerDirection.In ? e.Amount : -e.Amount, ct);
        return opening + movements;
    }

    /// <summary>Money in and out per calendar month over a half-open date range, optionally for one fund.</summary>
    public static async Task<List<CashflowPointDto>> CashflowAsync(
        HtnDbContext db, DateOnly from, DateOnly toExclusive, Guid? fundId, CancellationToken ct)
    {
        var rows = await Posted(db)
            .Where(e => e.EntryDate >= from && e.EntryDate < toExclusive)
            .Where(e => fundId == null || e.FundId == fundId)
            .GroupBy(e => new { e.EntryDate.Year, e.EntryDate.Month })
            .Select(g => new
            {
                g.Key.Year,
                g.Key.Month,
                In = g.Sum(e => e.Direction == LedgerDirection.In ? e.Amount : 0m),
                Out = g.Sum(e => e.Direction == LedgerDirection.Out ? e.Amount : 0m),
            })
            .ToListAsync(ct);

        return [.. rows
            .OrderBy(r => r.Year).ThenBy(r => r.Month)
            .Select(r => new CashflowPointDto(new DateOnly(r.Year, r.Month, 1), r.In, r.Out, r.In - r.Out))];
    }

    /// <summary>Category id → signed actual for one fund. The source of "actuals come from the ledger".</summary>
    public static async Task<Dictionary<Guid, decimal>> ActualsByCategoryAsync(
        HtnDbContext db, Guid fundId, CancellationToken ct) =>
        await Posted(db).Where(e => e.FundId == fundId)
            .GroupBy(e => e.CategoryId)
            .Select(g => new { CategoryId = g.Key, Sum = g.Sum(e => e.Direction == LedgerDirection.In ? e.Amount : -e.Amount) })
            .ToDictionaryAsync(r => r.CategoryId, r => r.Sum, ct);

    // ---------- Funds ----------

    /// <summary>The Organization fund, which the Finance_Ledger migration seeded with a fixed id.</summary>
    public static async Task<Fund> OrganizationFundAsync(HtnDbContext db, CancellationToken ct) =>
        await db.Funds.SingleAsync(f => f.Id == Funds.OrganizationId, ct);

    /// <summary>
    /// The fund for an event, created on first use. Events get a fund the moment money touches them, which keeps the
    /// events feature free of any finance concern.
    /// </summary>
    public static async Task<Fund> EventFundAsync(HtnDbContext db, Guid eventId, DateTimeOffset at, CancellationToken ct)
    {
        // The change tracker first: a bulk mark-paid posts several payments for one event in a single transaction, and
        // the fund added for the first of them is not in the database yet. Querying past it would add a second fund
        // and trip ux_funds_event at save time.
        var pending = db.Funds.Local.FirstOrDefault(f => f.EventId == eventId);
        if (pending is not null) return pending;
        var fund = await db.Funds.FirstOrDefaultAsync(f => f.EventId == eventId, ct);
        if (fund is not null) return fund;
        var ev = await db.Events.AsNoTracking().SingleAsync(e => e.Id == eventId, ct);
        fund = Fund.ForEvent(ev, at);
        db.Funds.Add(fund);
        return fund;
    }

    // ---------- Periods ----------

    /// <summary>The live close row for a Manila month, or null when the month is open.</summary>
    public static Task<MonthClose?> OpenCloseAsync(HtnDbContext db, int year, int month, CancellationToken ct) =>
        db.MonthCloses.FirstOrDefaultAsync(m => m.Year == year && m.Month == month && m.ReopenedAt == null, ct);

    /// <summary>
    /// Serializes everything that depends on which months are closed. Closing reads "is anything pending or
    /// unbalanced?" and then writes; a ledger insert reads "is that month closed?" and then writes. Without a shared
    /// lock both can pass their checks and commit, leaving a posted entry inside a month that was certified without
    /// it — invisible, because the deferred trigger in the inserting transaction cannot see the close that has not
    /// committed yet.
    /// <para>
    /// Callers that also hold the event lock (the registration mark-paid path) always take that one first, so the
    /// order is event then finance everywhere and there is no cycle.
    /// </para>
    /// </summary>
    public static Task LockPeriodsAsync(HtnDbContext db, CancellationToken ct) =>
        db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(hashtext('htn:finance:periods'))", ct);

    /// <summary>True when an entry dated <paramref name="date"/> would land in a closed month.</summary>
    public static Task<bool> IsClosedAsync(HtnDbContext db, DateOnly date, CancellationToken ct) =>
        db.MonthCloses.AsNoTracking()
            .AnyAsync(m => m.ReopenedAt == null && m.Year == date.Year && m.Month == date.Month, ct);

    /// <summary>
    /// Why this month cannot be closed yet. Empty means it can: nothing pending in or before the period, every active
    /// reconcilable account balanced for it, and no earlier month holding entries still open.
    /// </summary>
    public static async Task<List<MonthBlockerDto>> CloseBlockersAsync(
        HtnDbContext db, MonthWindow window, CancellationToken ct)
    {
        var blockers = new List<MonthBlockerDto>();

        // Pending entries are not money yet, so closing over them would freeze a half-finished period.
        var pending = await db.LedgerEntries.AsNoTracking()
            .CountAsync(e => e.Status == LedgerStatus.Pending && e.EntryDate < window.EndExclusive, ct);
        if (pending > 0)
            blockers.Add(new MonthBlockerDto(FinanceErrorCodes.MonthHasPending,
                pending == 1
                    ? "1 entry is still waiting for your approval."
                    : $"{pending} entries are still waiting for your approval."));

        // A deactivated account can never block a close: it holds no money any more.
        // Deactivating an account does not make its money disappear: an account is excluded only when it is both out
        // of use and has never held a posted entry in or before this period.
        var moved = await Posted(db)
            .Where(e => e.PaymentAccountId != null && e.EntryDate < window.EndExclusive)
            .Select(e => e.PaymentAccountId!.Value)
            .Distinct()
            .ToListAsync(ct);
        var accounts = await db.PaymentAccounts.AsNoTracking()
            .Where(a => a.IsReconcilable && (a.IsActive || moved.Contains(a.Id)))
            .OrderBy(a => a.Label)
            .Select(a => new { a.Id, a.Label })
            .ToListAsync(ct);
        var reconciliations = await db.AccountReconciliations.AsNoTracking()
            .Where(r => r.Year == window.Year && r.Month == window.Month && r.SupersededAt == null)
            .ToDictionaryAsync(r => r.PaymentAccountId, ct);
        var books = await BookBalancesAsync(db, window.EndExclusive.AddDays(-1), ct);

        // A reconciliation is only good for the books it was signed off against. If an entry landed afterwards, the
        // stored difference is stale — the statement no longer matches — so the CFO reconciles again before closing.
        var unbalanced = accounts
            .Where(a => reconciliations.GetValueOrDefault(a.Id) is not { Difference: 0m } live
                        || live.BookBalance != books.GetValueOrDefault(a.Id))
            .Select(a => a.Label)
            .ToList();
        if (unbalanced.Count > 0)
            blockers.Add(new MonthBlockerDto(FinanceErrorCodes.MonthUnbalanced,
                $"Not balanced yet: {string.Join(", ", unbalanced)}."));

        // Months close in order, so a balance is never certified over a gap.
        var earliest = await db.LedgerEntries.AsNoTracking()
            .Where(e => e.EntryDate < window.Start && e.MonthCloseId == null && e.Status != LedgerStatus.Voided)
            .OrderBy(e => e.EntryDate)
            .Select(e => (DateOnly?)e.EntryDate)
            .FirstOrDefaultAsync(ct);
        if (earliest is { } open)
            blockers.Add(new MonthBlockerDto(FinanceErrorCodes.PreviousMonthOpen,
                $"{new DateOnly(open.Year, open.Month, 1):MMMM yyyy} is still open. Close months in order."));

        return blockers;
    }

    /// <summary>The newest closed month, which is the only one that may be reopened.</summary>
    public static Task<MonthClose?> LatestCloseAsync(HtnDbContext db, CancellationToken ct) =>
        db.MonthCloses.AsNoTracking().Where(m => m.ReopenedAt == null)
            .OrderByDescending(m => m.Year).ThenByDescending(m => m.Month)
            .FirstOrDefaultAsync(ct);

    // ---------- Lookups ----------

    /// <summary>User id → "First Last", for the entered-by / approved-by columns.</summary>
    public static async Task<Dictionary<Guid, string>> ActorNamesAsync(
        HtnDbContext db, IReadOnlyCollection<Guid> userIds, CancellationToken ct)
    {
        if (userIds.Count == 0) return [];
        var rows = await (
            from u in db.Users.AsNoTracking()
            join p in db.People.AsNoTracking() on u.PersonId equals (Guid?)p.Id
            where userIds.Contains(u.Id)
            select new { u.Id, Name = p.FirstName + " " + p.LastName }).ToListAsync(ct);
        return rows.ToDictionary(r => r.Id, r => r.Name);
    }

    /// <summary>Validates a category the client picked: it must exist, and be active unless it is a system one.</summary>
    public static async Task<(LedgerCategory? Category, IResult? Error)> CategoryAsync(
        HtnDbContext db, Guid categoryId, string field, CancellationToken ct)
    {
        var category = await db.LedgerCategories.AsNoTracking().FirstOrDefaultAsync(c => c.Id == categoryId, ct);
        if (category is null)
            return (null, Problems.Field(field, "That category no longer exists. Pick another one.", FinanceErrorCodes.UnknownCategory));
        if (!category.IsActive)
            return (null, Problems.Field(field, "That category is no longer in use. Pick another one.", FinanceErrorCodes.CategoryInactive));
        return (category, null);
    }

    /// <summary>Validates a payment account the client picked: it must exist and still be active.</summary>
    public static async Task<IResult?> AccountErrorAsync(HtnDbContext db, Guid accountId, string field, CancellationToken ct)
    {
        var account = await db.PaymentAccounts.AsNoTracking()
            .Where(a => a.Id == accountId).Select(a => new { a.IsActive }).FirstOrDefaultAsync(ct);
        if (account is null)
            return Problems.Field(field, "That account no longer exists.", FinanceErrorCodes.UnknownAccount);
        if (!account.IsActive)
            return Problems.Field(field, "That account is deactivated. Pick another one.", FinanceErrorCodes.AccountInactive);
        return null;
    }

    /// <summary>409 with a machine-readable code, for the state conflicts finance is full of.</summary>
    public static IResult Conflict(string detail, string code) =>
        Problems.Result(StatusCodes.Status409Conflict, "That cannot be done right now.", detail, code);
}
