using Htn.Domain.Common;

namespace Htn.Domain.Finance;

/// <summary>
/// What an entry is for ("Ticket sales", "Venue", "Food"). A configurable table rather than an enum, because a budget
/// line and a ledger entry must point at the <b>same</b> category for "actuals come from the ledger" to work, and the
/// CFO invents categories mid-event — an enum would mean a migration and a redeploy.
/// <para>
/// The system categories (<see cref="LedgerCategories"/>) are seeded with fixed ids, cannot be deactivated, and keep
/// their <see cref="Code"/> forever, because auto-posting looks them up by it.
/// </para>
/// Renamed and deactivated, never deleted.
/// </summary>
public sealed class LedgerCategory
{
    public const int CodeMaxLength = 40;
    public const int NameMaxLength = 80;
    public const int ReasonMaxLength = 500;

    private LedgerCategory() { }

    public Guid Id { get; private set; }

    /// <summary>Stable lower_snake_case identity, immutable once created.</summary>
    public string Code { get; private set; } = default!;

    public string Name { get; private set; } = default!;
    public BudgetLineKind Kind { get; private set; }

    /// <summary>Seeded and relied on by auto-posting: it can be renamed, but never deactivated.</summary>
    public bool IsSystem { get; private set; }

    public bool IsActive { get; private set; }
    public int SortOrder { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? DeactivatedAt { get; private set; }
    public string? DeactivationReason { get; private set; }

    public static LedgerCategory Create(string code, string name, BudgetLineKind kind, int sortOrder, DateTimeOffset at) =>
        CreateCore(Guid.CreateVersion7(at), code, name, kind, isSystem: false, sortOrder, at);

    /// <summary>A seeded category with a fixed id, used by the migration and the dev seeder.</summary>
    public static LedgerCategory System(Guid id, string code, string name, BudgetLineKind kind, int sortOrder, DateTimeOffset at) =>
        CreateCore(id, code, name, kind, isSystem: true, sortOrder, at);

    public void Rename(string name, int sortOrder)
    {
        EnsureActive();
        Name = FinanceRules.Required(name, "Name", NameMaxLength);
        SortOrder = sortOrder;
    }

    public void Deactivate(string reason, DateTimeOffset at)
    {
        EnsureActive();
        if (IsSystem) throw new DomainException("This category is used by automatic posting and stays in use.");
        IsActive = false;
        DeactivatedAt = at;
        DeactivationReason = FinanceRules.Required(reason, "Reason", ReasonMaxLength);
    }

    public void EnsureActive()
    {
        if (!IsActive) throw new DomainException("This category is no longer in use.");
    }

    private static LedgerCategory CreateCore(
        Guid id, string code, string name, BudgetLineKind kind, bool isSystem, int sortOrder, DateTimeOffset at)
    {
        FinanceRules.Defined(kind, "category kind");
        var c = FinanceRules.Required(code, "Code", CodeMaxLength).ToLowerInvariant();
        if (!c.All(ch => char.IsAsciiLetterLower(ch) || char.IsAsciiDigit(ch) || ch == '_'))
            throw new DomainException("Code uses lower-case letters, digits and underscores.");
        return new LedgerCategory
        {
            Id = id,
            Code = c,
            Name = FinanceRules.Required(name, "Name", NameMaxLength),
            Kind = kind,
            IsSystem = isSystem,
            IsActive = true,
            SortOrder = sortOrder,
            CreatedAt = at,
        };
    }
}

/// <summary>
/// The system categories, seeded by the <c>Finance_Ledger</c> migration with these literal ids. Auto-posting resolves
/// a category by id, so these constants are the contract between the migration, the seeder and the posting code.
/// </summary>
public static class LedgerCategories
{
    public static readonly Guid TicketsId = new("0199c4a0-0000-7000-8000-000000000011");
    public static readonly Guid SponsorshipId = new("0199c4a0-0000-7000-8000-000000000012");
    public static readonly Guid MerchId = new("0199c4a0-0000-7000-8000-000000000013");
    public static readonly Guid MembershipDuesId = new("0199c4a0-0000-7000-8000-000000000014");
    public static readonly Guid TransferId = new("0199c4a0-0000-7000-8000-000000000015");
    public static readonly Guid WriteOffId = new("0199c4a0-0000-7000-8000-000000000016");

    public const string TicketsCode = "tickets";
    public const string SponsorshipCode = "sponsorship";
    public const string MerchCode = "merch";
    public const string MembershipDuesCode = "membership_dues";
    public const string TransferCode = "transfer";
    public const string WriteOffCode = "write_off";

    /// <summary>(id, code, name, kind, sort order) for every seeded category, in display order.</summary>
    public static readonly IReadOnlyList<(Guid Id, string Code, string Name, BudgetLineKind Kind, int SortOrder)> Seeded =
    [
        (TicketsId, TicketsCode, "Ticket sales", BudgetLineKind.Income, 10),
        (SponsorshipId, SponsorshipCode, "Sponsorship", BudgetLineKind.Income, 20),
        (MerchId, MerchCode, "Merch sales", BudgetLineKind.Income, 30),
        (MembershipDuesId, MembershipDuesCode, "Membership dues", BudgetLineKind.Income, 40),
        (TransferId, TransferCode, "Transfer between funds", BudgetLineKind.Income, 900),
        (WriteOffId, WriteOffCode, "Write-off", BudgetLineKind.Expense, 910),
    ];
}
