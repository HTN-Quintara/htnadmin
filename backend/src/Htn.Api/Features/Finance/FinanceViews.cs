using Htn.Domain.Finance;
using Htn.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Htn.Api.Features.Finance;

/// <summary>
/// The DTO projections. Permission flags are computed here and sent per record, because the screens must hide what
/// the caller cannot do rather than offer a button that 403s — and because "can I void this?" depends on the row
/// (a closed month freezes it), not only on the role.
/// </summary>
public static class FinanceViews
{
    /// <summary>The flat row shape the ledger list and detail share, before names are attached.</summary>
    public sealed record Row(LedgerEntry Entry, string FundName, string? AccountLabel, string CategoryName);

    /// <summary>
    /// Attaches the fund, account and category names to a set of entries. Correlated subqueries rather than joins,
    /// because the account is optional and a left join into a record constructor is not translatable.
    /// </summary>
    public static IQueryable<Row> Project(HtnDbContext db, IQueryable<LedgerEntry> entries) =>
        entries.Select(e => new Row(
            e,
            db.Funds.Where(f => f.Id == e.FundId).Select(f => f.Name).First(),
            db.PaymentAccounts.Where(a => a.Id == e.PaymentAccountId).Select(a => a.Label).FirstOrDefault(),
            db.LedgerCategories.Where(c => c.Id == e.CategoryId).Select(c => c.Name).First()));


    /// <summary>
    /// Every user id a set of rows refers to, so the names are fetched in one query instead of per row.
    /// </summary>
    public static HashSet<Guid> ActorIds(IEnumerable<Row> rows)
    {
        var ids = new HashSet<Guid>();
        foreach (var row in rows)
        {
            if (row.Entry.EnteredById is { } entered) ids.Add(entered);
            if (row.Entry.ApprovedById is { } approved) ids.Add(approved);
            if (row.Entry.VoidedById is { } voided) ids.Add(voided);
        }
        return ids;
    }

    public static LedgerEntryDto Entry(Row row, IReadOnlyDictionary<Guid, string> names, bool isCfo, Guid? actorId)
    {
        var e = row.Entry;
        // The CFO approves, but never their own entry: a second pair of eyes is the only control a one-person
        // finance team can have, and it is the same rule the task board uses for points.
        var canApprove = isCfo && e.Status == LedgerStatus.Pending && e.EnteredById != actorId;
        var canVoid = isCfo && e.Status != LedgerStatus.Voided && e.MonthCloseId is null;
        return new LedgerEntryDto(
            e.Id,
            e.FundId,
            row.FundName,
            e.PaymentAccountId,
            row.AccountLabel,
            e.CategoryId,
            row.CategoryName,
            e.EntryDate,
            e.Direction,
            e.Amount,
            e.SignedAmount,
            e.Reference,
            e.Description,
            e.ReceiptFileId is not null,
            e.Status,
            e.Source,
            e.TransferGroupId,
            e.ContraOfEntryId,
            e.MonthCloseId is not null,
            Name(names, e.EnteredById),
            e.EnteredAt,
            Name(names, e.ApprovedById),
            e.ApprovedAt,
            Name(names, e.VoidedById),
            e.VoidedAt,
            e.VoidReason,
            canApprove,
            canVoid);
    }

    public static FundDto Fund(Fund fund, decimal balance, bool isCfo) =>
        new(fund.Id, fund.Kind, fund.EventId, fund.Name, balance, fund.IsClosed, fund.IsSwept, fund.SweptAt,
            // Only a positive surplus on an unswept, open event fund is worth moving.
            isCfo && fund.Kind == FundKind.Event && !fund.IsSwept && !fund.IsClosed && balance > 0m);

    public static LedgerCategoryDto Category(LedgerCategory c) =>
        new(c.Id, c.Code, c.Name, c.Kind, c.IsSystem, c.IsActive);

    public static ReconciliationDto Reconciliation(AccountReconciliation r, IReadOnlyDictionary<Guid, string> names) =>
        new(r.Id, r.Year, r.Month, r.StatementBalance, r.BookBalance, r.Difference, r.IsBalanced, r.Note,
            Name(names, r.ReconciledById), r.ReconciledAt);

