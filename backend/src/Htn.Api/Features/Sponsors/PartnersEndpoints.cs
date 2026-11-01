using System.Globalization;
using System.Security.Cryptography;
using Htn.Api.Auth;
using Htn.Api.Common;
using Htn.Domain.Common;
using Htn.Domain.Files;
using Htn.Domain.Sponsors;
using Htn.Infrastructure.Audit;
using Htn.Infrastructure.Data;
using Htn.Infrastructure.Email;
using Htn.Infrastructure.Identity;
using Htn.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Htn.Api.Features.Sponsors;

/// <summary>
/// Frame 13 (Partners tab): organizations that cross the event's registrant threshold, grouped from the agency/company
/// field on registrations and matched by hand (the Directory match arrives in Phase 7). Partner status and counts are
/// derived at read time; only the manual tag, the per-event settings and issued group links are stored. Board-only.
/// </summary>
public static class PartnersEndpoints
{
    /// <summary>The fixed partner perks shown in the aside (configurable storage arrives with the Directory slice).</summary>
    private static readonly string[] PartnerPerks =
    [
        "Logo on the partners wall and website",
        "Certificate of partnership",
        "Shout-out at opening remarks",
    ];

    public static RouteGroupBuilder MapPartners(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/events/{eventId:guid}/partners").WithTags("Sponsors");
        g.MapGet("/", List).RequireAuthorization(Policies.SponsorsManage);
        g.MapPut("/settings", SaveSettings).RequireAuthorization(Policies.SponsorsManage)
            .RequireRateLimiting(SponsorsRateLimiting.Write).Validate<PartnerSettingsRequest>();
        g.MapPost("/tag", Tag).RequireAuthorization(Policies.SponsorsManage)
            .RequireRateLimiting(SponsorsRateLimiting.Write).Validate<TagPartnerRequest>();
        g.MapPost("/{pid:guid}/untag", Untag).RequireAuthorization(Policies.SponsorsManage)
            .RequireRateLimiting(SponsorsRateLimiting.Write).Validate<ReasonRequest>();
        g.MapPut("/{pid:guid}/website", SetWebsite).RequireAuthorization(Policies.SponsorsManage)
            .RequireRateLimiting(SponsorsRateLimiting.Write).Validate<PartnerWebsiteRequest>();
        g.MapPut("/{pid:guid}/logo", UploadLogo).RequireAuthorization(Policies.SponsorsManage)
            .RequireRateLimiting(SponsorsRateLimiting.Write);
        g.MapPost("/{pid:guid}/group-link", CreateLink).RequireAuthorization(Policies.SponsorsManage)
            .RequireRateLimiting(SponsorsRateLimiting.Write);
        g.MapPost("/{pid:guid}/group-link/revoke", RevokeLink).RequireAuthorization(Policies.SponsorsManage)
            .RequireRateLimiting(SponsorsRateLimiting.Write).Validate<ReasonRequest>();
        return g;
    }

    private static async Task<IResult> List(Guid eventId, HtnDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (!await db.Events.AnyAsync(e => e.Id == eventId, ct)) return TypedResults.NotFound();
        var settings = await db.EventPartnerSettings.AsNoTracking().FirstOrDefaultAsync(s => s.EventId == eventId, ct);
        var threshold = settings?.Threshold ?? EventPartnerSettings.DefaultThreshold;
        var basis = settings?.CountBasis ?? PartnerCountBasis.PaidOnly;
        var groupLinkEnabled = settings?.GroupLinkEnabled ?? false;

        var groups = await SponsorsOps.CompanyGroupsAsync(db, eventId, ct);
        var groupByKey = groups.ToDictionary(g => g.CompanyKey);
        var tagged = await db.EventPartners.AsNoTracking().Where(p => p.EventId == eventId && p.UntaggedAt == null).ToListAsync(ct);
        var liveLinks = await db.PartnerGroupLinks.AsNoTracking()
            .Where(l => l.EventId == eventId && l.RevokedAt == null).ToListAsync(ct);
        var linkByPartner = liveLinks.ToDictionary(l => l.EventPartnerId);

        var partners = new List<PartnerDto>();
        var seen = new HashSet<string>();
        foreach (var p in tagged)
        {
            var row = groupByKey.GetValueOrDefault(p.NameKey);
            seen.Add(p.NameKey);
            partners.Add(Partner(p, row, threshold, basis, linkByPartner.ContainsKey(p.Id)));
        }
        // Organizations past the threshold that nobody tagged yet show as auto-partners / "N more to partner".
        foreach (var row in groups.Where(r => !seen.Contains(r.CompanyKey)))
        {
            var count = SponsorsOps.ForBasis(row, basis);
            if (count == 0) continue;
            partners.Add(Derived(row, threshold, basis));
        }
        partners = [.. partners.OrderByDescending(p => p.CountForBasis).ThenBy(p => p.DisplayName)];

        // The review queue: company groups with 2+ registrants and no partner row yet (the Phase 7 Directory match).
        var unmatched = groups
            .Where(r => !seen.Contains(r.CompanyKey) && r.AllRegistrants >= 2 && SponsorsOps.ForBasis(r, basis) < threshold)
            .OrderByDescending(r => r.AllRegistrants)
            .Select(r => new UnmatchedOrgDto(r.CompanyKey, r.SampleName, r.AllRegistrants, r.Paid))
            .ToList();

        return TypedResults.Ok(new PartnersPageDto(
            new PartnerSettingsDto(threshold, basis, groupLinkEnabled), partners, unmatched, PartnerPerks));
    }

