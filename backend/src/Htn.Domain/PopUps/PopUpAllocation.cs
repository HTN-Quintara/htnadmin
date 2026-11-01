using Htn.Domain.Common;

namespace Htn.Domain.PopUps;

/// <summary>
/// How much of one <c>MerchVariant</c>'s leftover stock a pop-up brought with it — the reservation that keeps two
/// pop-ups from allocating the same leftover units at once (see <c>PopUpOps.AvailableLeftoverAsync</c> for the
/// "available" math). <see cref="QtyReturned"/> is filled in at the "close &amp; count" step; before that it is
/// null and treated as zero everywhere the reservation is computed.
/// </summary>
public sealed class PopUpAllocation
{
    private PopUpAllocation() { }

    public Guid Id { get; private set; }
    public Guid PopUpId { get; private set; }
    public Guid VariantId { get; private set; }
    public int QtyBrought { get; private set; }
    public int? QtyReturned { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static PopUpAllocation Create(Guid popUpId, Guid variantId, int qtyBrought, DateTimeOffset at)
    {
        if (popUpId == Guid.Empty) throw new DomainException("A pop-up is required.");
        if (variantId == Guid.Empty) throw new DomainException("A variant is required.");
        if (qtyBrought <= 0) throw new DomainException("Quantity to bring must be at least 1.");
        return new PopUpAllocation
        {
            Id = Guid.CreateVersion7(at),
            PopUpId = popUpId,
            VariantId = variantId,
            QtyBrought = qtyBrought,
            CreatedAt = at,
        };
    }

    /// <summary>Records what came back unsold at the "close &amp; count" step. Can only be set once.</summary>
    public void RecordReturned(int qtyReturned)
    {
        if (QtyReturned is not null) throw new DomainException("This line has already been counted.");
        if (qtyReturned < 0 || qtyReturned > QtyBrought)
            throw new DomainException("Returned quantity must be between 0 and the quantity brought.");
        QtyReturned = qtyReturned;
    }
}
