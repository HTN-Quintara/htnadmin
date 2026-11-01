using Htn.Domain.Common;

namespace Htn.Domain.Merch;

/// <summary>
/// One size/variant of a <see cref="MerchItem"/> ("M", "One size") with its own stock count. Deactivate-not-delete,
/// mirroring <see cref="MerchItem"/>. <see cref="StockOnHand"/> is adjusted here for manual inventory corrections
/// (a recount, stock arriving); a POS sale instead decrements it through the database trigger on
/// <c>merch_sale_lines</c> (<c>Merch_Guards</c>), which is the actual guarantee against a race between two
/// concurrent sales for the last unit — this method is not used by the sale path at all.
/// </summary>
public sealed class MerchVariant
{
    public const int LabelMaxLength = 40;
    public const int ReasonMaxLength = 500;
    public const int MaxStock = 1_000_000;

    private MerchVariant() { }

    public Guid Id { get; private set; }
    public Guid ItemId { get; private set; }
    public string Label { get; private set; } = default!;
    public int StockOnHand { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public Guid? CreatedById { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public Guid? UpdatedById { get; private set; }
    public DateTimeOffset? DeactivatedAt { get; private set; }
    public Guid? DeactivatedById { get; private set; }
    public string? DeactivateReason { get; private set; }

    public bool IsActive => DeactivatedAt is null;

    public static MerchVariant Create(Guid itemId, string label, int stockOnHand, Guid actorId, DateTimeOffset at)
    {
        if (itemId == Guid.Empty) throw new DomainException("An item is required.");
        var variant = new MerchVariant
        {
            Id = Guid.CreateVersion7(at),
            ItemId = itemId,
            Label = MerchRules.Required(label, "Label", LabelMaxLength),
            StockOnHand = ValidStock(stockOnHand),
            CreatedAt = at,
            CreatedById = actorId,
            UpdatedAt = at,
            UpdatedById = actorId,
        };
        return variant;
    }

    public void Rename(string label, Guid actorId, DateTimeOffset at)
    {
        EnsureActive();
        Label = MerchRules.Required(label, "Label", LabelMaxLength);
        Touch(actorId, at);
    }

    /// <summary>Sets the counted stock directly (a recount or newly arrived stock), not a delta.</summary>
    public void AdjustStock(int newStockOnHand, Guid actorId, DateTimeOffset at)
    {
        EnsureActive();
        StockOnHand = ValidStock(newStockOnHand);
        Touch(actorId, at);
    }

    public void Deactivate(string reason, Guid actorId, DateTimeOffset at)
    {
        if (!IsActive) throw new DomainException("This variant is already deactivated.");
        DeactivateReason = MerchRules.Required(reason, "Reason", ReasonMaxLength);
        DeactivatedAt = at;
        DeactivatedById = actorId;
    }

    private static int ValidStock(int value)
    {
        if (value < 0 || value > MaxStock) throw new DomainException($"Stock on hand must be between 0 and {MaxStock}.");
        return value;
    }

    private void Touch(Guid actorId, DateTimeOffset at)
    {
        UpdatedAt = at;
        UpdatedById = actorId;
    }

    private void EnsureActive()
    {
        if (!IsActive) throw new DomainException("This variant has been deactivated.");
    }
}