    private static async Task<IResult> SaveSettings(
        Guid eventId, PartnerSettingsRequest req, HtnDbContext db, IAuditLog audit, ICurrentUser me,
        TimeProvider clock, CancellationToken ct)
    {
        if (!await db.Events.AnyAsync(e => e.Id == eventId, ct)) return TypedResults.NotFound();
        var now = clock.GetUtcNow();
        var settings = await SponsorsOps.SettingsAsync(db, eventId, now, ct);
        settings.Update(req.Threshold!.Value, req.CountBasis!.Value, req.GroupLinkEnabled!.Value, me.Id!.Value, now);
        audit.Record(me.Id, SponsorAuditActions.PartnerSettings, nameof(EventPartnerSettings), settings.Id.ToString(),
            after: new { settings.Threshold, basis = settings.CountBasis.ToString(), settings.GroupLinkEnabled });
        await db.SaveChangesAsync(ct);
        return await List(eventId, db, clock, ct);
    }

    private static async Task<IResult> Tag(
        Guid eventId, TagPartnerRequest req, HtnDbContext db, IAuditLog audit, ICurrentUser me,
        TimeProvider clock, CancellationToken ct)
    {
        if (!await db.Events.AnyAsync(e => e.Id == eventId, ct)) return TypedResults.NotFound();
        var now = clock.GetUtcNow();
        var partner = EventPartner.Tag(eventId, req.Name!, req.OrgType, me.Id!.Value, now);

        var existing = await db.EventPartners.FirstOrDefaultAsync(p => p.EventId == eventId && p.NameKey == partner.NameKey, ct);
        if (existing is not null)
        {
            if (existing.IsTagged)
                return SponsorsOps.Conflict("That organization is already a partner.", SponsorErrorCodes.AlreadyTagged);
            // A previously untagged org cannot be re-tagged in place (the row is frozen); surface a clear message.
            return SponsorsOps.Conflict("That organization was a partner before and was untagged.", SponsorErrorCodes.AlreadyTagged);
        }

        db.EventPartners.Add(partner);
        audit.Record(me.Id, SponsorAuditActions.PartnerTag, nameof(EventPartner), partner.Id.ToString(),
            after: new { partner.DisplayName, partner.OrgType });
        await db.SaveChangesAsync(ct);
        return await List(eventId, db, clock, ct);
    }

