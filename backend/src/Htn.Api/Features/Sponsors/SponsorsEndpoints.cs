using System.Globalization;
using System.Security.Cryptography;
using Htn.Api.Auth;
using Htn.Api.Common;
using Htn.Api.Features.Finance;
using Htn.Domain.Files;
using Htn.Domain.Registrations;
using Htn.Domain.Sponsors;
using Htn.Infrastructure.Audit;
using Htn.Infrastructure.Data;
using Htn.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

namespace Htn.Api.Features.Sponsors;

/// <summary>
/// Frame 13 (Sponsors tab): the event's sponsorship tiers and the sponsors signed to them. Board-only
/// (<c>Sponsors.Manage</c>). A sponsor's contribution is cash (decimal PHP), in-kind, or both; recording the cash
/// received auto-posts it to the event fund under <c>Sponsorship</c> via <see cref="AutoPost"/>, in the same
/// transaction. Sponsors and tiers are append-only: confirmed, paid, voided or deactivated, never deleted.
/// </summary>
public static class SponsorsEndpoints
{
    public static RouteGroupBuilder MapSponsors(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/events/{eventId:guid}/sponsors").WithTags("Sponsors");

        g.MapGet("/", List).RequireAuthorization(Policies.SponsorsManage);
        g.MapGet("/{sid:guid}", Detail).RequireAuthorization(Policies.SponsorsManage);
        g.MapPost("/", Create).RequireAuthorization(Policies.SponsorsManage)
            .RequireRateLimiting(SponsorsRateLimiting.Write).Validate<SponsorRequest>();
        g.MapPut("/{sid:guid}", Update).RequireAuthorization(Policies.SponsorsManage)
            .RequireRateLimiting(SponsorsRateLimiting.Write).Validate<SponsorRequest>();
        g.MapPost("/{sid:guid}/confirm", Confirm).RequireAuthorization(Policies.SponsorsManage)
            .RequireRateLimiting(SponsorsRateLimiting.Write);
        g.MapPost("/{sid:guid}/unconfirm", Unconfirm).RequireAuthorization(Policies.SponsorsManage)
            .RequireRateLimiting(SponsorsRateLimiting.Write).Validate<ReasonRequest>();
        g.MapPost("/{sid:guid}/cash-received", CashReceived).RequireAuthorization(Policies.SponsorsManage)
            .RequireRateLimiting(SponsorsRateLimiting.Write).Validate<CashReceivedRequest>();
        g.MapPost("/{sid:guid}/cash-received/reverse", ReverseCash).RequireAuthorization(Policies.SponsorsManage)
            .RequireRateLimiting(SponsorsRateLimiting.Write).Validate<ReasonRequest>();
        g.MapPost("/{sid:guid}/void", Void).RequireAuthorization(Policies.SponsorsManage)
            .RequireRateLimiting(SponsorsRateLimiting.Write).Validate<ReasonRequest>();
        g.MapPut("/{sid:guid}/logo", UploadLogo).RequireAuthorization(Policies.SponsorsManage)
            .RequireRateLimiting(SponsorsRateLimiting.Write);
        g.MapPost("/{sid:guid}/perks", AddPerk).RequireAuthorization(Policies.SponsorsManage)
            .RequireRateLimiting(SponsorsRateLimiting.Write).Validate<SponsorPerkRequest>();
        g.MapPost("/{sid:guid}/perks/{pid:guid}/delivered", SetPerkDelivered).RequireAuthorization(Policies.SponsorsManage)
            .RequireRateLimiting(SponsorsRateLimiting.Write).Validate<PerkDeliveredRequest>();
        g.MapPost("/{sid:guid}/perks/{pid:guid}/remove", RemovePerk).RequireAuthorization(Policies.SponsorsManage)
            .RequireRateLimiting(SponsorsRateLimiting.Write).Validate<ReasonRequest>();
        return g;
    }

