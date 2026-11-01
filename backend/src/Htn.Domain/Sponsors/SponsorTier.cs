using Htn.Domain.Common;

namespace Htn.Domain.Sponsors;

/// <summary>The fields of a tier, shared by create and update so the two can't diverge.</summary>
public sealed record NewSponsorTier(
    TierClass Class,
    string Name,
    decimal? CashAmount,
    string? PriceNote,
    string? InKindText,
    int? InKindItemCount,
    int Slots,
    int CompedSeats,
    int SortOrder,
    IReadOnlyList<string> Perks);

/// <summary>
/// One tier in an event's sponsorship catalogue (the cards on frame 13): a class, a name, what it costs (cash,
/// in-kind, or both), how many slots it has and how many comped seats it grants. Deactivated, never deleted, so a
/// signed sponsor keeps pointing at the tier they bought.
/// </summary>
public sealed class SponsorTier
{
    public const int NameMaxLength = 80;
    public const int PriceNoteMaxLength = 80;
    public const int InKindMaxLength = 200;
    public const int PerkMaxLength = 200;
    public const int MaxPerks = 20;
    public const int MaxSlots = 100;
    public const int MaxCompedSeats = 200;
    public const int ReasonMaxLength = 500;

    private SponsorTier() { }

    public Guid Id { get; private set; }
    public Guid EventId { get; private set; }
    public TierClass Class { get; private set; }
    public string Name { get; private set; } = default!;

    /// <summary>Cash price in PHP, when the tier has one.</summary>
    public decimal? CashAmount { get; private set; }

    /// <summary>A free-text tail shown after the price, e.g. "or equivalent".</summary>
    public string? PriceNote { get; private set; }

    public string? InKindText { get; private set; }
    public int? InKindItemCount { get; private set; }
    public int Slots { get; private set; }
    public int CompedSeats { get; private set; }
    public int SortOrder { get; private set; }

    /// <summary>The perks this tier promises, snapshotted onto a sponsor when it is confirmed.</summary>
    public IReadOnlyList<string> Perks { get; private set; } = [];

    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public Guid? CreatedById { get; private set; }
    public DateTimeOffset? DeactivatedAt { get; private set; }

    public static SponsorTier Create(Guid eventId, NewSponsorTier data, Guid actorId, DateTimeOffset at)
    {
        if (eventId == Guid.Empty) throw new DomainException("An event is required.");
        var tier = new SponsorTier
        {
            Id = Guid.CreateVersion7(at),
            EventId = eventId,
            IsActive = true,
            CreatedAt = at,
            CreatedById = actorId,
        };
        tier.Apply(data);
        return tier;
    }

    public void Update(NewSponsorTier data) => Apply(data);

    public void Deactivate(DateTimeOffset at)
    {
        if (!IsActive) throw new DomainException("This tier is already inactive.");
        IsActive = false;
        DeactivatedAt = at;
    }

    private void Apply(NewSponsorTier data)
    {
        ArgumentNullException.ThrowIfNull(data);
        SponsorRules.Defined(data.Class, "tier class");
        Class = data.Class;
        Name = SponsorRules.Required(data.Name, "Name", NameMaxLength);
        CashAmount = data.CashAmount is { } cash ? SponsorRules.Money(cash, "Cash amount") : null;
        PriceNote = SponsorRules.Optional(data.PriceNote, "Price note", PriceNoteMaxLength);
        InKindText = SponsorRules.Optional(data.InKindText, "In-kind", InKindMaxLength);
        InKindItemCount = data.InKindItemCount;
        if (InKindItemCount is not null && InKindText is null)
            throw new DomainException("Give the in-kind description before its item count.");
        if (InKindItemCount is < 1 or > 100_000) throw new DomainException("In-kind item count is out of range.");
        if (CashAmount is null && InKindText is null)
            throw new DomainException("A tier needs a cash amount, an in-kind description, or both.");
        Slots = SponsorRules.Count(data.Slots, "Slots", 1, MaxSlots);
        CompedSeats = SponsorRules.Count(data.CompedSeats, "Comped seats", 0, MaxCompedSeats);
        SortOrder = data.SortOrder;
        Perks = CleanPerks(data.Perks);
    }

    private static IReadOnlyList<string> CleanPerks(IReadOnlyList<string>? perks)
    {
        if (perks is null) return [];
        var cleaned = perks.Select(p => p?.Trim()).Where(p => !string.IsNullOrEmpty(p)).Select(p => p!).ToList();
        if (cleaned.Count > MaxPerks) throw new DomainException($"A tier can list at most {MaxPerks} perks.");
        if (cleaned.Any(p => p.Length > PerkMaxLength))
            throw new DomainException($"Each perk must be at most {PerkMaxLength} characters.");
        return cleaned;
    }
}