    private static async Task<IResult> Untag(
        Guid eventId, Guid pid, ReasonRequest req, HtnDbContext db, IAuditLog audit, ICurrentUser me,
        TimeProvider clock, CancellationToken ct)
    {
        var partner = await db.EventPartners.FirstOrDefaultAsync(p => p.Id == pid && p.EventId == eventId, ct);
        if (partner is null) return TypedResults.NotFound();
        if (!partner.IsTagged) return TypedResults.NoContent();
        // A live group link must go first, so the index never carries an orphan.
        var link = await db.PartnerGroupLinks.FirstOrDefaultAsync(l => l.EventPartnerId == pid && l.RevokedAt == null, ct);
        if (link is not null)
        {
            link.Revoke("Partner untagged", me.Id!.Value, clock.GetUtcNow());
            audit.Record(me.Id, SponsorAuditActions.GroupLinkRevoke, nameof(PartnerGroupLink), link.Id.ToString(),
                reason: "Partner untagged");
        }
        partner.Untag(req.Reason!, clock.GetUtcNow());
        audit.Record(me.Id, SponsorAuditActions.PartnerUntag, nameof(EventPartner), partner.Id.ToString(), reason: req.Reason);
        await db.SaveChangesAsync(ct);
        return await List(eventId, db, clock, ct);
    }

    private static async Task<IResult> SetWebsite(
        Guid eventId, Guid pid, PartnerWebsiteRequest req, HtnDbContext db, IAuditLog audit, ICurrentUser me,
        TimeProvider clock, CancellationToken ct)
    {
        var partner = await db.EventPartners.FirstOrDefaultAsync(p => p.Id == pid && p.EventId == eventId && p.UntaggedAt == null, ct);
        if (partner is null) return TypedResults.NotFound();
        partner.SetWebsite(req.WebsiteUrl);
        audit.Record(me.Id, SponsorAuditActions.PartnerBrand, nameof(EventPartner), partner.Id.ToString(),
            after: new { partner.WebsiteUrl });
        await db.SaveChangesAsync(ct);
        return await List(eventId, db, clock, ct);
    }

    private static async Task<IResult> UploadLogo(
        Guid eventId, Guid pid, HttpRequest request, HtnDbContext db, IFileStorage storage, IAuditLog audit,
        ICurrentUser me, TimeProvider clock, CancellationToken ct)
    {
        var partner = await db.EventPartners.FirstOrDefaultAsync(p => p.Id == pid && p.EventId == eventId && p.UntaggedAt == null, ct);
        if (partner is null) return TypedResults.NotFound();

        var (file, error) = await Uploads.ReadSingleFileAsync(
            request, "file", StoredFilePurposes.SponsorLogoMaxBytes, "The logo must be 1 MB or smaller.", ct);
        if (error is not null) return error;
        if (!FileSignatures.IsPng(file!.Bytes)) return Problems.Field("file", "Upload a PNG image.", SponsorErrorCodes.UnsupportedType);
        var (width, height) = FileSignatures.PngSize(file.Bytes);
        if (width == 0 || height == 0) return Problems.Field("file", "Upload a PNG image.", SponsorErrorCodes.UnsupportedType);
        if (width > FileSignatures.MaxImageDimension || height > FileSignatures.MaxImageDimension)
            return Problems.Field("file", $"The logo can be at most {FileSignatures.MaxImageDimension}×{FileSignatures.MaxImageDimension} pixels.", "image_too_large");
        var stripped = FileSignatures.StripMetadata(file.Bytes);
        if (stripped is null) return Problems.Field("file", "Upload a PNG image.", SponsorErrorCodes.UnsupportedType);

        var now = clock.GetUtcNow();
        var stored = StoredFile.Create(FileSignatures.PngContentType, stripped.LongLength,
            Convert.ToHexStringLower(SHA256.HashData(stripped)), StoredFilePurposes.SponsorLogo, file.FileName, now, me.Id);
        await storage.SaveAsync(stored.Id, stripped, ct);
        db.StoredFiles.Add(stored);
        partner.SetLogo(stored.Id);
        audit.Record(me.Id, SponsorAuditActions.PartnerBrand, nameof(EventPartner), partner.Id.ToString(),
            after: new { fileId = stored.Id });
        await db.SaveChangesAsync(ct);
        return await List(eventId, db, clock, ct);
    }