    public static MonthStatusDto Month(
        int year, int month, MonthClose? close, IReadOnlyList<MonthBlockerDto> blockers,
        bool isCfo, bool isLatestClose, IReadOnlyDictionary<Guid, string> names) =>
        new(year, month, new DateOnly(year, month, 1).ToString("MMMM yyyy"),
            close is not null,
            close?.ClosedAt,
            close is null ? null : Name(names, close.ClosedById),
            isCfo && close is null && blockers.Count == 0,
            isCfo && close is not null && isLatestClose,
            blockers);

    /// <summary>
    /// A budget line with its actual. Income is earned (a positive signed sum) and expense is spent (a negative one),
    /// so both are reported as positive magnitudes and the variance always reads "better when higher".
    /// </summary>
    public static BudgetLineDto Line(EventBudgetLine line, string categoryName, decimal signedActual)
    {
        var actual = line.Kind == BudgetLineKind.Income ? signedActual : -signedActual;
        var variance = line.Kind == BudgetLineKind.Income
            ? actual - line.AmountBudgeted           // more income than planned is good
            : line.AmountBudgeted - actual;          // less spent than planned is good
        return new BudgetLineDto(line.Id, line.CategoryId, categoryName, line.Kind, line.Label,
            line.AmountBudgeted, actual, variance, line.SortOrder, line.Note);
    }

    public static BudgetTotalsDto Totals(IReadOnlyList<BudgetLineDto> lines)
    {
        var income = lines.Where(l => l.Kind == BudgetLineKind.Income).ToList();
        var expense = lines.Where(l => l.Kind == BudgetLineKind.Expense).ToList();
        var incomeBudgeted = income.Sum(l => l.AmountBudgeted);
        var incomeActual = income.Sum(l => l.Actual);
        var expenseBudgeted = expense.Sum(l => l.AmountBudgeted);
        var expenseActual = expense.Sum(l => l.Actual);
        return new BudgetTotalsDto(incomeBudgeted, incomeActual, expenseBudgeted, expenseActual,
            incomeBudgeted - expenseBudgeted, incomeActual - expenseActual);
    }

    public static BudgetVersionDto Version(EventBudgetVersion v, IReadOnlyDictionary<Guid, string> names) =>
        new(v.Id, v.VersionNumber, v.Reason, Name(names, v.CreatedById), v.CreatedAt);

    public static ExpenseRequestDto Request(
        ExpenseRequest r, string categoryName, IReadOnlyDictionary<Guid, string> names, bool isCfo, Guid? actorId)
    {
        var pending = r.Status == ExpenseRequestStatus.Pending;
        return new ExpenseRequestDto(
            r.Id, r.Title, r.Description, r.Amount, r.CategoryId, categoryName, r.NeededBy,
            Name(names, r.RequestedById), r.RequestedAt, r.Status,
            Name(names, r.DecidedById), r.DecidedAt, r.DecisionReason, r.PaidAt, r.PaidEntryId, r.CancelReason,
            // The CFO decides, but not on their own request: the same second-pair-of-eyes rule as the ledger.
            isCfo && pending && r.RequestedById != actorId,
            isCfo && pending && r.RequestedById != actorId,
            isCfo && r.Status == ExpenseRequestStatus.Approved,
            // The requester withdraws their own; the CFO may withdraw anyone's.
            pending && (isCfo || r.RequestedById == actorId));
    }

    public static FinanceAuditDto Audit(Htn.Domain.Audit.AuditEntry a, IReadOnlyDictionary<Guid, string> names) =>
        new(a.Id, a.At, Name(names, a.ActorId), a.Action, a.EntityType, a.EntityId, a.BeforeJson, a.AfterJson, a.Reason);

    private static string? Name(IReadOnlyDictionary<Guid, string> names, Guid? id) =>
        id is { } key ? names.GetValueOrDefault(key) : null;
}
