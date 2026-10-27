using System.Security.Cryptography;
using Htn.Api.Auth;
using Htn.Api.Common;
using Htn.Domain.Directory;
using Htn.Domain.Files;
using Htn.Infrastructure.Audit;
using Htn.Infrastructure.Data;
using Htn.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

namespace Htn.Api.Features.Directory;

/// <summary>
/// Phase 7A: the global HTN directory of organizations (government agencies, companies, universities, non-profits)
/// and their contacts — distinct from a per-event sponsor, lead or partner, which link to one of these by
/// <c>DirectoryOrgId</c> once the Phase 7 backfill runs. Writes are board-only (<c>Directory.Manage</c>); reads are
/// board or any committee lead, anywhere (<c>Directory.View</c>), because contact follow-up isn't tied to one event.
/// Append-only: deactivate/void with a reason, never delete; every change is audited.
/// </summary>
public static class DirectoryEndpoints
{
    public static RouteGroupBuilder MapDirectory(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/directory").WithTags("Directory");

        g.MapGet("/", List).RequireAuthorization(Policies.DirectoryView);
        g.MapGet("/{id:guid}", Detail).RequireAuthorization(Policies.DirectoryView);
        g.MapPost("/", Create).RequireAuthorization(Policies.DirectoryManage)
            .RequireRateLimiting(SponsorsRateLimiting.Write).Validate<DirectoryOrgRequest>();
        g.MapPatch("/{id:guid}", Update).RequireAuthorization(Policies.DirectoryManage)
            .RequireRateLimiting(SponsorsRateLimiting.Write).Validate<DirectoryOrgUpdateRequest>();
        g.MapPost("/{id:guid}/deactivate", Deactivate).RequireAuthorization(Policies.DirectoryManage)
            .RequireRateLimiting(SponsorsRateLimiting.Write).Validate<ReasonRequest>();
        g.MapPut("/{id:guid}/logo", UploadLogo).RequireAuthorization(Policies.DirectoryManage)
            .RequireRateLimiting(SponsorsRateLimiting.Write);

        g.MapPost("/{id:guid}/contacts", CreateContact).RequireAuthorization(Policies.DirectoryManage)
            .RequireRateLimiting(SponsorsRateLimiting.Write).Validate<DirectoryContactRequest>();
        g.MapPatch("/{id:guid}/contacts/{contactId:guid}", UpdateContact).RequireAuthorization(Policies.DirectoryManage)
            .RequireRateLimiting(SponsorsRateLimiting.Write).Validate<DirectoryContactRequest>();
        g.MapPost("/{id:guid}/contacts/{contactId:guid}/still-correct", StillCorrect).RequireAuthorization(Policies.DirectoryManage)
            .RequireRateLimiting(SponsorsRateLimiting.Write);
        g.MapPost("/{id:guid}/contacts/{contactId:guid}/head-changed", HeadChanged).RequireAuthorization(Policies.DirectoryManage)
            .RequireRateLimiting(SponsorsRateLimiting.Write).Validate<HeadChangedRequest>();
        g.MapPost("/{id:guid}/contacts/{contactId:guid}/void", VoidContact).RequireAuthorization(Policies.DirectoryManage)
            .RequireRateLimiting(SponsorsRateLimiting.Write).Validate<ReasonRequest>();

        return g;
    }

    // ---------- Organizations ----------

