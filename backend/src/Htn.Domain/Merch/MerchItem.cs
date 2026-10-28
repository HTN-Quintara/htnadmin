using Htn.Domain.Common;
using Htn.Domain.Settings;

namespace Htn.Domain.Merch;

/// <summary>The fields given when creating a merch item.</summary>
public sealed record NewMerchItem(string Name, decimal Price, string? SwatchColor);

/// <summary>
/// One sellable item at an event's POS (Phase 8A): a T-shirt, a tote bag, a pin — event-scoped, with a price and an
/// optional swatch color (no file upload, per the brief). Mirrors <see cref="Htn.Domain.Suppliers.Supplier"/>'s
/// deactivate-not-delete shape: a <see cref="MerchSaleLine"/> always snapshots the price it sold at
/// (<see cref="MerchSaleLine.UnitPriceSnapshot"/>), so changing <see cref="Price"/> later never rewrites a past
/// sale, and deactivating an item never touches the sales already made against it.
/// </summary>
public sealed class MerchItem
{
    public const int NameMaxLength = 160;
    public const int ReasonMaxLength = 500;

    private MerchItem() { }

    public Guid Id { get; private set; }

    /// <summary>
    /// The event this item belongs to, or null once its remaining stock has moved to the Merch store (Phase 8C,
    /// <see cref="MoveToStore"/>) — "leftover stock" is <c>merch_items</c>/<c>merch_variants</c> rows with no event,
    /// unlinked from the event they were originally sold at into the shared pop-up pool.
    /// </summary>
    public Guid? EventId { get; private set; }

    /// <summary>
    /// The event this item was originally created for, frozen the moment <see cref="MoveToStore"/> is called — the
    /// "From" label on the Merch store's leftover inventory table. Null until moved; never changes afterwards.
    /// </summary>
    public Guid? LeftoverSourceEventId { get; private set; }

    public string Name { get; private set; } = default!;
    public decimal Price { get; private set; }

    /// <summary>A <c>#RRGGBB</c> hex color shown on the POS tile, or null for no swatch.</summary>
    public string? SwatchColor { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public Guid? CreatedById { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public Guid? UpdatedById { get; private set; }
    public DateTimeOffset? DeactivatedAt { get; private set; }
    public Guid? DeactivatedById { get; private set; }
    public string? DeactivateReason { get; private set; }

    public bool IsActive => DeactivatedAt is null;

    public static MerchItem Create(Guid eventId, NewMerchItem data, Guid actorId, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (eventId == Guid.Empty) throw new DomainException("An event is required.");
        var item = new MerchItem
        {
            Id = Guid.CreateVersion7(at),
            EventId = eventId == Guid.Empty ? null : eventId,
            CreatedAt = at,
            CreatedById = actorId,
            UpdatedAt = at,
            UpdatedById = actorId,
        };
        item.ApplyEditable(data.Name, data.Price, data.SwatchColor, actorId, at);
        return item;
    }

    public void Update(NewMerchItem data, Guid actorId, DateTimeOffset at)
    {
        EnsureActive();
        ArgumentNullException.ThrowIfNull(data);
        ApplyEditable(data.Name, data.Price, data.SwatchColor, actorId, at);
    }

    /// <summary>
    /// Phase 8C: unlinks this item (and, by the caller's convention, its variants) from its event into the shared
    /// Merch store leftover pool, stamping <see cref="LeftoverSourceEventId"/> once, forever. Idempotent-by-refusal:
    /// an item already in the store can't be moved again (there is nowhere else to go, and the "From" label must
    /// stay frozen).
    /// </summary>
    public void MoveToStore(Guid actorId, DateTimeOffset at)
    {
        EnsureActive();
        if (EventId is null) throw new DomainException("This item is already in the Merch store.");
        LeftoverSourceEventId = EventId;
        EventId = null;
        Touch(actorId, at);
    }

    public void Deactivate(string reason, Guid actorId, DateTimeOffset at)
    {
        if (!IsActive) throw new DomainException("This item is already deactivated.");
        DeactivateReason = MerchRules.Required(reason, "Reason", ReasonMaxLength);
        DeactivatedAt = at;
        DeactivatedById = actorId;
    }

    private void ApplyEditable(string name, decimal price, string? swatchColor, Guid actorId, DateTimeOffset at)
    {
        Name = MerchRules.Required(name, "Name", NameMaxLength);
        Price = MerchRules.NonNegativeMoney(price, "Price");
        if (swatchColor is not null && !ColorContrast.IsHex(swatchColor))
            throw new DomainException("Swatch color must look like #B71C1C.");
        SwatchColor = swatchColor;
        Touch(actorId, at);
    }

    private void Touch(Guid actorId, DateTimeOffset at)
    {
        UpdatedAt = at;
        UpdatedById = actorId;
    }

    private void EnsureActive()
    {
        if (!IsActive) throw new DomainException("This item has been deactivated.");
    }
}
