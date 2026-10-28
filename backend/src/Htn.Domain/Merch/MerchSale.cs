using Htn.Domain.Common;
using Htn.Domain.Organization;

namespace Htn.Domain.Merch;

/// <summary>One line the caller wants rung up: which variant, how many, and the price it will be sold at.</summary>
public sealed record NewMerchSaleLine(Guid ItemId, Guid VariantId, int Qty, decimal UnitPriceSnapshot);

/// <summary>
/// One POS transaction (Phase 8A): a buyer, a cashier, a payment method and one or more <see cref="MerchSaleLine"/>
/// rows. <b>Fully a snapshot, like <see cref="Htn.Domain.Tasks.PointsAward"/> and <c>EventInvitation</c>'s send
/// fields</b>: <see cref="BuyerNameSnapshot"/>, <see cref="BuyerLevelSnapshot"/> and every money field are frozen the
/// moment the sale is created and never recomputed from the live <see cref="MerchItem"/>/<see cref="Person"/> rows —
/// a later price change, discount-table edit or promotion never rewrites what the buyer actually paid. Sales are
/// permanent: never updated, never deleted, enforced again at the database (<c>Merch_Guards</c>).
/// </summary>
public sealed class MerchSale
{
    public const int BuyerNameMaxLength = 160;
    public const int StationMaxLength = 60;

    private MerchSale() { }

    public Guid Id { get; private set; }
    public Guid EventId { get; private set; }

    /// <summary>
    /// A GUID the POS client generates fresh per distinct sale attempt and holds in its own state, resending the
    /// <i>same</i> value on any retry of that same attempt (a network retry, a double-tap on flaky wifi). Unique
    /// per <see cref="EventId"/> so a retry of the same attempt can never create a second <see cref="MerchSale"/>
    /// row, without colliding across different events. Not public-facing; purely a dedup key.
    /// </summary>
    public Guid ClientRequestId { get; private set; }

    public Guid? BuyerPersonId { get; private set; }
    public Guid? BuyerAttendeeId { get; private set; }

    /// <summary>The receipt/audit label regardless of which FK resolved, or a free-text walk-in name if neither did.</summary>
    public string BuyerNameSnapshot { get; private set; } = default!;

    public PersonLevel? BuyerLevelSnapshot { get; private set; }
    public decimal Subtotal { get; private set; }
    public int DiscountPct { get; private set; }
    public decimal DiscountAmount { get; private set; }
    public decimal Total { get; private set; }
    public MerchSaleMethod Method { get; private set; }
    public Guid CashierId { get; private set; }
    public string? Station { get; private set; }
    public DateTimeOffset SoldAt { get; private set; }

    /// <summary>
    /// Whether this sale also included the Membership 2026-style POS tile for its own buyer (Phase 8B). The fee is
    /// a flat yearly dues amount from <c>Event.MembershipFee</c>, not a <see cref="MerchItem"/> — it is carried as
    /// its own field rather than bent into a <see cref="MerchSaleLine"/>, and it is <b>never discounted</b> (the
    /// per-level merch discount applies only to <see cref="Subtotal"/>, which is merch-only).
    /// </summary>
    public bool MembershipFeeIncluded { get; private set; }

    public decimal? MembershipFeeAmount { get; private set; }

