using Htn.Domain.Merch;

namespace Htn.Api.Features.Merch;

/// <summary>Projections from the merch entities to the DTOs the screens render. Low stock is &lt;= this many units.</summary>
public static class MerchViews
{
    public const int LowStockThreshold = 5;

    public static MerchVariantDto Variant(MerchVariant v) =>
        new(v.Id, v.ItemId, v.Label, v.StockOnHand, v.IsActive, v.IsActive && v.StockOnHand <= LowStockThreshold);

    public static MerchItemDto Item(MerchItem item, IReadOnlyList<MerchVariant> variants) =>
        new(item.Id, item.EventId, item.Name, item.Price, item.SwatchColor, item.IsActive,
            [.. variants.Where(v => v.ItemId == item.Id).OrderBy(v => v.Label).Select(Variant)]);

    public static EventMerchSettingsDto Settings(EventMerchSettings s) =>
        new(s.EventId, s.GoonPct, s.ApprenticePct, s.MemberPct, s.BoardPct);

    public static MerchSaleLineDto Line(MerchSaleLine l, string itemName, string variantLabel) =>
        new(l.Id, l.ItemId, itemName, l.VariantId, variantLabel, l.Qty, l.UnitPriceSnapshot, l.LineTotal);

    public static MerchSaleDto Sale(MerchSale s, IReadOnlyList<MerchSaleLineDto> lines) =>
        new(s.Id, s.EventId, s.BuyerPersonId, s.BuyerAttendeeId, s.BuyerNameSnapshot, s.BuyerLevelSnapshot,
            s.Subtotal, s.DiscountPct, s.DiscountAmount, s.Total, s.Method, s.CashierId, s.Station, s.SoldAt, lines,
            s.MembershipFeeIncluded, s.MembershipFeeAmount);
}
