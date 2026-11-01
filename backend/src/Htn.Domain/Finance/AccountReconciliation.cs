using Htn.Domain.Common;

namespace Htn.Domain.Finance;

/// <summary>
/// The CFO's comparison, for one account and one month, of the book balance (derived from posted entries) with the
/// bank statement or the counted cash. A month can only close when every reconcilable account has a live
/// reconciliation whose <see cref="Difference"/> is zero.
/// <para>
/// Append-only: re-reconciling supersedes the previous row rather than editing it, so the history of what the CFO
/// believed, and when, is kept.
/// </para>
/// </summary>
public sealed class AccountReconciliation
{
    public const int NoteMaxLength = 500;
    public const int ReasonMaxLength = 500;

    private AccountReconciliation() { }

    public Guid Id { get; private set; }
    public Guid PaymentAccountId { get; private set; }
    public int Year { get; private set; }
    public int Month { get; private set; }

    /// <summary>What the statement says, or what was counted. May be negative.</summary>
    public decimal StatementBalance { get; private set; }

    /// <summary>A snapshot of the derived book balance at the moment of reconciling.</summary>
    public decimal BookBalance { get; private set; }

    /// <summary>Statement minus book. Stored, so "was it balanced then?" survives later entries.</summary>
    public decimal Difference { get; private set; }

    public string? Note { get; private set; }
    public Guid ReconciledById { get; private set; }
    public DateTimeOffset ReconciledAt { get; private set; }
    public DateTimeOffset? SupersededAt { get; private set; }
    public Guid? SupersededById { get; private set; }
    public string? SupersedeReason { get; private set; }

    public bool IsLive => SupersededAt is null;
    public bool IsBalanced => Difference == 0m;

    public static AccountReconciliation Create(
        Guid paymentAccountId, int year, int month, decimal statementBalance, decimal bookBalance,
        string? note, Guid actorId, DateTimeOffset at)
    {
        var (y, m) = FinanceRules.Period(year, month);
        if (paymentAccountId == Guid.Empty) throw new DomainException("An account is required.");
        if (actorId == Guid.Empty) throw new DomainException("An actor is required.");
        var statement = FinanceRules.SignedMoney(statementBalance, "Statement balance");
        var book = FinanceRules.SignedMoney(bookBalance, "Book balance");
        return new AccountReconciliation
        {
            Id = Guid.CreateVersion7(at),
            PaymentAccountId = paymentAccountId,
            Year = y,
            Month = m,
            StatementBalance = statement,
            BookBalance = book,
            Difference = statement - book,
            Note = FinanceRules.Optional(note, "Note", NoteMaxLength),
            ReconciledById = actorId,
            ReconciledAt = at,
        };
    }

    /// <summary>Retires this row in favour of a newer reconciliation of the same account and month.</summary>
    public void Supersede(string reason, Guid actorId, DateTimeOffset at)
    {
        if (!IsLive) throw new DomainException("This reconciliation was already superseded.");
        SupersedeReason = FinanceRules.Required(reason, "Reason", ReasonMaxLength);
        SupersededById = actorId;
        SupersededAt = at;
    }
}