    /// <summary>
    /// Builds the sale and its lines together so the money math (subtotal, discount, total) is computed exactly
    /// once, from the same snapshot prices the lines carry. The caller has already resolved
    /// <paramref name="discountPct"/> from <see cref="EventMerchSettings"/> and checked stock; this constructor only
    /// validates shape and arithmetic, never looks anything up. <paramref name="membershipFeeAmount"/>, when given,
    /// is added to <see cref="Total"/> on top of the discounted merch subtotal, undiscounted.
    /// </summary>
    public static (MerchSale Sale, List<MerchSaleLine> Lines) Create(
        Guid eventId, Guid clientRequestId, Guid? buyerPersonId, Guid? buyerAttendeeId, string buyerNameSnapshot, PersonLevel? buyerLevelSnapshot,
        IReadOnlyList<NewMerchSaleLine> lineInputs, int discountPct, MerchSaleMethod method, Guid cashierId, string? station,
        bool membershipFeeIncluded, decimal? membershipFeeAmount, DateTimeOffset at)
    {
        if (eventId == Guid.Empty) throw new DomainException("An event is required.");
        if (clientRequestId == Guid.Empty) throw new DomainException("A client request id is required.");
        if ((lineInputs is null || lineInputs.Count == 0) && !membershipFeeIncluded)
            throw new DomainException("A sale needs at least one line.");
        if (cashierId == Guid.Empty) throw new DomainException("A cashier is required.");
        MerchRules.Defined(method, "payment method");
        if (buyerLevelSnapshot is { } lvl) MerchRules.Defined(lvl, "buyer level");
        var pct = MerchRules.Pct(discountPct, "Discount percent");
        var name = MerchRules.Required(buyerNameSnapshot, "Buyer name", BuyerNameMaxLength);
        if (membershipFeeIncluded && (membershipFeeAmount is null || membershipFeeAmount < 0))
            throw new DomainException("A membership fee amount is required when the membership line is included.");
        if (!membershipFeeIncluded && membershipFeeAmount is not null)
            throw new DomainException("A membership fee amount is only set when the membership line is included.");

        var saleId = Guid.CreateVersion7(at);
        var lines = (lineInputs ?? []).Select(l => MerchSaleLine.Create(saleId, l, at)).ToList();
        var subtotal = decimal.Round(lines.Sum(l => l.LineTotal), 2);
        var discountAmount = decimal.Round(subtotal * pct / 100m, 2);
        var membershipFee = membershipFeeIncluded ? decimal.Round(membershipFeeAmount!.Value, 2) : (decimal?)null;
        var total = subtotal - discountAmount + (membershipFee ?? 0m);

        var sale = new MerchSale
        {
            Id = saleId,
            EventId = eventId,
            ClientRequestId = clientRequestId,
            BuyerPersonId = buyerPersonId,
            BuyerAttendeeId = buyerAttendeeId,
            BuyerNameSnapshot = name,
            BuyerLevelSnapshot = buyerLevelSnapshot,
            Subtotal = subtotal,
            DiscountPct = pct,
            DiscountAmount = discountAmount,
            Total = total,
            Method = method,
            CashierId = cashierId,
            Station = MerchRules.Optional(station, "Station", StationMaxLength),
            MembershipFeeIncluded = membershipFeeIncluded,
            MembershipFeeAmount = membershipFee,
            SoldAt = at,
        };
        return (sale, lines);
    }
}

/// <summary>
/// One item/variant/quantity on a <see cref="MerchSale"/>. <see cref="UnitPriceSnapshot"/> and
/// <see cref="LineTotal"/> are frozen at sale time and never recomputed from the live <see cref="MerchItem.Price"/>.
/// </summary>
public sealed class MerchSaleLine
{
    private MerchSaleLine() { }

    public Guid Id { get; private set; }
    public Guid SaleId { get; private set; }
    public Guid ItemId { get; private set; }
    public Guid VariantId { get; private set; }
    public int Qty { get; private set; }
    public decimal UnitPriceSnapshot { get; private set; }
    public decimal LineTotal { get; private set; }

    public static MerchSaleLine Create(Guid saleId, NewMerchSaleLine data, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (saleId == Guid.Empty) throw new DomainException("A sale is required.");
        if (data.ItemId == Guid.Empty) throw new DomainException("An item is required.");
        if (data.VariantId == Guid.Empty) throw new DomainException("A variant is required.");
        if (data.Qty <= 0) throw new DomainException("Quantity must be at least 1.");
        var unitPrice = MerchRules.NonNegativeMoney(data.UnitPriceSnapshot, "Unit price");
        return new MerchSaleLine
        {
            Id = Guid.CreateVersion7(at),
            SaleId = saleId,
            ItemId = data.ItemId,
            VariantId = data.VariantId,
            Qty = data.Qty,
            UnitPriceSnapshot = unitPrice,
            LineTotal = decimal.Round(unitPrice * data.Qty, 2),
        };
    }
}
