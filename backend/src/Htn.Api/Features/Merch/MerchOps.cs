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
}

public static class MerchOps
{
    public static IResult Conflict(string detail, string code) =>
        Problems.Result(StatusCodes.Status409Conflict, "That cannot be done right now.", detail, code);
}