    public static RouteGroupBuilder MapSponsorTiers(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/events/{eventId:guid}/sponsor-tiers").WithTags("Sponsors");
        g.MapPost("/", CreateTier).RequireAuthorization(Policies.SponsorsManage)
            .RequireRateLimiting(SponsorsRateLimiting.Write).Validate<SponsorTierRequest>();
        g.MapPut("/{tid:guid}", UpdateTier).RequireAuthorization(Policies.SponsorsManage)
            .RequireRateLimiting(SponsorsRateLimiting.Write).Validate<SponsorTierRequest>();
        g.MapPost("/{tid:guid}/deactivate", DeactivateTier).RequireAuthorization(Policies.SponsorsManage)
            .RequireRateLimiting(SponsorsRateLimiting.Write);
        return g;
    }

    // ---------- Sponsors ----------

    private static async Task<IResult> List(Guid eventId, HtnDbContext db, CancellationToken ct)
    {
        var ev = await EventNameAsync(db, eventId, ct);
        if (ev is null) return TypedResults.NotFound();
        return TypedResults.Ok(await PageAsync(db, eventId, ev, ct));
    }

    private static async Task<IResult> Detail(Guid eventId, Guid sid, HtnDbContext db, CancellationToken ct)
    {
        var sponsor = await db.Sponsors.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == sid && s.EventId == eventId && s.VoidedAt == null, ct);
        if (sponsor is null) return TypedResults.NotFound();
        return TypedResults.Ok(await DetailDtoAsync(db, sponsor, ct));
    }

    private static async Task<IResult> Create(
        Guid eventId, SponsorRequest req, HtnDbContext db, IAuditLog audit, ICurrentUser me, TimeProvider clock, CancellationToken ct)
    {
        if (!await db.Events.AnyAsync(e => e.Id == eventId, ct)) return TypedResults.NotFound();
        if (req.TierId is { } tierId && tierId != Guid.Empty
            && !await db.SponsorTiers.AnyAsync(t => t.Id == tierId && t.EventId == eventId, ct))
            return Problems.Field("tierId", "That tier no longer exists.", SponsorErrorCodes.UnknownTier);

        var now = clock.GetUtcNow();
        var sponsor = Sponsor.Create(eventId, ToNew(req), me.Id!.Value, now);
        if (await DuplicateNameAsync(db, eventId, sponsor.NameKey, null, ct))
            return SponsorsOps.Conflict("A sponsor with that name already exists for this event.", SponsorErrorCodes.DuplicateName);

        db.Sponsors.Add(sponsor);
        audit.Record(me.Id, SponsorAuditActions.SponsorCreate, nameof(Sponsor), sponsor.Id.ToString(),
            after: new { sponsor.Name, kind = sponsor.Kind.ToString(), sponsor.CashAmount });
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await DetailDtoAsync(db, sponsor, ct));
    }

    private static async Task<IResult> Update(
        Guid eventId, Guid sid, SponsorRequest req, HtnDbContext db, IAuditLog audit, ICurrentUser me,
        TimeProvider clock, CancellationToken ct)
    {
        var sponsor = await LiveSponsorAsync(db, eventId, sid, ct);
        if (sponsor is null) return TypedResults.NotFound();
        if (req.TierId is { } tierId && tierId != Guid.Empty
            && !await db.SponsorTiers.AnyAsync(t => t.Id == tierId && t.EventId == eventId, ct))
            return Problems.Field("tierId", "That tier no longer exists.", SponsorErrorCodes.UnknownTier);

        var now = clock.GetUtcNow();
        try
        {
            sponsor.Update(ToNew(req), me.Id!.Value, now);
        }
        catch (Htn.Domain.Common.DomainException ex)
        {
            return Problems.Field("cashAmount", ex.Message, SponsorErrorCodes.CashPosted);
        }
        if (await DuplicateNameAsync(db, eventId, sponsor.NameKey, sid, ct))
            return SponsorsOps.Conflict("A sponsor with that name already exists for this event.", SponsorErrorCodes.DuplicateName);

        audit.Record(me.Id, SponsorAuditActions.SponsorUpdate, nameof(Sponsor), sponsor.Id.ToString(),
            after: new { sponsor.Name, sponsor.CashAmount, sponsor.InKindDescription });
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await DetailDtoAsync(db, sponsor, ct));
    }

    private static async Task<IResult> Confirm(
        Guid eventId, Guid sid, HtnDbContext db, IAuditLog audit, ICurrentUser me, TimeProvider clock, CancellationToken ct)
    {
        var sponsor = await LiveSponsorAsync(db, eventId, sid, ct);
        if (sponsor is null) return TypedResults.NotFound();
        if (sponsor.IsConfirmed) return await DetailOkAsync(db, sponsor, ct);

        var now = clock.GetUtcNow();
        sponsor.Confirm(me.Id!.Value, now);
        // Snapshot the tier's perks onto the sponsor, so editing the tier later never rewrites a signed deal.
        if (sponsor.TierId is { } tierId
            && await db.SponsorTiers.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tierId, ct) is { } tier
            && !await db.SponsorPerks.AnyAsync(p => p.SponsorId == sponsor.Id, ct))
        {
            var order = 0;
            foreach (var text in tier.Perks)
                db.SponsorPerks.Add(SponsorPerk.Create(sponsor.Id, eventId, tier.Id, text, order++, now));
        }
        audit.Record(me.Id, SponsorAuditActions.SponsorConfirm, nameof(Sponsor), sponsor.Id.ToString());
        await db.SaveChangesAsync(ct);
        return await DetailOkAsync(db, sponsor, ct);
    }

    private static async Task<IResult> Unconfirm(
        Guid eventId, Guid sid, ReasonRequest req, HtnDbContext db, IAuditLog audit, ICurrentUser me,
        TimeProvider clock, CancellationToken ct)
    {
        var sponsor = await LiveSponsorAsync(db, eventId, sid, ct);
        if (sponsor is null) return TypedResults.NotFound();
        if (sponsor.CashPosted)
            return SponsorsOps.Conflict("This sponsor's cash is in the ledger. Reverse the receipt first.", SponsorErrorCodes.CashPosted);
        if (!sponsor.IsConfirmed)
            return SponsorsOps.Conflict("This sponsor is not confirmed.", SponsorErrorCodes.NotConfirmed);

        sponsor.Unconfirm(me.Id!.Value, clock.GetUtcNow());
        audit.Record(me.Id, SponsorAuditActions.SponsorUnconfirm, nameof(Sponsor), sponsor.Id.ToString(), reason: req.Reason);
        await db.SaveChangesAsync(ct);
        return await DetailOkAsync(db, sponsor, ct);
    }

    private static async Task<IResult> CashReceived(
        Guid eventId, Guid sid, CashReceivedRequest req, HtnDbContext db, AutoPost autoPost, IAuditLog audit,
        ICurrentUser me, TimeProvider clock, CancellationToken ct)
    {
        var sponsor = await LiveSponsorAsync(db, eventId, sid, ct);
        if (sponsor is null) return TypedResults.NotFound();
        if (sponsor.CashAmount is null)
            return SponsorsOps.Conflict("This sponsor has no cash contribution.", SponsorErrorCodes.NoCash);
        if (!sponsor.IsConfirmed)
            return SponsorsOps.Conflict("Confirm the sponsor before recording its cash.", SponsorErrorCodes.NotConfirmed);
        if (sponsor.CashPosted)
            return SponsorsOps.Conflict("This sponsor's cash was already recorded.", SponsorErrorCodes.AlreadyReceived);

        var account = await db.PaymentAccounts.FirstOrDefaultAsync(a => a.Id == req.PaymentAccountId, ct);
        if (account is null)
            return Problems.Field("paymentAccountId", "That account no longer exists.", SponsorErrorCodes.UnknownAccount);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await FinanceOps.LockPeriodsAsync(db, ct);
        // A contra from an earlier closed-month reversal leaves the ledger slot occupied: that correction belongs in finance.
        if (await autoPost.HasLiveEntryAsync(Htn.Domain.Finance.LedgerSource.SponsorPayment, sponsor.Id, ct))
            return SponsorsOps.Conflict(
                "This sponsor's earlier cash sits in a closed month. Book the correction from Finance.",
                SponsorErrorCodes.PeriodClosedUseFinance);

        var now = clock.GetUtcNow();
        sponsor.MarkCashReceived(account.Id, req.Reference, me.Id!.Value, now);
        var entry = await autoPost.SponsorCashAsync(sponsor, account.Id, req.ReceivedOn!.Value, req.Reference, me.Id!.Value, now, ct);
        audit.Record(me.Id, SponsorAuditActions.SponsorCashReceived, nameof(Sponsor), sponsor.Id.ToString(),
            after: new { sponsor.CashAmount, accountId = account.Id, entryId = entry?.Id });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await DetailOkAsync(db, sponsor, ct);
    }

    private static async Task<IResult> ReverseCash(
        Guid eventId, Guid sid, ReasonRequest req, HtnDbContext db, AutoPost autoPost, IAuditLog audit,
        ICurrentUser me, TimeProvider clock, CancellationToken ct)
    {
        var sponsor = await LiveSponsorAsync(db, eventId, sid, ct);
        if (sponsor is null) return TypedResults.NotFound();
        if (!sponsor.CashPosted)
            return SponsorsOps.Conflict("This sponsor's cash was not recorded as received.", SponsorErrorCodes.NotReceived);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await FinanceOps.LockPeriodsAsync(db, ct);
        var now = clock.GetUtcNow();
        var reversal = await autoPost.ReverseSponsorCashAsync(sponsor, req.Reason!, me.Id!.Value, now, ct);
        sponsor.ClearCashReceipt(me.Id!.Value, now);
        audit.Record(me.Id, SponsorAuditActions.SponsorCashReversed, nameof(Sponsor), sponsor.Id.ToString(),
            after: new { voided = reversal?.Voided, entryId = reversal?.Entry.Id }, reason: req.Reason);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await DetailOkAsync(db, sponsor, ct);
    }

    private static async Task<IResult> Void(
        Guid eventId, Guid sid, ReasonRequest req, HtnDbContext db, IAuditLog audit, ICurrentUser me,
        TimeProvider clock, CancellationToken ct)
    {
        var sponsor = await LiveSponsorAsync(db, eventId, sid, ct);
        if (sponsor is null) return TypedResults.NotFound();
        if (sponsor.CashPosted)
            return SponsorsOps.Conflict("This sponsor's cash is in the ledger. Reverse the receipt before removing it.", SponsorErrorCodes.CashPosted);

        sponsor.Void(req.Reason!, me.Id!.Value, clock.GetUtcNow());
        audit.Record(me.Id, SponsorAuditActions.SponsorVoid, nameof(Sponsor), sponsor.Id.ToString(), reason: req.Reason);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> UploadLogo(
        Guid eventId, Guid sid, HttpRequest request, HtnDbContext db, IFileStorage storage, IAuditLog audit,
        ICurrentUser me, TimeProvider clock, CancellationToken ct)
    {
        var sponsor = await LiveSponsorAsync(db, eventId, sid, ct);
        if (sponsor is null) return TypedResults.NotFound();

        var (file, error) = await Uploads.ReadSingleFileAsync(
            request, "file", StoredFilePurposes.SponsorLogoMaxBytes, "The logo must be 1 MB or smaller.", ct);
        if (error is not null) return error;
        // PNG only in Phase 6: an uploaded SVG would be executable XML served inline.
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
        sponsor.SetLogo(stored.Id, me.Id!.Value, now);
        audit.Record(me.Id, SponsorAuditActions.SponsorLogo, nameof(Sponsor), sponsor.Id.ToString(),
            after: new { fileId = stored.Id, stored.SizeBytes });
        await db.SaveChangesAsync(ct);
        return await DetailOkAsync(db, sponsor, ct);
    }

    // ---------- Perks ----------

    private static async Task<IResult> AddPerk(
        Guid eventId, Guid sid, SponsorPerkRequest req, HtnDbContext db, IAuditLog audit, ICurrentUser me,
        TimeProvider clock, CancellationToken ct)
    {
        var sponsor = await LiveSponsorAsync(db, eventId, sid, ct);
        if (sponsor is null) return TypedResults.NotFound();
        var now = clock.GetUtcNow();
        var order = await db.SponsorPerks.Where(p => p.SponsorId == sid).CountAsync(ct);
        var perk = SponsorPerk.Create(sid, eventId, null, req.Text!, order, now);
        db.SponsorPerks.Add(perk);
        audit.Record(me.Id, SponsorAuditActions.PerkSave, nameof(SponsorPerk), perk.Id.ToString(), after: new { perk.Text });
        await db.SaveChangesAsync(ct);
        return await DetailOkAsync(db, sponsor, ct);
    }

    private static async Task<IResult> SetPerkDelivered(
        Guid eventId, Guid sid, Guid pid, PerkDeliveredRequest req, HtnDbContext db, IAuditLog audit, ICurrentUser me,
        TimeProvider clock, CancellationToken ct)
    {
        var sponsor = await LiveSponsorAsync(db, eventId, sid, ct);
        if (sponsor is null) return TypedResults.NotFound();
        var perk = await db.SponsorPerks.FirstOrDefaultAsync(p => p.Id == pid && p.SponsorId == sid, ct);
        if (perk is null) return TypedResults.NotFound();
        perk.SetDelivered(req.Delivered!.Value, me.Id!.Value, clock.GetUtcNow());
        audit.Record(me.Id, SponsorAuditActions.PerkDeliver, nameof(SponsorPerk), perk.Id.ToString(),
            after: new { delivered = perk.IsDelivered });
        await db.SaveChangesAsync(ct);
        return await DetailOkAsync(db, sponsor, ct);
    }

    private static async Task<IResult> RemovePerk(
        Guid eventId, Guid sid, Guid pid, ReasonRequest req, HtnDbContext db, IAuditLog audit, ICurrentUser me,
        TimeProvider clock, CancellationToken ct)
    {
        var sponsor = await LiveSponsorAsync(db, eventId, sid, ct);
        if (sponsor is null) return TypedResults.NotFound();
        var perk = await db.SponsorPerks.FirstOrDefaultAsync(p => p.Id == pid && p.SponsorId == sid, ct);
        if (perk is null) return TypedResults.NotFound();
        perk.Remove(req.Reason!, clock.GetUtcNow());
        audit.Record(me.Id, SponsorAuditActions.PerkRemove, nameof(SponsorPerk), perk.Id.ToString(), reason: req.Reason);
        await db.SaveChangesAsync(ct);
        return await DetailOkAsync(db, sponsor, ct);
    }

    // ---------- Tiers ----------

    private static async Task<IResult> CreateTier(
        Guid eventId, SponsorTierRequest req, HtnDbContext db, IAuditLog audit, ICurrentUser me, TimeProvider clock, CancellationToken ct)
    {
        if (!await db.Events.AnyAsync(e => e.Id == eventId, ct)) return TypedResults.NotFound();
        var now = clock.GetUtcNow();
        var tier = SponsorTier.Create(eventId, ToNewTier(req), me.Id!.Value, now);
        if (await db.SponsorTiers.AnyAsync(t => t.EventId == eventId && t.DeactivatedAt == null && t.Name.ToLower() == tier.Name.ToLower(), ct))
            return SponsorsOps.Conflict("A tier with that name already exists.", SponsorErrorCodes.DuplicateName);
        db.SponsorTiers.Add(tier);
        audit.Record(me.Id, SponsorAuditActions.TierSave, nameof(SponsorTier), tier.Id.ToString(),
            after: new { tier.Name, tier.Class });
        await db.SaveChangesAsync(ct);
        var ev = await EventNameAsync(db, eventId, ct);
        return TypedResults.Ok(await PageAsync(db, eventId, ev!, ct));
    }

    private static async Task<IResult> UpdateTier(
        Guid eventId, Guid tid, SponsorTierRequest req, HtnDbContext db, IAuditLog audit, ICurrentUser me,
        TimeProvider clock, CancellationToken ct)
    {
        var tier = await db.SponsorTiers.FirstOrDefaultAsync(t => t.Id == tid && t.EventId == eventId, ct);
        if (tier is null) return TypedResults.NotFound();
        tier.Update(ToNewTier(req));
        audit.Record(me.Id, SponsorAuditActions.TierSave, nameof(SponsorTier), tier.Id.ToString(),
            after: new { tier.Name, tier.Class });
        await db.SaveChangesAsync(ct);
        var ev = await EventNameAsync(db, eventId, ct);
        return TypedResults.Ok(await PageAsync(db, eventId, ev!, ct));
    }

    private static async Task<IResult> DeactivateTier(
        Guid eventId, Guid tid, HtnDbContext db, IAuditLog audit, ICurrentUser me, TimeProvider clock, CancellationToken ct)
    {
        var tier = await db.SponsorTiers.FirstOrDefaultAsync(t => t.Id == tid && t.EventId == eventId, ct);
        if (tier is null) return TypedResults.NotFound();
        if (!tier.IsActive) return TypedResults.NoContent();
        tier.Deactivate(clock.GetUtcNow());
        audit.Record(me.Id, SponsorAuditActions.TierDeactivate, nameof(SponsorTier), tier.Id.ToString());
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    // ---------- Helpers ----------

    private static NewSponsor ToNew(SponsorRequest r) => new(
        r.Kind!.Value, r.Name!, r.DisplayName, r.IsAnonymous ?? false,
        r.ContactName, r.ContactRole, r.ContactEmail, r.ContactMobile,
        r.TierId, r.CashAmount, r.InKindDescription, r.InKindItemCount, r.CompedSeats ?? 0, r.WebsiteUrl);

    private static NewSponsorTier ToNewTier(SponsorTierRequest r) => new(
        r.Class!.Value, r.Name!, r.CashAmount, r.PriceNote, r.InKindText, r.InKindItemCount,
        r.Slots ?? 1, r.CompedSeats ?? 0, r.SortOrder ?? 0, r.Perks ?? []);

    private static Task<Sponsor?> LiveSponsorAsync(HtnDbContext db, Guid eventId, Guid sid, CancellationToken ct) =>
        db.Sponsors.FirstOrDefaultAsync(s => s.Id == sid && s.EventId == eventId && s.VoidedAt == null, ct);

    private static Task<string?> EventNameAsync(HtnDbContext db, Guid eventId, CancellationToken ct) =>
        db.Events.AsNoTracking().Where(e => e.Id == eventId).Select(e => e.Name).FirstOrDefaultAsync(ct);

    private static Task<bool> DuplicateNameAsync(HtnDbContext db, Guid eventId, string nameKey, Guid? exceptId, CancellationToken ct) =>
        db.Sponsors.AnyAsync(s => s.EventId == eventId && s.VoidedAt == null && s.NameKey == nameKey
                                  && (exceptId == null || s.Id != exceptId), ct);

    private static async Task<IResult> DetailOkAsync(HtnDbContext db, Sponsor sponsor, CancellationToken ct) =>
        TypedResults.Ok(await DetailDtoAsync(db, sponsor, ct));

    private static async Task<SponsorDetailDto> DetailDtoAsync(HtnDbContext db, Sponsor sponsor, CancellationToken ct)
    {
        var tier = sponsor.TierId is { } tid
            ? await db.SponsorTiers.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tid, ct) : null;
        var perks = await db.SponsorPerks.AsNoTracking().Where(p => p.SponsorId == sponsor.Id)
            .OrderBy(p => p.SortOrder).ToListAsync(ct);
        var live = perks.Where(p => !p.IsRemoved).ToList();
        var compsUsed = await CompsUsedAsync(db, sponsor.EventId, sponsor.Id, ct);
        var dto = SponsorsViews.Sponsor(sponsor, tier?.Class, tier?.Name, compsUsed,
            live.Count(p => p.IsDelivered), live.Count);
        return new SponsorDetailDto(dto, [.. perks.Select(SponsorsViews.Perk)]);
    }

    private static Task<int> CompsUsedAsync(HtnDbContext db, Guid eventId, Guid sponsorId, CancellationToken ct) =>
        (from c in db.SponsorCompRegistrations.AsNoTracking()
         join r in db.Registrations.AsNoTracking() on c.RegistrationId equals r.Id
         where c.SponsorId == sponsorId && r.Status != RegistrationStatus.Cancelled
         select c.Id).CountAsync(ct);

    private static async Task<SponsorsPageDto> PageAsync(HtnDbContext db, Guid eventId, string eventName, CancellationToken ct)
    {
        var tiers = await db.SponsorTiers.AsNoTracking().Where(t => t.EventId == eventId)
            .OrderBy(t => t.SortOrder).ThenBy(t => t.Name).ToListAsync(ct);
        var sponsors = await db.Sponsors.AsNoTracking().Where(s => s.EventId == eventId && s.VoidedAt == null)
            .OrderByDescending(s => s.CreatedAt).ToListAsync(ct);

        var tierById = tiers.ToDictionary(t => t.Id);
        var slotsTaken = sponsors.Where(s => s.TierId is not null)
            .GroupBy(s => s.TierId!.Value).ToDictionary(g => g.Key, g => g.Count());

        var perks = await db.SponsorPerks.AsNoTracking()
            .Where(p => p.EventId == eventId && p.RemovedAt == null)
            .Select(p => new { p.SponsorId, p.DeliveredAt })
            .ToListAsync(ct);
        var perksBySponsor = perks.GroupBy(p => p.SponsorId)
            .ToDictionary(g => g.Key, g => (Total: g.Count(), Delivered: g.Count(p => p.DeliveredAt != null)));

        var comps = await (
            from c in db.SponsorCompRegistrations.AsNoTracking()
            join r in db.Registrations.AsNoTracking() on c.RegistrationId equals r.Id
            where c.EventId == eventId && r.Status != RegistrationStatus.Cancelled
            group c by c.SponsorId into g
            select new { SponsorId = g.Key, Used = g.Count() }).ToListAsync(ct);
        var compsBySponsor = comps.ToDictionary(x => x.SponsorId, x => x.Used);

        var tierDtos = tiers.Select(t => SponsorsViews.Tier(t, slotsTaken.GetValueOrDefault(t.Id), t.Perks)).ToList();
        var sponsorDtos = sponsors.Select(s =>
        {
            var tier = s.TierId is { } tid ? tierById.GetValueOrDefault(tid) : null;
            var pk = perksBySponsor.GetValueOrDefault(s.Id);
            return SponsorsViews.Sponsor(s, tier?.Class, tier?.Name, compsBySponsor.GetValueOrDefault(s.Id), pk.Delivered, pk.Total);
        }).ToList();

        return new SponsorsPageDto(eventId, eventName, Kpis(sponsorDtos, tierDtos), tierDtos, sponsorDtos);
    }

    private static List<SponsorKpiDto> Kpis(List<SponsorDto> sponsors, List<SponsorTierDto> tiers)
    {
        var confirmed = sponsors.Where(s => s.Status == SponsorStatus.Confirmed).ToList();
        var cashCommitted = confirmed.Sum(s => s.CashAmount ?? 0m);
        var cashReceived = confirmed.Where(s => s.CashReceived).Sum(s => s.CashAmount ?? 0m);
        var fromIndividuals = confirmed.Where(s => s.Kind == SponsorKind.Individual).Sum(s => s.CashAmount ?? 0m);
        var inKindItems = confirmed.Count(s => s.InKindDescription is not null);
        var compsUsed = sponsors.Sum(s => s.CompsUsed);
        var compsEntitlement = confirmed.Sum(s => s.CompsEntitlement);
        var missingAssets = confirmed.Count(s => s.Kind == SponsorKind.Organization && (s.LogoFileId is null || s.WebsiteUrl is null));

        return
        [
            new("Cash committed", SponsorsViews.Peso(cashCommitted),
                $"{SponsorsViews.Peso(cashReceived)} received · {SponsorsViews.Peso(fromIndividuals)} from individuals"),
            new("In-kind sponsors", inKindItems.ToString(CultureInfo.InvariantCulture), "Confirmed in-kind contributions"),
            new("Comped seats", $"{compsUsed} / {compsEntitlement}", "Linked to registrations"),
            new("Missing logo or website", missingAssets.ToString(CultureInfo.InvariantCulture), "Confirmed organizations to follow up"),
        ];
    }
}
