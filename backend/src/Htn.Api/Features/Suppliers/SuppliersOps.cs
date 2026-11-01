using Htn.Api.Common;
using Htn.Domain.Suppliers;
using Htn.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Htn.Api.Features.Suppliers;

/// <summary>The <c>code</c> extension on every suppliers problem response, mirrored in the frontend.</summary>
public static class SupplierErrorCodes
{
    public const string DuplicateName = "duplicate_name";
    public const string AlreadyDeactivated = "already_deactivated";
    public const string DocumentAlreadyVoided = "document_already_voided";
    public const string InvalidTransition = "invalid_transition";
    public const string AlreadyVoided = "already_voided";
    public const string NotQuotation = "not_quotation";
    public const string EventNotEnded = "event_not_ended";
    public const string AlreadyRated = "already_rated";
    public const string AlreadyHired = "already_hired";
}

/// <summary>Audit action strings, <c>&lt;entity&gt;.&lt;verb&gt;</c> like the rest of the system.</summary>
public static class SupplierAuditActions
{
    public const string Create = "supplier.create";
    public const string Update = "supplier.update";
    public const string Deactivate = "supplier.deactivate";
    public const string Logo = "supplier.logo";
    public const string DocumentUpload = "supplier_document.upload";
    public const string DocumentVoid = "supplier_document.void";
    public const string HireCreate = "event_supplier.create";
    public const string HireUpdate = "event_supplier.update";
    public const string HireAdvance = "event_supplier.advance";
    public const string HireVoid = "event_supplier.void";
    public const string HireDelete = "event_supplier.delete";
    public const string Rate = "event_supplier.rate";
}

public static class SuppliersOps
{
    public static IResult Conflict(string detail, string code) =>
        Problems.Result(StatusCodes.Status409Conflict, "That cannot be done right now.", detail, code);

    public static Task<bool> DuplicateNameAsync(HtnDbContext db, string nameKey, Guid? exceptId, CancellationToken ct) =>
        db.Suppliers.AnyAsync(s => s.DeactivatedAt == null && s.NameKey == nameKey && (exceptId == null || s.Id != exceptId), ct);
}
