using Htn.Domain.Common;

namespace Htn.Domain.Finance;

/// <summary>
/// Non-event spending asked for by a board member (the year-end party, a domain renewal). The CFO approves or rejects
/// it with a reason, then marks it paid, which posts an entry to the Organization fund.
/// <para>Cancelled or rejected with a reason, never deleted.</para>
/// </summary>
public sealed class ExpenseRequest
{
    public const int TitleMaxLength = 120;
    public const int DescriptionMaxLength = 1000;
    public const int ReasonMaxLength = 500;

    private ExpenseRequest() { }

    public Guid Id { get; private set; }
    public string Title { get; private set; } = default!;
    public string? Description { get; private set; }

    /// <summary>PHP asked for.</summary>
    public decimal Amount { get; private set; }

    public Guid CategoryId { get; private set; }
    public DateOnly? NeededBy { get; private set; }
    public Guid RequestedById { get; private set; }
    public DateTimeOffset RequestedAt { get; private set; }
    public ExpenseRequestStatus Status { get; private set; }

    public Guid? DecidedById { get; private set; }
    public DateTimeOffset? DecidedAt { get; private set; }

    /// <summary>Required on a rejection, optional as a note on an approval.</summary>
    public string? DecisionReason { get; private set; }

    /// <summary>The ledger entry that paid it.</summary>
    public Guid? PaidEntryId { get; private set; }

    public Guid? PaidById { get; private set; }
    public DateTimeOffset? PaidAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public Guid? CancelledById { get; private set; }
    public string? CancelReason { get; private set; }

    public bool IsDecided => DecidedAt is not null;

    public static ExpenseRequest Create(
        string title, string? description, decimal amount, Guid categoryId, DateOnly? neededBy,
        Guid requestedById, DateTimeOffset at)
    {
        if (categoryId == Guid.Empty) throw new DomainException("A category is required.");
        if (requestedById == Guid.Empty) throw new DomainException("A requester is required.");
        return new ExpenseRequest
        {
            Id = Guid.CreateVersion7(at),
            Title = FinanceRules.Required(title, "Title", TitleMaxLength),
            Description = FinanceRules.Optional(description, "Description", DescriptionMaxLength),
            Amount = FinanceRules.Money(amount),
            CategoryId = categoryId,
            NeededBy = neededBy,
            RequestedById = requestedById,
            RequestedAt = at,
            Status = ExpenseRequestStatus.Pending,
        };
    }

    public void Approve(Guid actorId, string? note, DateTimeOffset at)
    {
        EnsurePending("decided");
        DecisionReason = FinanceRules.Optional(note, "Note", ReasonMaxLength);
        Status = ExpenseRequestStatus.Approved;
        DecidedById = actorId;
        DecidedAt = at;
    }

    public void Reject(string reason, Guid actorId, DateTimeOffset at)
    {
        EnsurePending("decided");
        // Validate before mutating: a refused call must leave the request exactly as it was.
        DecisionReason = FinanceRules.Required(reason, "Reason", ReasonMaxLength);
        Status = ExpenseRequestStatus.Rejected;
        DecidedById = actorId;
        DecidedAt = at;
    }

    /// <summary>Records the payment. The caller posts the ledger entry in the same transaction.</summary>
    public void MarkPaid(Guid entryId, Guid actorId, DateTimeOffset at)
    {
        if (Status == ExpenseRequestStatus.Paid) throw new DomainException("This request is already paid.");
        if (Status != ExpenseRequestStatus.Approved) throw new DomainException("Approve the request before marking it paid.");
        if (entryId == Guid.Empty) throw new DomainException("A ledger entry is required.");
        Status = ExpenseRequestStatus.Paid;
        PaidEntryId = entryId;
        PaidById = actorId;
        PaidAt = at;
    }

    /// <summary>Withdrawn by the requester (or the CFO) while it is still waiting for a decision.</summary>
    public void Cancel(string reason, Guid actorId, DateTimeOffset at)
    {
        EnsurePending("cancelled");
        CancelReason = FinanceRules.Required(reason, "Reason", ReasonMaxLength);
        Status = ExpenseRequestStatus.Cancelled;
        CancelledById = actorId;
        CancelledAt = at;
    }

    private void EnsurePending(string verb)
    {
        if (Status != ExpenseRequestStatus.Pending)
            throw new DomainException(Status == ExpenseRequestStatus.Paid
                ? "This request is already paid."
                : $"This request has already been {(Status == ExpenseRequestStatus.Cancelled ? "cancelled" : verb)}.");
    }
}
