using Htn.Domain.Merch;
using Htn.Domain.Organization;

namespace Htn.Api.Features.Merch;

// Requests: every property nullable so FluentValidation, not the model binder, produces the message.

public sealed record MerchItemRequest(string? Name, decimal? Price, string? SwatchColor);

public sealed record MerchVariantRequest(string? Label, int? StockOnHand);

public sealed record MerchVariantAdjustRequest(string? Label, int? StockOnHand);

public sealed record ReasonRequest(string? Reason);

public sealed record EventMerchSettingsRequest(int? GoonPct, int? ApprenticePct, int? MemberPct, int? BoardPct);

public sealed record MerchSaleLineRequest(Guid? VariantId, int? Qty);

/// <summary>
/// <see cref="ClientRequestId"/> is a GUID the POS client generates fresh per distinct sale attempt and resends
/// unchanged on any retry of that same attempt (network retry, double-tap) — the dedup key that makes
/// <c>POST /pos/sale</c> idempotent.
/// <para>
/// <see cref="IncludesMembership"/> (Phase 8B): the design mock's "Membership 2026" POS tile always adds the fee
/// for whoever this sale's own buyer already is — there is no third-party-gift flow shown — so this is a plain
/// flag tied to the sale's buyer, not a second, separate <c>membershipForPersonId</c>/<c>membershipForAttendeeId</c>
/// pair. <see cref="Lines"/> may be empty when this is true (membership-only, no merch in the cart).
/// </para>
/// </summary>
public sealed record MerchSaleRequest(
    Guid? ClientRequestId, Guid? BuyerPersonId, Guid? BuyerAttendeeId, string? BuyerName, IReadOnlyList<MerchSaleLineRequest>? Lines,
    MerchSaleMethod? Method, string? Station, bool? IncludesMembership);

public sealed record PosScanRequest(string? Payload);

// Responses.

public sealed record MerchVariantDto(Guid Id, Guid ItemId, string Label, int StockOnHand, bool IsActive, bool LowStock);

public sealed record MerchItemDto(
    Guid Id, Guid EventId, string Name, decimal Price, string? SwatchColor, bool IsActive, IReadOnlyList<MerchVariantDto> Variants);

public sealed record EventMerchSettingsDto(Guid EventId, int GoonPct, int ApprenticePct, int MemberPct, int BoardPct);

public sealed record MerchSaleLineDto(Guid Id, Guid ItemId, string ItemName, Guid VariantId, string VariantLabel, int Qty, decimal UnitPriceSnapshot, decimal LineTotal);

public sealed record MerchSaleDto(
    Guid Id, Guid EventId, Guid? BuyerPersonId, Guid? BuyerAttendeeId, string BuyerNameSnapshot, PersonLevel? BuyerLevelSnapshot,
    decimal Subtotal, int DiscountPct, decimal DiscountAmount, decimal Total, MerchSaleMethod Method, Guid CashierId,
    string? Station, DateTimeOffset SoldAt, IReadOnlyList<MerchSaleLineDto> Lines,
    bool MembershipFeeIncluded, decimal? MembershipFeeAmount);

/// <summary>The Merch page's KPI strip. <c>Money</c> and <c>DiscountsGiven</c> are board/manage-only at the DTO
/// (stripped for a committee lead who can only sell, per the brief's "money/discount config shown only if they can
/// Manage" default).</summary>
public sealed record MerchKpiDto(int OrderCount, int ItemsSold, int ItemsInStock, int LowStockVariantCount, decimal? TotalSales, decimal? DiscountsGiven);

public sealed record MerchDashboardDto(
    bool CanManage, MerchKpiDto Kpi, IReadOnlyList<MerchItemDto> Items, IReadOnlyList<MerchSaleDto> Sales);

/// <summary>One hit from the POS buyer search or a successful QR scan: a <c>Person</c> (tagged with their real
/// level) or an unmatched <c>Attendee</c> (tagged "Attendee").</summary>
public sealed record BuyerHitDto(string Kind, Guid? PersonId, Guid? AttendeeId, string Name, string Level, string? Email);
