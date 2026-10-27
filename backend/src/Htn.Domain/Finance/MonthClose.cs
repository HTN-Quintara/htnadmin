using Htn.Domain.Common;

namespace Htn.Domain.Finance;

/// <summary>
/// A closed accounting month. A month can only be closed when every reconcilable account is balanced and nothing is
/// pending; closing stamps every posted entry in the period, which freezes it.
/// <para>Append-only: reopening stamps the row with a reason instead of deleting it.</para>
/// </summary>
public sealed class MonthClose
{
    public const int NoteMaxLength = 500;
    public const int ReasonMaxLength = 500;

    private MonthClose() { }

    public Guid Id { get; private set; }

    /// <summary>Manila calendar year and month.</summary>
    public int Year { get; private set; }

    public int Month { get; private set; }
    public string? Note { get; private set; }
    public Guid ClosedById { get; private set; }
    public DateTimeOffset ClosedAt { get; private set; }
    public DateTimeOffset? ReopenedAt { get; private set; }
    public Guid? ReopenedById { get; private set; }
    public string? ReopenReason { get; private set; }

    public bool IsOpen => ReopenedAt is not null;

    public static MonthClose Create(int year, int month, string? note, Guid actorId, DateTimeOffset at)
    {
        var (y, m) = FinanceRules.Period(year, month);
        if (actorId == Guid.Empty) throw new DomainException("An actor is required.");
        return new MonthClose
        {
            Id = Guid.CreateVersion7(at),
            Year = y,
            Month = m,
            Note = FinanceRules.Optional(note, "Note", NoteMaxLength),
            ClosedById = actorId,
            ClosedAt = at,
        };
    }

    /// <summary>Reopens the month with a reason. Only the most recently closed month may be reopened.</summary>
    public void Reopen(string reason, Guid actorId, DateTimeOffset at)
    {
        if (IsOpen) throw new DomainException("This month is already reopened.");
        ReopenReason = FinanceRules.Required(reason, "Reason", ReasonMaxLength);
        ReopenedById = actorId;
        ReopenedAt = at;
    }
}
