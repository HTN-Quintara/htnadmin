using Htn.Domain.Common;

namespace Htn.Domain.Finance;

/// <summary>A line the CFO is about to save. Not an entity: the input to <see cref="EventBudget.SaveVersion"/>.</summary>
public sealed record NewBudgetLine(
    Guid CategoryId,
    BudgetLineKind Kind,
    string Label,
    decimal AmountBudgeted,
    int SortOrder,
    string? Note);

/// <summary>
/// One budget per event, with income and expense lines. Only the CFO edits it and there is no board approval, so the
/// safeguard is history: <b>every save creates a new immutable version with a reason</b> and this row simply points at
/// the current one. Actuals are never stored — they come from the posted ledger entries of the event's fund, matched
/// to a line by category.
/// </summary>
public sealed class EventBudget
{
    private EventBudget() { }

    public Guid Id { get; private set; }
    public Guid EventId { get; private set; }

    /// <summary>The version the screens show. Repointed by <see cref="SaveVersion"/>.</summary>
    public Guid CurrentVersionId { get; private set; }

    public int VersionCount { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static EventBudget Create(Guid eventId, DateTimeOffset at)
    {
        if (eventId == Guid.Empty) throw new DomainException("An event is required.");
        return new EventBudget { Id = Guid.CreateVersion7(at), EventId = eventId, CreatedAt = at, UpdatedAt = at };
    }

    /// <summary>
    /// Writes the next version and its lines, and points the budget at it. The caller adds the returned rows and
    /// notifies the board.
    /// </summary>
    public (EventBudgetVersion Version, IReadOnlyList<EventBudgetLine> Lines) SaveVersion(
        IReadOnlyList<NewBudgetLine> lines, string reason, Guid actorId, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(lines);
        if (lines.Count == 0) throw new DomainException("A budget needs at least one line.");
        var duplicate = lines.GroupBy(l => (l.CategoryId, l.Kind)).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
            throw new DomainException("Each category appears once per income or expense side. Combine the duplicate lines.");

        var version = EventBudgetVersion.Create(Id, VersionCount + 1, reason, actorId, at);
        var rows = lines.Select(l => EventBudgetLine.Create(version.Id, l, at)).ToList();
        CurrentVersionId = version.Id;
        VersionCount = version.VersionNumber;
        UpdatedAt = at;
        return (version, rows);
    }
}

/// <summary>
/// One saved revision of an event budget. Fully immutable: a correction is the next version, which is what makes the
/// "every save creates a new version with a reason" rule structural rather than a convention.
/// </summary>
public sealed class EventBudgetVersion
{
    public const int ReasonMaxLength = 500;

    private EventBudgetVersion() { }

    public Guid Id { get; private set; }
    public Guid BudgetId { get; private set; }

    /// <summary>1 for the first save, then up by one.</summary>
    public int VersionNumber { get; private set; }

    /// <summary>Why the CFO changed the budget. Always required, shown in the version history.</summary>
    public string Reason { get; private set; } = default!;

    public Guid CreatedById { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    internal static EventBudgetVersion Create(Guid budgetId, int versionNumber, string reason, Guid actorId, DateTimeOffset at)
    {
        if (actorId == Guid.Empty) throw new DomainException("An actor is required.");
        return new EventBudgetVersion
        {
            Id = Guid.CreateVersion7(at),
            BudgetId = budgetId,
            VersionNumber = versionNumber,
            Reason = FinanceRules.Required(reason, "Reason", ReasonMaxLength),
            CreatedById = actorId,
            CreatedAt = at,
        };
    }
}

/// <summary>An income or expense line of one budget version. Immutable, like the version that holds it.</summary>
public sealed class EventBudgetLine
{
    public const int LabelMaxLength = 120;
    public const int NoteMaxLength = 300;

    private EventBudgetLine() { }

    public Guid Id { get; private set; }
    public Guid VersionId { get; private set; }
    public Guid CategoryId { get; private set; }
    public BudgetLineKind Kind { get; private set; }
    public string Label { get; private set; } = default!;

    /// <summary>PHP planned for this line, always positive. The side is <see cref="Kind"/>.</summary>
    public decimal AmountBudgeted { get; private set; }

    public int SortOrder { get; private set; }
    public string? Note { get; private set; }

    internal static EventBudgetLine Create(Guid versionId, NewBudgetLine line, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(line);
        FinanceRules.Defined(line.Kind, "budget line kind");
        if (line.CategoryId == Guid.Empty) throw new DomainException("A category is required.");
        return new EventBudgetLine
        {
            Id = Guid.CreateVersion7(at),
            VersionId = versionId,
            CategoryId = line.CategoryId,
            Kind = line.Kind,
            Label = FinanceRules.Required(line.Label, "Label", LabelMaxLength),
            AmountBudgeted = FinanceRules.Money(line.AmountBudgeted, "Budgeted amount"),
            SortOrder = line.SortOrder,
            Note = FinanceRules.Optional(line.Note, "Note", NoteMaxLength),
        };
    }
}
