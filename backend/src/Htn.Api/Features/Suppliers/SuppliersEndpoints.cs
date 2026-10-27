using System.Security.Cryptography;
using Htn.Api.Auth;
using Htn.Api.Common;
using Htn.Domain.Files;
using Htn.Domain.Suppliers;
using Htn.Infrastructure.Audit;
using Htn.Infrastructure.Data;
using Htn.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;

namespace Htn.Api.Features.Suppliers;

/// <summary>
/// Phase 7C: the global HTN supplier list (venue, catering, printing, tech/AV, merch, transport), their documents
/// (BIR 2303, business permit) and the per-event hire history on the detail screen. Writes are board-only
/// (<c>Suppliers.Manage</c>); reads are board or any committee lead, anywhere (<c>Suppliers.View</c>), mirroring
/// <c>Directory.Manage</c>/<c>Directory.View</c> — contacts, documents and reviews are lead-visible, only prices,
/// TIN and bank details are stripped at the DTO for a non-board caller (never in the UI alone). Append-only:
/// deactivate/void with a reason, never delete; every change is audited.
/// </summary>
public static class SuppliersEndpoints
{
    public static RouteGroupBuilder MapSuppliers(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/suppliers").WithTags("Suppliers");

        g.MapGet("/", List).RequireAuthorization(Policies.SuppliersView);
        g.MapGet("/{id:guid}", Detail).RequireAuthorization(Policies.SuppliersView);
        g.MapPost("/", Create).RequireAuthorization(Policies.SuppliersManage)
            .RequireRateLimiting(SponsorsRateLimiting.Write).Validate<SupplierRequest>();
        g.MapPatch("/{id:guid}", Update).RequireAuthorization(Policies.SuppliersManage)
            .RequireRateLimiting(SponsorsRateLimiting.Write).Validate<SupplierUpdateRequest>();
        g.MapPost("/{id:guid}/deactivate", Deactivate).RequireAuthorization(Policies.SuppliersManage)
            .RequireRateLimiting(SponsorsRateLimiting.Write).Validate<ReasonRequest>();
        g.MapPut("/{id:guid}/logo", UploadLogo).RequireAuthorization(Policies.SuppliersManage)
            .RequireRateLimiting(SponsorsRateLimiting.Write);

        g.MapPost("/{id:guid}/documents", UploadDocument).RequireAuthorization(Policies.SuppliersManage)
            .RequireRateLimiting(SponsorsRateLimiting.Write);
        g.MapGet("/{id:guid}/documents/{docId:guid}/download", DownloadDocument).RequireAuthorization(Policies.SuppliersView);
        g.MapPost("/{id:guid}/documents/{docId:guid}/void", VoidDocument).RequireAuthorization(Policies.SuppliersManage)
            .RequireRateLimiting(SponsorsRateLimiting.Write).Validate<ReasonRequest>();

        return g;
    }

    // ---------- Suppliers ----------

    private static async Task<IResult> List(SupplierCategory? category, HttpContext http, HtnDbContext db, CancellationToken ct)
    {
        // Suppliers.Manage is a plain board-only role check (no resource), so the board-only-ness of the response is
        // decided from the role directly rather than round-tripping the authorization service.
        var boardOnly = http.User.IsInRole(Htn.Infrastructure.Identity.Roles.Board);
        var query = db.Suppliers.AsNoTracking().Where(s => s.DeactivatedAt == null);
        if (category is { } c) query = query.Where(s => s.Category == c);
        var suppliers = await query.OrderBy(s => s.Name).ToListAsync(ct);
        return TypedResults.Ok(suppliers.Select(s => SuppliersViews.Supplier(s, boardOnly)).ToList());
    }

