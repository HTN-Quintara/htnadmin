using Htn.Domain.Finance;

namespace Htn.Api.Features.Finance;

// ---------- Requests ----------

/// <summary>A reason on its own: voiding an entry, reopening a month, cancelling a request.</summary>
public sealed record FinanceReasonRequest(string? Reason);

/// <summary>
/// A new ledger entry. There is deliberately <b>no status field</b>: the server decides, from who is asking — the CFO
/// posts straight away, the finance assistant's entry lands Pending.
/// </summary>
public sealed record CreateLedgerEntryRequest(
    Guid? FundId,
    Guid? PaymentAccountId,
    Guid? CategoryId,
    DateOnly? EntryDate,
    LedgerDirection? Direction,
    decimal? Amount,
    string? Reference,
    string? Description);

public sealed record CreateTransferRequest(
    Guid? FromFundId,
    Guid? ToFundId,
    DateOnly? EntryDate,
    decimal? Amount,
    string? Description);

public sealed record SweepSurplusRequest(DateOnly? EntryDate, string? Description);

public sealed record CloseMonthRequest(string? Note);

public sealed record ReconcileAccountRequest(int? Year, int? Month, decimal? StatementBalance, string? Note);

public sealed record SaveBudgetRequest(string? Reason, IReadOnlyList<SaveBudgetLineRequest>? Lines);

public sealed record SaveBudgetLineRequest(
    Guid? CategoryId, BudgetLineKind? Kind, string? Label, decimal? AmountBudgeted, int? SortOrder, string? Note);

public sealed record CreateExpenseRequestRequest(
    string? Title, string? Description, decimal? Amount, Guid? CategoryId, DateOnly? NeededBy);

public sealed record DecideExpenseRequestRequest(string? Reason);

/// <summary>The non-file fields of the multipart "mark this request paid" form.</summary>
public sealed record PayExpenseRequestForm(Guid? PaymentAccountId, DateOnly? EntryDate, string? Reference);

// ---------- Responses ----------

public sealed record FundDto(
    Guid Id, FundKind Kind, Guid? EventId, string Name, decimal Balance,
    bool IsClosed, bool IsSwept, DateTimeOffset? SweptAt, bool CanSweep);

public sealed record LedgerCategoryDto(Guid Id, string Code, string Name, BudgetLineKind Kind, bool IsSystem, bool IsActive);

public sealed record LedgerEntryDto(
    Guid Id,
    Guid FundId,
    string FundName,
    Guid? PaymentAccountId,
    string? AccountLabel,
    Guid CategoryId,
    string CategoryName,
    DateOnly EntryDate,
    LedgerDirection Direction,
    decimal Amount,
    decimal SignedAmount,
    string? Reference,
    string Description,
    bool HasReceipt,
    LedgerStatus Status,
    LedgerSource Source,
    Guid? TransferGroupId,
    Guid? ContraOfEntryId,
    bool IsInClosedMonth,
    string? EnteredBy,
    DateTimeOffset EnteredAt,
    string? ApprovedBy,
    DateTimeOffset? ApprovedAt,
    string? VoidedBy,
    DateTimeOffset? VoidedAt,
    string? VoidReason,
    bool CanApprove,
    bool CanVoid);

public sealed record LedgerPageDto(IReadOnlyList<LedgerEntryDto> Items, int Page, int PageSize, int Total, LedgerTotalsDto Totals);

/// <summary>Totals over every posted entry matching the filter, not just the page on screen.</summary>
public sealed record LedgerTotalsDto(decimal MoneyIn, decimal MoneyOut, decimal Net, int PendingCount);

public sealed record CashflowPointDto(DateOnly Period, decimal MoneyIn, decimal MoneyOut, decimal Net);

public sealed record FinanceOverviewDto(
    DateOnly From,
    DateOnly To,
    decimal MoneyIn,
    decimal MoneyOut,
    decimal Net,
    int PendingCount,
    decimal PendingAmount,
    IReadOnlyList<CashflowPointDto> Cashflow,
    IReadOnlyList<FundDto> Funds,
    bool CanPost,
    bool CanApprove);

public sealed record AccountBalanceDto(
    Guid Id,
    string Label,
    string Kind,
    string AccountNumberMasked,
    decimal OpeningBalance,
    decimal BookBalance,
    bool IsReconcilable,
    bool IsActive,
    ReconciliationDto? LastReconciliation);

public sealed record ReconciliationDto(
    Guid Id, int Year, int Month, decimal StatementBalance, decimal BookBalance, decimal Difference,
    bool IsBalanced, string? Note, string? ReconciledBy, DateTimeOffset ReconciledAt);

public sealed record BalancingDto(
    int Year, int Month, IReadOnlyList<AccountBalanceDto> Accounts, MonthStatusDto Period, bool CanPost);

/// <summary>Whether this month can be closed, and if not, exactly why — the dialog shows these back to the CFO.</summary>
public sealed record MonthStatusDto(
    int Year,
    int Month,
    string Label,
    bool IsClosed,
    DateTimeOffset? ClosedAt,
    string? ClosedBy,
    bool CanClose,
    bool CanReopen,
    IReadOnlyList<MonthBlockerDto> Blockers);

public sealed record MonthBlockerDto(string Code, string Message);

public sealed record BudgetLineDto(
    Guid Id,
    Guid CategoryId,
    string CategoryName,
    BudgetLineKind Kind,
    string Label,
    decimal AmountBudgeted,
    decimal Actual,
    decimal Variance,
    int SortOrder,
    string? Note);

public sealed record BudgetVersionDto(
    Guid Id, int VersionNumber, string Reason, string? CreatedBy, DateTimeOffset CreatedAt);

public sealed record EventBudgetDto(
    Guid? Id,
    Guid EventId,
    string EventName,
    Guid? FundId,
    decimal FundBalance,
    BudgetVersionDto? Version,
    IReadOnlyList<BudgetLineDto> Lines,
    BudgetTotalsDto Totals,
    IReadOnlyList<BudgetVersionDto> History,
    bool CanEdit);

public sealed record BudgetTotalsDto(
    decimal IncomeBudgeted, decimal IncomeActual, decimal ExpenseBudgeted, decimal ExpenseActual,
    decimal NetBudgeted, decimal NetActual);

public sealed record ExpenseRequestDto(
    Guid Id,
    string Title,
    string? Description,
    decimal Amount,
    Guid CategoryId,
    string CategoryName,
    DateOnly? NeededBy,
    string? RequestedBy,
    DateTimeOffset RequestedAt,
    ExpenseRequestStatus Status,
    string? DecidedBy,
    DateTimeOffset? DecidedAt,
    string? DecisionReason,
    DateTimeOffset? PaidAt,
    Guid? PaidEntryId,
    string? CancelReason,
    bool CanApprove,
    bool CanReject,
    bool CanPay,
    bool CanCancel);

public sealed record ExpenseRequestPageDto(IReadOnlyList<ExpenseRequestDto> Items, int Page, int PageSize, int Total);

public sealed record FinanceAuditDto(
    Guid Id, DateTimeOffset At, string? Actor, string Action, string EntityType, string EntityId,
    string? BeforeJson, string? AfterJson, string? Reason);

public sealed record FinanceAuditPageDto(IReadOnlyList<FinanceAuditDto> Items, int Page, int PageSize, int Total);
