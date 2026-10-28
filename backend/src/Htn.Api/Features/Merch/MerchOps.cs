using Htn.Api.Common;

namespace Htn.Api.Features.Merch;

/// <summary>The <c>code</c> extension on every merch problem response, mirrored in the frontend.</summary>
public static class MerchErrorCodes
{
    public const string AlreadyDeactivated = "already_deactivated";
    public const string DuplicateLabel = "duplicate_label";
    public const string InsufficientStock = "insufficient_stock";
    public const string UnknownCode = "unknown_code";
    public const string NoBuyer = "no_buyer";

    // Phase 8B: the POS membership tile ("Membership 2026 ... yearly fee").
    /// <summary>The resolved buyer already is a Person (any level) — nothing to promote, remove it from the cart.</summary>
    public const string AlreadyAMember = "already_a_member";
    /// <summary>The resolved Attendee has never checked in anywhere, so <c>MemberRule</c> refuses the promotion.</summary>
    public const string NotCheckedIn = "not_checked_in";
    /// <summary>No Person or Attendee resolved for this sale's buyer — a free-text walk-in name can't buy membership.</summary>
    public const string BuyerRequiredForMembership = "buyer_required_for_membership";
    /// <summary>This event isn't the (or a) main event, so there's no <c>MembershipFee</c> to sell here.</summary>
    public const string MembershipNotAvailable = "membership_not_available";
}

/// <summary>Audit action strings, <c>&lt;entity&gt;.&lt;verb&gt;</c> like the rest of the system.</summary>
public static class MerchAuditActions
{
    public const string ItemCreate = "merch_item.create";
    public const string ItemUpdate = "merch_item.update";
    public const string ItemDeactivate = "merch_item.deactivate";
    public const string VariantCreate = "merch_variant.create";
    public const string VariantUpdate = "merch_variant.update";
    public const string VariantDeactivate = "merch_variant.deactivate";
    public const string DiscountsUpdate = "event_merch_settings.update";
    public const string SaleCreate = "merch_sale.create";

    /// <summary>A membership-included POS sale promoted an Attendee to a Member in the same transaction.</summary>
    public const string PersonCreatedViaMembershipSale = "person.created_via_membership_sale";
}

public static class MerchOps
{
    public static IResult Conflict(string detail, string code) =>
        Problems.Result(StatusCodes.Status409Conflict, "That cannot be done right now.", detail, code);
}