    private static async Task<IResult> List(
        DirectoryOrgKind? kind, bool? needsCheck, HtnDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var staleCutoff = now - TimeSpan.FromDays(DirectoryContact.StaleAfterDays);

        var orgsQuery = db.DirectoryOrgs.AsNoTracking().Where(o => o.DeactivatedAt == null);
        if (kind is { } k) orgsQuery = orgsQuery.Where(o => o.Kind == k);

        if (needsCheck is { } nc)
        {
            // Computed server-side: an org needs a check when its live primary contact (or, lacking one, every
            // live contact) has never been verified or was verified more than 90 days ago.
            var orgIdsNeedingCheck = db.DirectoryContacts.AsNoTracking()
                .Where(c => c.VoidedAt == null && c.IsPrimary && (c.VerifiedAt == null || c.VerifiedAt < staleCutoff))
                .Select(c => c.OrgId);
            var orgIdsWithPrimary = db.DirectoryContacts.AsNoTracking().Where(c => c.VoidedAt == null && c.IsPrimary).Select(c => c.OrgId);
            var orgIdsWithAnyLiveContact = db.DirectoryContacts.AsNoTracking().Where(c => c.VoidedAt == null).Select(c => c.OrgId);
            var orgIdsWithAllStaleAndNoPrimary = db.DirectoryContacts.AsNoTracking()
                .Where(c => c.VoidedAt == null)
                .GroupBy(c => c.OrgId)
                .Where(g => !orgIdsWithPrimary.Contains(g.Key) && g.All(c => c.VerifiedAt == null || c.VerifiedAt < staleCutoff))
                .Select(g => g.Key);
            var needsCheckIds = orgIdsNeedingCheck.Union(orgIdsWithAllStaleAndNoPrimary);
            orgsQuery = nc
                ? orgsQuery.Where(o => needsCheckIds.Contains(o.Id) || !orgIdsWithAnyLiveContact.Contains(o.Id))
                : orgsQuery.Where(o => !needsCheckIds.Contains(o.Id) && orgIdsWithAnyLiveContact.Contains(o.Id));
        }

        var orgs = await orgsQuery.OrderBy(o => o.Name).ToListAsync(ct);
        var contacts = await db.DirectoryContacts.AsNoTracking()
            .Where(c => c.VoidedAt == null && orgs.Select(o => o.Id).Contains(c.OrgId))
            .ToListAsync(ct);
        var byOrg = contacts.GroupBy(c => c.OrgId).ToDictionary(g => g.Key, g => (IReadOnlyList<DirectoryContact>)[.. g]);

        return TypedResults.Ok(orgs.Select(o => DirectoryViews.Org(o, byOrg.GetValueOrDefault(o.Id, []), now)).ToList());
    }