    private static async Task<IResult> Detail(
        Guid id, HttpContext http, HtnDbContext db, ICommitteeContext committees, ICurrentUser me, CancellationToken ct)
    {
        var supplier = await db.Suppliers.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct);
        if (supplier is null) return TypedResults.NotFound();
        var isBoard = http.User.IsInRole(Htn.Infrastructure.Identity.Roles.Board);
        return TypedResults.Ok(await DetailDtoAsync(db, supplier, boardOnly: isBoard, isBoard, committees, me.Id, ct));
    }

    private static async Task<IResult> Create(
        SupplierRequest req, HtnDbContext db, IAuditLog audit, ICurrentUser me, TimeProvider clock, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var supplier = Supplier.Create(
            new NewSupplier(req.Category!.Value, req.Name!, req.ContactName, req.ContactEmail, req.ContactPhone,
                req.IssuesOr!.Value, req.Tin, req.BankDetails, req.Notes),
            me.Id!.Value, now);
        if (await SuppliersOps.DuplicateNameAsync(db, supplier.NameKey, null, ct))
            return SuppliersOps.Conflict("A supplier with that name already exists.", SupplierErrorCodes.DuplicateName);

        db.Suppliers.Add(supplier);
        audit.Record(me.Id, SupplierAuditActions.Create, nameof(Supplier), supplier.Id.ToString(),
            after: new
            {
                supplier.Name, category = supplier.Category.ToString(), supplier.ContactName, supplier.ContactEmail,
                supplier.ContactPhone, supplier.IssuesOr, supplier.Tin, supplier.BankDetails, supplier.Notes,
            });
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(SuppliersViews.Supplier(supplier, includeBoardOnly: true));
    }

    private static async Task<IResult> Update(
        Guid id, SupplierUpdateRequest req, HtnDbContext db, IAuditLog audit, ICurrentUser me, TimeProvider clock, CancellationToken ct)
    {
        var supplier = await db.Suppliers.FirstOrDefaultAsync(s => s.Id == id && s.DeactivatedAt == null, ct);
        if (supplier is null) return TypedResults.NotFound();

        var now = clock.GetUtcNow();
        var before = new
        {
            supplier.ContactName, supplier.ContactEmail, supplier.ContactPhone, supplier.IssuesOr,
            supplier.Tin, supplier.BankDetails, supplier.Notes,
        };
        supplier.Update(new UpdateSupplier(req.ContactName, req.ContactEmail, req.ContactPhone, req.IssuesOr!.Value, req.Tin, req.BankDetails, req.Notes),
            me.Id!.Value, now);
        audit.Record(me.Id, SupplierAuditActions.Update, nameof(Supplier), supplier.Id.ToString(),
            before, new
            {
                supplier.ContactName, supplier.ContactEmail, supplier.ContactPhone, supplier.IssuesOr,
                supplier.Tin, supplier.BankDetails, supplier.Notes,
            });
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(SuppliersViews.Supplier(supplier, includeBoardOnly: true));
    }

    private static async Task<IResult> Deactivate(
        Guid id, ReasonRequest req, HtnDbContext db, IAuditLog audit, ICurrentUser me, TimeProvider clock, CancellationToken ct)
    {
        var supplier = await db.Suppliers.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (supplier is null) return TypedResults.NotFound();
        if (!supplier.IsActive) return SuppliersOps.Conflict("This supplier is already deactivated.", SupplierErrorCodes.AlreadyDeactivated);

        supplier.Deactivate(req.Reason!, me.Id!.Value, clock.GetUtcNow());
        audit.Record(me.Id, SupplierAuditActions.Deactivate, nameof(Supplier), supplier.Id.ToString(), reason: req.Reason);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> UploadLogo(
        Guid id, HttpRequest request, HtnDbContext db, IFileStorage storage, IAuditLog audit, ICurrentUser me,
        TimeProvider clock, CancellationToken ct)
    {
        var supplier = await db.Suppliers.FirstOrDefaultAsync(s => s.Id == id && s.DeactivatedAt == null, ct);
        if (supplier is null) return TypedResults.NotFound();

        var (file, error) = await Uploads.ReadSingleFileAsync(
            request, "file", StoredFilePurposes.MaxBytesFor(StoredFilePurposes.SupplierLogo), "The logo must be 1 MB or smaller.", ct);
        if (error is not null) return error;
        var image = FileSignatures.DetectImage(file!.Bytes, allowJpeg: true);
        if (image is null) return Problems.Field("file", "Upload a PNG or JPG image.", "unsupported_type");
        if (image.Value.Width > FileSignatures.MaxImageDimension || image.Value.Height > FileSignatures.MaxImageDimension)
            return Problems.Field("file", $"The logo can be at most {FileSignatures.MaxImageDimension}×{FileSignatures.MaxImageDimension} pixels.", "image_too_large");
        var stripped = FileSignatures.StripMetadata(file.Bytes);
        if (stripped is null) return Problems.Field("file", "Upload a PNG or JPG image.", "unsupported_type");

        var now = clock.GetUtcNow();
        var stored = StoredFile.Create(image.Value.ContentType, stripped.LongLength,
            Convert.ToHexStringLower(SHA256.HashData(stripped)), StoredFilePurposes.SupplierLogo, file.FileName, now, me.Id);
        await storage.SaveAsync(stored.Id, stripped, ct);
        db.StoredFiles.Add(stored);
        supplier.SetLogo(stored.Id, me.Id!.Value, now);
        audit.Record(me.Id, SupplierAuditActions.Logo, nameof(Supplier), supplier.Id.ToString(), after: new { fileId = stored.Id, stored.SizeBytes });
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(SuppliersViews.Supplier(supplier, includeBoardOnly: true));
    }

    // ---------- Documents ----------

    private static async Task<IResult> UploadDocument(
        Guid id, HttpRequest request, HtnDbContext db, IFileStorage storage, IAuditLog audit, ICurrentUser me,
        TimeProvider clock, CancellationToken ct)
    {
        var supplier = await db.Suppliers.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id && s.DeactivatedAt == null, ct);
        if (supplier is null) return TypedResults.NotFound();

        var (file, uploadError) = await Uploads.ReadSingleFileAsync(request, "file",
            StoredFilePurposes.SupplierDocumentMaxBytes, "The document must be 5 MB or smaller.", ct);
        if (uploadError is not null) return uploadError;
        if (FileSignatures.DetectAttachment(file!.Bytes) is not { } detected)
            return Problems.Field("file", "Upload a PNG, JPG or PDF.", "unsupported_type");

        if (!Enum.TryParse<SupplierDocumentKind>(request.Form["kind"], out var kind) || !Enum.IsDefined(kind))
            return Problems.Field("kind", "Pick a document kind.");

        var now = clock.GetUtcNow();
        var (contentType, bytes) = detected;
        var stored = StoredFile.Create(contentType, bytes.LongLength, Convert.ToHexStringLower(SHA256.HashData(bytes)),
            StoredFilePurposes.SupplierDocument, file.FileName, now, me.Id);
        await storage.SaveAsync(stored.Id, bytes, ct);
        db.StoredFiles.Add(stored);

        var document = SupplierDocument.Create(id, kind, stored.Id, me.Id, now);
        db.SupplierDocuments.Add(document);
        audit.Record(me.Id, SupplierAuditActions.DocumentUpload, nameof(SupplierDocument), document.Id.ToString(),
            after: new { supplierId = id, kind = kind.ToString(), fileId = stored.Id });
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(SuppliersViews.Document(document));
    }

    /// <summary>
    /// Streams the document. Lead-visible per the domain rule (docs aren't board-only, prices/TIN/bank are), so this
    /// sits behind <c>Suppliers.View</c>, not <c>Suppliers.Manage</c>. Always a download with <c>nosniff</c>,
    /// mirroring <c>finance_receipt</c>: a PDF rendered inline is a script execution context.
    /// </summary>
    private static async Task<IResult> DownloadDocument(
        Guid id, Guid docId, HttpContext http, HtnDbContext db, IFileStorage storage, CancellationToken ct)
    {
        var found = await db.SupplierDocuments.AsNoTracking()
            .Where(d => d.Id == docId && d.SupplierId == id)
            .Join(db.StoredFiles.AsNoTracking(), d => d.FileId, f => f.Id, (d, f) => new { f.Id, f.ContentType })
            .FirstOrDefaultAsync(ct);
        if (found is null) return TypedResults.NotFound();
        var stream = await storage.OpenReadAsync(found.Id, ct);
        if (stream is null) return TypedResults.NotFound();

        var headers = http.Response.Headers;
        headers[HeaderNames.XContentTypeOptions] = "nosniff";
        headers[HeaderNames.ContentSecurityPolicy] = "default-src 'none'";
        headers[HeaderNames.CacheControl] = "private, no-store";
        headers["Cross-Origin-Resource-Policy"] = "same-origin";
        var extension = found.ContentType == FileSignatures.PdfContentType ? "pdf"
            : found.ContentType == FileSignatures.PngContentType ? "png" : "jpg";
        return TypedResults.File(stream, found.ContentType, $"supplier-document-{docId}.{extension}");
    }

    private static async Task<IResult> VoidDocument(
        Guid id, Guid docId, ReasonRequest req, HtnDbContext db, IAuditLog audit, ICurrentUser me, TimeProvider clock, CancellationToken ct)
    {
        var document = await db.SupplierDocuments.FirstOrDefaultAsync(d => d.Id == docId && d.SupplierId == id, ct);
        if (document is null) return TypedResults.NotFound();
        if (document.IsVoided) return SuppliersOps.Conflict("This document is already removed.", SupplierErrorCodes.DocumentAlreadyVoided);

        document.Void(req.Reason!, me.Id!.Value, clock.GetUtcNow());
        audit.Record(me.Id, SupplierAuditActions.DocumentVoid, nameof(SupplierDocument), document.Id.ToString(), reason: req.Reason);
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(SuppliersViews.Document(document));
    }

    // ---------- Helpers ----------

    internal static async Task<SupplierDetailDto> DetailDtoAsync(
        HtnDbContext db, Supplier supplier, bool boardOnly, bool isBoard, ICommitteeContext committees, Guid? callerId, CancellationToken ct)
    {
        var documents = await db.SupplierDocuments.AsNoTracking()
            .Where(d => d.SupplierId == supplier.Id).OrderByDescending(d => d.UploadedAt).ToListAsync(ct);
        var hires = await db.EventSuppliers.AsNoTracking().Where(h => h.SupplierId == supplier.Id).ToListAsync(ct);
        var hireDtos = await EventSuppliersEndpoints.HireDtosAsync(db, hires, boardOnly, isBoard, committees, callerId, ct);
        return new SupplierDetailDto(SuppliersViews.Supplier(supplier, boardOnly), [.. documents.Select(SuppliersViews.Document)], hireDtos);
    }
}