    private static async Task<IResult> CreateLink(
        Guid eventId, Guid pid, HtnDbContext db, IPublicIdGenerator ids, IOptions<AppLinkOptions> links,
        IAuditLog audit, ICurrentUser me, TimeProvider clock, CancellationToken ct)
    {
        var partner = await db.EventPartners.FirstOrDefaultAsync(p => p.Id == pid && p.EventId == eventId && p.UntaggedAt == null, ct);
        if (partner is null) return TypedResults.NotFound();
        var slug = await db.Events.AsNoTracking().Where(e => e.Id == eventId).Select(e => e.Slug).FirstOrDefaultAsync(ct);
        if (slug is null) return TypedResults.NotFound();

        var settings = await db.EventPartnerSettings.AsNoTracking().FirstOrDefaultAsync(s => s.EventId == eventId, ct);
        var threshold = settings?.Threshold ?? EventPartnerSettings.DefaultThreshold;
        var basis = settings?.CountBasis ?? PartnerCountBasis.PaidOnly;
        if (settings is null || !settings.GroupLinkEnabled)
            return SponsorsOps.Conflict("Turn on the group registration link for this event first.", SponsorErrorCodes.ThresholdNotReached);
        var groups = await SponsorsOps.CompanyGroupsAsync(db, eventId, ct);
        var count = groups.FirstOrDefault(r => r.CompanyKey == partner.NameKey) is { } row ? SponsorsOps.ForBasis(row, basis) : 0;
        if (count < threshold)
            return SponsorsOps.Conflict("This partner hasn't reached the threshold yet.", SponsorErrorCodes.ThresholdNotReached);

        if (await db.PartnerGroupLinks.AnyAsync(l => l.EventPartnerId == pid && l.RevokedAt == null, ct))
            return SponsorsOps.Conflict("This partner already has a live link.", SponsorErrorCodes.AlreadyTagged);

        var raw = ids.NewToken(32);
        var link = PartnerGroupLink.Create(eventId, pid, AccountTokenService.Hash(raw), me.Id!.Value, clock.GetUtcNow());
        db.PartnerGroupLinks.Add(link);
        audit.Record(me.Id, SponsorAuditActions.GroupLinkCreate, nameof(PartnerGroupLink), link.Id.ToString(),
            after: new { partnerId = pid });
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(new GroupLinkDto(links.Value.PartnerRegister(slug, raw)));
    }

    private static async Task<IResult> RevokeLink(
        Guid eventId, Guid pid, ReasonRequest req, HtnDbContext db, IAuditLog audit, ICurrentUser me,
        TimeProvider clock, CancellationToken ct)
    {
        var link = await db.PartnerGroupLinks.FirstOrDefaultAsync(l => l.EventPartnerId == pid && l.EventId == eventId && l.RevokedAt == null, ct);
        if (link is null) return TypedResults.NotFound();
        link.Revoke(req.Reason!, me.Id!.Value, clock.GetUtcNow());
        audit.Record(me.Id, SponsorAuditActions.GroupLinkRevoke, nameof(PartnerGroupLink), link.Id.ToString(), reason: req.Reason);
        await db.SaveChangesAsync(ct);
        return await List(eventId, db, clock, ct);
    }

    // ---------- Helpers ----------

    private static PartnerDto Partner(
        EventPartner p, PartnerGroupRow? row, int threshold, PartnerCountBasis basis, bool hasLink)
    {
        var all = row?.AllRegistrants ?? 0;
        var paid = row?.Paid ?? 0;
        var checkedIn = row?.CheckedIn ?? 0;
        var count = row is null ? 0 : SponsorsOps.ForBasis(row, basis);
        var reached = count >= threshold;
        // A tagged org is a partner whether or not it has reached the threshold; the count still shows progress.
        var status = reached ? "Partner" : "Partner · tagged";
        var assets = p.LogoFileId is not null && p.WebsiteUrl is not null ? "Logo & website ✓" : "Needs logo";
        return new PartnerDto(
            p.Id, p.NameKey, p.DisplayName, p.OrgType, all, paid, checkedIn, count,
            status, Math.Max(0, threshold - count), true, true, assets, p.LogoFileId, p.WebsiteUrl,
            hasLink, count);
    }

    private static PartnerDto Derived(PartnerGroupRow row, int threshold, PartnerCountBasis basis)
    {
        var count = SponsorsOps.ForBasis(row, basis);
        var reached = count >= threshold;
        var remaining = Math.Max(0, threshold - count);
        var status = reached ? "Partner" : remaining <= 5 ? $"{remaining} more to partner" : "Not yet";
        return new PartnerDto(
            null, row.CompanyKey, row.SampleName, null, row.AllRegistrants, row.Paid, row.CheckedIn, count,
            status, remaining, reached, false, "—", null, null, false, count);
    }
}