    private static async Task<IResult> Detail(Guid id, HtnDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var org = await db.DirectoryOrgs.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id, ct);
        if (org is null) return TypedResults.NotFound();
        return TypedResults.Ok(await DetailDtoAsync(db, org, clock.GetUtcNow(), ct));
    }

    private static async Task<IResult> Create(
        DirectoryOrgRequest req, HtnDbContext db, IAuditLog audit, ICurrentUser me, TimeProvider clock, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var org = DirectoryOrg.Create(new NewDirectoryOrg(req.Kind!.Value, req.Name!, req.AddresseeSalutation, req.WebsiteUrl, req.Notes),
            me.Id!.Value, now);
        if (await DirectoryOps.DuplicateNameAsync(db, org.NameKey, null, ct))
            return DirectoryOps.Conflict("An organization with that name already exists.", DirectoryErrorCodes.DuplicateName);

        db.DirectoryOrgs.Add(org);
        audit.Record(me.Id, DirectoryAuditActions.OrgCreate, nameof(DirectoryOrg), org.Id.ToString(),
            after: new { org.Name, kind = org.Kind.ToString() });
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await DetailDtoAsync(db, org, now, ct));
    }

    private static async Task<IResult> Update(
        Guid id, DirectoryOrgUpdateRequest req, HtnDbContext db, IAuditLog audit, ICurrentUser me, TimeProvider clock, CancellationToken ct)
    {
        var org = await db.DirectoryOrgs.FirstOrDefaultAsync(o => o.Id == id && o.DeactivatedAt == null, ct);
        if (org is null) return TypedResults.NotFound();

        var now = clock.GetUtcNow();
        org.Update(new UpdateDirectoryOrg(req.AddresseeSalutation, req.WebsiteUrl, req.Notes), me.Id!.Value, now);
        audit.Record(me.Id, DirectoryAuditActions.OrgUpdate, nameof(DirectoryOrg), org.Id.ToString(),
            after: new { org.AddresseeSalutation, org.WebsiteUrl });
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await DetailDtoAsync(db, org, now, ct));
    }

    private static async Task<IResult> Deactivate(
        Guid id, ReasonRequest req, HtnDbContext db, IAuditLog audit, ICurrentUser me, TimeProvider clock, CancellationToken ct)
    {
        var org = await db.DirectoryOrgs.FirstOrDefaultAsync(o => o.Id == id, ct);
        if (org is null) return TypedResults.NotFound();
        if (!org.IsActive) return DirectoryOps.Conflict("This organization is already deactivated.", DirectoryErrorCodes.AlreadyDeactivated);

        org.Deactivate(req.Reason!, me.Id!.Value, clock.GetUtcNow());
        audit.Record(me.Id, DirectoryAuditActions.OrgDeactivate, nameof(DirectoryOrg), org.Id.ToString(), reason: req.Reason);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> UploadLogo(
        Guid id, HttpRequest request, HtnDbContext db, IFileStorage storage, IAuditLog audit, ICurrentUser me,
        TimeProvider clock, CancellationToken ct)
    {
        var org = await db.DirectoryOrgs.FirstOrDefaultAsync(o => o.Id == id && o.DeactivatedAt == null, ct);
        if (org is null) return TypedResults.NotFound();

        var (file, error) = await Uploads.ReadSingleFileAsync(
            request, "file", StoredFilePurposes.MaxBytesFor(StoredFilePurposes.DirectoryLogo), "The logo must be 1 MB or smaller.", ct);
        if (error is not null) return error;
        if (!FileSignatures.IsPng(file!.Bytes) && !FileSignatures.IsJpeg(file.Bytes))
            return Problems.Field("file", "Upload a PNG or JPG image.", "unsupported_type");
        var image = FileSignatures.DetectImage(file.Bytes, allowJpeg: true);
        if (image is null) return Problems.Field("file", "Upload a PNG or JPG image.", "unsupported_type");
        if (image.Value.Width > FileSignatures.MaxImageDimension || image.Value.Height > FileSignatures.MaxImageDimension)
            return Problems.Field("file", $"The logo can be at most {FileSignatures.MaxImageDimension}×{FileSignatures.MaxImageDimension} pixels.", "image_too_large");
        var stripped = FileSignatures.StripMetadata(file.Bytes);
        if (stripped is null) return Problems.Field("file", "Upload a PNG or JPG image.", "unsupported_type");

        var now = clock.GetUtcNow();
        var stored = StoredFile.Create(image.Value.ContentType, stripped.LongLength,
            Convert.ToHexStringLower(SHA256.HashData(stripped)), StoredFilePurposes.DirectoryLogo, file.FileName, now, me.Id);
        await storage.SaveAsync(stored.Id, stripped, ct);
        db.StoredFiles.Add(stored);
        org.SetLogo(stored.Id, me.Id!.Value, now);
        audit.Record(me.Id, DirectoryAuditActions.OrgLogo, nameof(DirectoryOrg), org.Id.ToString(),
            after: new { fileId = stored.Id, stored.SizeBytes });
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await DetailDtoAsync(db, org, now, ct));
    }

    // ---------- Contacts ----------

    private static async Task<IResult> CreateContact(
        Guid id, DirectoryContactRequest req, HtnDbContext db, IAuditLog audit, ICurrentUser me, TimeProvider clock, CancellationToken ct)
    {
        var org = await db.DirectoryOrgs.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id && o.DeactivatedAt == null, ct);
        if (org is null) return TypedResults.NotFound();

        var now = clock.GetUtcNow();
        var contact = DirectoryContact.Create(id,
            new NewDirectoryContact(req.Position!, req.HolderName!, req.Email!, req.Phone, req.IsPrimary!.Value, req.CcEmails),
            me.Id!.Value, now);
        db.DirectoryContacts.Add(contact);
        if (contact.IsPrimary)
            await DirectoryOps.UnsetOtherPrimariesAsync(db, id, contact.Id, me.Id!.Value, now, ct);

        audit.Record(me.Id, DirectoryAuditActions.ContactCreate, nameof(DirectoryContact), contact.Id.ToString(),
            after: new { contact.Position, contact.HolderName, contact.Email });
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await DetailDtoAsync(db, org, now, ct));
    }

    private static async Task<IResult> UpdateContact(
        Guid id, Guid contactId, DirectoryContactRequest req, HtnDbContext db, IAuditLog audit, ICurrentUser me,
        TimeProvider clock, CancellationToken ct)
    {
        var org = await db.DirectoryOrgs.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id, ct);
        if (org is null) return TypedResults.NotFound();
        var contact = await db.DirectoryContacts.FirstOrDefaultAsync(c => c.Id == contactId && c.OrgId == id && c.VoidedAt == null, ct);
        if (contact is null) return TypedResults.NotFound();

        var now = clock.GetUtcNow();
        var before = new { contact.Position, contact.HolderName, contact.Email };
        contact.Update(new NewDirectoryContact(req.Position!, req.HolderName!, req.Email!, req.Phone, req.IsPrimary!.Value, req.CcEmails),
            me.Id!.Value, now);
        if (contact.IsPrimary)
            await DirectoryOps.UnsetOtherPrimariesAsync(db, id, contact.Id, me.Id!.Value, now, ct);

        audit.Record(me.Id, DirectoryAuditActions.ContactUpdate, nameof(DirectoryContact), contact.Id.ToString(),
            before: before, after: new { contact.Position, contact.HolderName, contact.Email });
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await DetailDtoAsync(db, org, now, ct));
    }

    private static async Task<IResult> StillCorrect(
        Guid id, Guid contactId, HtnDbContext db, IAuditLog audit, ICurrentUser me, TimeProvider clock, CancellationToken ct)
    {
        var org = await db.DirectoryOrgs.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id, ct);
        if (org is null) return TypedResults.NotFound();
        var contact = await db.DirectoryContacts.FirstOrDefaultAsync(c => c.Id == contactId && c.OrgId == id && c.VoidedAt == null, ct);
        if (contact is null) return TypedResults.NotFound();

        var now = clock.GetUtcNow();
        contact.StillCorrect(me.Id!.Value, now);
        db.DirectoryContactHistory.Add(DirectoryContactHistory.StillCorrect(id, contact.Id, me.Id!.Value, now));
        audit.Record(me.Id, DirectoryAuditActions.ContactStillCorrect, nameof(DirectoryContact), contact.Id.ToString(),
            after: new { contact.VerifiedAt });
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await DetailDtoAsync(db, org, now, ct));
    }

    private static async Task<IResult> HeadChanged(
        Guid id, Guid contactId, HeadChangedRequest req, HtnDbContext db, IAuditLog audit, ICurrentUser me,
        TimeProvider clock, CancellationToken ct)
    {
        var org = await db.DirectoryOrgs.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id, ct);
        if (org is null) return TypedResults.NotFound();
        var contact = await db.DirectoryContacts.FirstOrDefaultAsync(c => c.Id == contactId && c.OrgId == id && c.VoidedAt == null, ct);
        if (contact is null) return TypedResults.NotFound();

        var now = clock.GetUtcNow();
        var snapshot = contact.HeadChanged(req.Position!, req.HolderName!, req.Email!, req.Phone, me.Id!.Value, now);
        db.DirectoryContactHistory.Add(
            DirectoryContactHistory.HolderChanged(id, contact.Id, snapshot, me.Id!.Value, now));
        audit.Record(me.Id, DirectoryAuditActions.ContactHeadChanged, nameof(DirectoryContact), contact.Id.ToString(),
            before: new { snapshot.PriorPosition, snapshot.PriorHolderName },
            after: new { contact.Position, contact.HolderName, contact.Email });
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await DetailDtoAsync(db, org, now, ct));
    }

    private static async Task<IResult> VoidContact(
        Guid id, Guid contactId, ReasonRequest req, HtnDbContext db, IAuditLog audit, ICurrentUser me,
        TimeProvider clock, CancellationToken ct)
    {
        var org = await db.DirectoryOrgs.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id, ct);
        if (org is null) return TypedResults.NotFound();
        var contact = await db.DirectoryContacts.FirstOrDefaultAsync(c => c.Id == contactId && c.OrgId == id, ct);
        if (contact is null) return TypedResults.NotFound();
        if (contact.IsVoided) return DirectoryOps.Conflict("This contact is already removed.", DirectoryErrorCodes.ContactAlreadyVoided);

        var now = clock.GetUtcNow();
        contact.Void(req.Reason!, me.Id!.Value, now);
        audit.Record(me.Id, DirectoryAuditActions.ContactVoid, nameof(DirectoryContact), contact.Id.ToString(), reason: req.Reason);
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await DetailDtoAsync(db, org, now, ct));
    }

    // ---------- Helpers ----------

    private static async Task<DirectoryOrgDetailDto> DetailDtoAsync(HtnDbContext db, DirectoryOrg org, DateTimeOffset now, CancellationToken ct)
    {
        var contacts = await db.DirectoryContacts.AsNoTracking()
            .Where(c => c.OrgId == org.Id && c.VoidedAt == null).OrderByDescending(c => c.IsPrimary).ThenBy(c => c.Position)
            .ToListAsync(ct);
        var history = await db.DirectoryContactHistory.AsNoTracking()
            .Where(h => h.OrgId == org.Id).OrderByDescending(h => h.At).ToListAsync(ct);
        return new DirectoryOrgDetailDto(
            DirectoryViews.Org(org, contacts, now),
            [.. contacts.Select(c => DirectoryViews.Contact(c, now))],
            [.. history.Select(DirectoryViews.History)]);
    }
}
