using Htn.Domain.Common;
using Htn.Domain.Events;

namespace Htn.Domain.Finance;

/// <summary>
/// A pot of money: the single Organization fund, or one fund per event. A balance is never stored on the fund — it is
/// derived from the posted ledger entries that point at it. Closed and swept, never deleted.
/// </summary>
public sealed class Fund
{
    public const int NameMaxLength = 120;

    private Fund() { }

    public Guid Id { get; private set; }
    public FundKind Kind { get; private set; }

    /// <summary>Set for <see cref="FundKind.Event"/>, null for the Organization fund.</summary>
    public Guid? EventId { get; private set; }

    public string Name { get; private set; } = default!;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ClosedAt { get; private set; }

    /// <summary>When the surplus was moved to the Organization fund, and the transfer that did it.</summary>
    public DateTimeOffset? SweptAt { get; private set; }

    public Guid? SweptByTransferGroupId { get; private set; }

    public bool IsClosed => ClosedAt is not null;
    public bool IsSwept => SweptAt is not null;

    /// <summary>The one Organization fund. Its id is fixed (<see cref="Funds.OrganizationId"/>) so SQL and C# agree.</summary>
    public static Fund Organization(DateTimeOffset at) => new()
    {
        Id = Funds.OrganizationId,
        Kind = FundKind.Organization,
        EventId = null,
        Name = Funds.OrganizationName,
        CreatedAt = at,
    };

    public static Fund ForEvent(Event ev, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(ev);
        return new Fund
        {
            Id = Guid.CreateVersion7(at),
            Kind = FundKind.Event,
            EventId = ev.Id,
            Name = FinanceRules.Required(ev.Name, "Name", NameMaxLength),
            CreatedAt = at,
        };
    }

    /// <summary>Records that the surplus has been moved out. The transfer rows themselves are the money movement.</summary>
    public void MarkSwept(Guid transferGroupId, DateTimeOffset at)
    {
        if (Kind != FundKind.Event) throw new DomainException("Only an event fund is swept.");
        if (IsSwept) throw new DomainException("This fund's surplus was already transferred.");
        if (transferGroupId == Guid.Empty) throw new DomainException("A transfer is required.");
        SweptAt = at;
        SweptByTransferGroupId = transferGroupId;
    }

    /// <summary>Closes the fund to new entries. The surplus must be swept first, or there is nothing to close over.</summary>
    public void Close(DateTimeOffset at)
    {
        if (Kind != FundKind.Organization && !IsSwept)
            throw new DomainException("Transfer the surplus to the Organization fund before closing this fund.");
        if (IsClosed) throw new DomainException("This fund is already closed.");
        ClosedAt = at;
    }

    public void EnsureOpen()
    {
        if (IsClosed) throw new DomainException("This fund is closed.");
    }
}

/// <summary>Fixed ids and names seeded by the <c>Finance_Ledger</c> migration.</summary>
public static class Funds
{
    public const string OrganizationName = "Organization";

    /// <summary>The Organization fund. Seeded with this literal id so the migration, the seeder and C# all agree.</summary>
    public static readonly Guid OrganizationId = new("0199c4a0-0000-7000-8000-000000000001");
}
