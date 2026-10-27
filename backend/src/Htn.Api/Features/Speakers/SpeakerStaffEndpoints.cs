using System.Security.Cryptography;
using Htn.Api.Auth;
using Htn.Api.Common;
using Htn.Domain.Common;
using Htn.Domain.Events;
using Htn.Domain.Files;
using Htn.Domain.Speakers;
using Htn.Infrastructure.Audit;
using Htn.Infrastructure.Data;
using Htn.Infrastructure.Email;
using Htn.Infrastructure.Identity;
using Htn.Infrastructure.Outbox;
using Htn.Infrastructure.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Htn.Api.Features.Speakers;

/// <summary>
/// Frame 22: the speaker list, confirmation links, and the board/Programs-lead-only travel view. Every route carries
/// <c>Speakers.Manage</c> — board, or the lead of the event's Programs committee, re-checked per event — because
/// travel details are the one thing in this slice that is never shown more broadly.
/// </summary>
public static class SpeakerStaffEndpoints
{
    public const string SpeakerClosed = "speaker_removed";

    public static RouteGroupBuilder MapSpeakerStaff(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/events/{eventId:guid}").WithTags("Speakers");

        g.MapGet("/agenda/speakers", List).RequireAuthorization(Policies.SpeakersManage);
        g.MapGet("/agenda/speakers/{sid:guid}", Detail).RequireAuthorization(Policies.SpeakersManage);
        g.MapPut("/agenda/speakers/{sid:guid}", Patch).RequireAuthorization(Policies.SpeakersManage)
            .RequireRateLimiting(SponsorsRateLimiting.Write).Validate<SpeakerPatchRequest>();
        g.MapPost("/agenda/speakers/{sid:guid}/photo/approve", ApprovePhoto).RequireAuthorization(Policies.SpeakersManage)
            .RequireRateLimiting(SponsorsRateLimiting.Write);
        g.MapPost("/agenda/speakers/{sid:guid}/link", CreateLink).RequireAuthorization(Policies.SpeakersManage)
            .RequireRateLimiting(SponsorsRateLimiting.Write);
        g.MapPost("/agenda/speakers/{sid:guid}/link/resend", ResendLink).RequireAuthorization(Policies.SpeakersManage)
            .RequireRateLimiting(SponsorsRateLimiting.Write);
        g.MapPost("/agenda/speakers/{sid:guid}/link/revoke", RevokeLink).RequireAuthorization(Policies.SpeakersManage)
            .RequireRateLimiting(SponsorsRateLimiting.Write).Validate<SpeakerReasonRequest>();
        g.MapPost("/agenda/speakers/links/send-unsent", SendUnsent).RequireAuthorization(Policies.SpeakersManage)
            .RequireRateLimiting(SponsorsRateLimiting.Write);
        g.MapPut("/agenda/speakers/{sid:guid}/travel", PatchTravel).RequireAuthorization(Policies.SpeakersManage)
            .RequireRateLimiting(SponsorsRateLimiting.Write).Validate<SpeakerTravelPatchRequest>();
        g.MapPost("/agenda/speakers/{sid:guid}/plus-one/remove", RemovePlusOne).RequireAuthorization(Policies.SpeakersManage)
            .RequireRateLimiting(SponsorsRateLimiting.Write).Validate<SpeakerReasonRequest>();
        g.MapPut("/travel-mode", SetTravelMode).RequireAuthorization(Policies.EventsManage)
            .Validate<TravelModeRequest>();

        return g;
    }

    // ---------- List / detail ----------

    private static async Task<IResult> List(
        Guid eventId, HttpContext http, IAuthorizationService auth, HtnDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var ev = await db.Events.AsNoTracking().Where(e => e.Id == eventId)
            .Select(e => new { e.Id, e.TravelMode }).FirstOrDefaultAsync(ct);
        if (ev is null) return TypedResults.NotFound();
        if (await auth.DenyAsync(http, new EventScope(eventId), Policies.SpeakersManage) is { } denied) return denied;
        var now = clock.GetUtcNow();

        var speakers = await db.Speakers.AsNoTracking().Where(sp => sp.EventId == eventId && sp.VoidedAt == null)
            .OrderBy(sp => sp.PersonName).ToListAsync(ct);
        var links = await db.SpeakerLinks.AsNoTracking().Where(l => l.EventId == eventId && l.RevokedAt == null)
            .ToListAsync(ct);
        var linkBySpeaker = links.GroupBy(l => l.SpeakerId).ToDictionary(g => g.Key, g => g.First());
        var sessionText = await SessionTextsAsync(db, eventId, ct);
        var travelBySpeaker = await db.SpeakerTravel.AsNoTracking().Where(t => t.EventId == eventId)
            .ToDictionaryAsync(t => t.SpeakerId, ct);
        var plusOnes = await db.SpeakerPlusOnes.AsNoTracking()
            .Where(p => p.EventId == eventId && p.RemovedAt == null).ToListAsync(ct);
        var plusOneBySpeaker = plusOnes.ToDictionary(p => p.SpeakerId);

        var rows = speakers.Select(sp =>
        {
            var link = linkBySpeaker.GetValueOrDefault(sp.Id);
            var (state, sub) = SpeakersViews.LinkState(link, now);
            var travel = travelBySpeaker.GetValueOrDefault(sp.Id);
            var plus = plusOneBySpeaker.GetValueOrDefault(sp.Id);
            return new SpeakerRowDto(
                sp.Id, sp.PersonName, sessionText.GetValueOrDefault(sp.Id, "No session yet"),
                sp.PhotoFileId is null ? "Not uploaded yet" : sp.PhotoApproved ? "Uploaded · approved" : "Uploaded · approve for website",
                state, sub,
                travel?.ArrivalDate is { } a ? $"{a:MMM d} → {travel.DepartureDate:MMM d}" : "—",
                travel?.HasSubmitted == true ? TravelSummary(travel, ev.TravelMode) : "Not submitted yet",
                plus is null ? "No" : $"Yes · voucher {(plus.VoucherId is null ? "pending" : "sent")}");
        }).ToList();

        var unsent = speakers.Count(sp => SpeakersViews.CanSend(linkBySpeaker.GetValueOrDefault(sp.Id), now));
        var kpis = new List<SpeakerKpiDto>
        {
            new("Confirmed speakers", speakers.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new("Links sent", links.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new("Forms submitted", speakers.Count(sp => travelBySpeaker.GetValueOrDefault(sp.Id)?.HasSubmitted == true).ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new("Plus-one vouchers", $"{plusOnes.Count(p => p.VoucherId is not null)} issued"),
        };
        return TypedResults.Ok(new SpeakersListDto(ev.TravelMode, kpis, rows));
    }

    private static async Task<IResult> Detail(
        Guid eventId, Guid sid, HttpContext http, IAuthorizationService auth, HtnDbContext db, ICurrentUser me, CancellationToken ct)
    {
        var sp = await db.Speakers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == sid && x.EventId == eventId && x.VoidedAt == null, ct);
        if (sp is null) return TypedResults.NotFound();
        if (await auth.DenyAsync(http, new EventScope(eventId), Policies.SpeakersManage) is { } denied) return denied;
        return TypedResults.Ok(await DetailDtoAsync(db, eventId, sp, ct));
    }

    private static async Task<SpeakerDetailDto> DetailDtoAsync(HtnDbContext db, Guid eventId, Domain.Speakers.Speaker sp, CancellationToken ct)
    {
        var link = await db.SpeakerLinks.AsNoTracking().FirstOrDefaultAsync(l => l.SpeakerId == sp.Id && l.RevokedAt == null, ct);
        var now = DateTimeOffset.UtcNow;
        var (state, _) = SpeakersViews.LinkState(link, now);
        var travel = await db.SpeakerTravel.AsNoTracking().FirstOrDefaultAsync(t => t.SpeakerId == sp.Id, ct);
        var plus = await db.SpeakerPlusOnes.AsNoTracking().FirstOrDefaultAsync(p => p.SpeakerId == sp.Id && p.RemovedAt == null, ct);
        var voucherCode = plus?.VoucherId is { } vid
            ? await db.Vouchers.AsNoTracking().Where(v => v.Id == vid).Select(v => v.Code).FirstOrDefaultAsync(ct)
            : null;
        var registered = plus?.VoucherId is { } v2
            && await db.Registrations.AsNoTracking().AnyAsync(r => r.VoucherId == v2 && r.Status != Domain.Registrations.RegistrationStatus.Cancelled, ct);

        var logs = link is null ? [] : await LogsAsync(db, link.Id, ct);
        var sessionText = (await SessionTextsAsync(db, eventId, ct)).GetValueOrDefault(sp.Id, "No session yet");
        var travelDto = travel is null ? null : new SpeakerTravelDto(
            travel.ArrivalDate?.ToString("yyyy-MM-dd"), travel.DepartureDate?.ToString("yyyy-MM-dd"),
            travel.FlyingFrom, travel.PreferredArrivalTime, travel.IdFullName is not null,
            travel.BusFrom, travel.BusEta, travel.NeedsTerminalPickup, travel.RoomNote, travel.NeedsNotes);

        return new SpeakerDetailDto(
            sp.Id, sp.PersonName, EmailMask.Mask(sp.Email), sp.Affiliation, sp.Status.ToString(), sessionText,
            sp.PhotoFileId is null ? "Not uploaded yet" : sp.PhotoApproved ? "Uploaded · approved" : "Uploaded · approve for website",
            sp.PhotoFileId is not null && !sp.PhotoApproved,
            travelDto,
            plus is null ? null : new SpeakerPlusOneDto(plus.Id, plus.Name, EmailMask.Mask(plus.Email), plus.SharesRoom, voucherCode ?? "pending", registered),
            logs, SpeakersViews.CanSend(link, now), link is not null);
    }

    private static async Task<List<SpeakerLogDto>> LogsAsync(HtnDbContext db, Guid linkId, CancellationToken ct) =>
        await db.SpeakerLinkAccess.AsNoTracking().Where(a => a.LinkId == linkId).OrderByDescending(a => a.At)
            .Select(a => new SpeakerLogDto(a.At, What(a.Kind, a.Note))).ToListAsync(ct);

    private static string What(SpeakerLinkAccessKind kind, string? note) => kind switch
    {
        SpeakerLinkAccessKind.LinkSent => note ?? "Link sent",
        SpeakerLinkAccessKind.Opened => "Code verified",
        SpeakerLinkAccessKind.CodeSent => "Code sent",
        SpeakerLinkAccessKind.CodeFailed => "Code entered incorrectly",
        SpeakerLinkAccessKind.CodeLocked => "Too many attempts; code locked",
        SpeakerLinkAccessKind.Submitted => "Form submitted",
        SpeakerLinkAccessKind.Revoked => note ?? "Link revoked",
        _ => kind.ToString(),
    };

    private static string TravelSummary(SpeakerTravel t, TravelMode mode) => mode switch
    {
        TravelMode.Plane => t.FlyingFrom is null ? "Plane" : $"Plane · from {t.FlyingFrom}",
        TravelMode.Bus => t.BusFrom is null ? "Bus" : $"Bus · from {t.BusFrom}",
        _ => "Submitted",
    };

    private static async Task<Dictionary<Guid, string>> SessionTextsAsync(HtnDbContext db, Guid eventId, CancellationToken ct)
    {
        var links = await (
            from ss in db.SessionSpeakers.AsNoTracking()
            join s in db.Sessions.AsNoTracking() on ss.SessionId equals s.Id
            where ss.EventId == eventId
            orderby s.StartsAt
            select new { ss.SpeakerId, s.Title, s.Kind, s.StartsAt }).ToListAsync(ct);
        var result = new Dictionary<Guid, string>();
        foreach (var l in links)
            result.TryAdd(l.SpeakerId, $"{l.Kind} · {Htn.Api.Features.Agenda.AgendaViews.Time(l.StartsAt)}");
        return result;
    }

    // ---------- Mutations ----------

    private static async Task<IResult> Patch(
        Guid eventId, Guid sid, SpeakerPatchRequest req, HttpContext http, IAuthorizationService auth, HtnDbContext db, IAuditLog audit, ICurrentUser me, TimeProvider clock, CancellationToken ct)
    {
        var sp = await db.Speakers.FirstOrDefaultAsync(x => x.Id == sid && x.EventId == eventId && x.VoidedAt == null, ct);
        if (sp is null) return TypedResults.NotFound();
        if (await auth.DenyAsync(http, new EventScope(eventId), Policies.SpeakersManage) is { } denied) return denied;
        sp.Update(req.Name ?? sp.PersonName, req.Email ?? sp.Email, req.Affiliation ?? sp.Affiliation, me.Id!.Value, clock.GetUtcNow());
        audit.Record(me.Id, "speaker.update", nameof(Domain.Speakers.Speaker), sp.Id.ToString(), after: new { sp.PersonName, sp.Email });
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await DetailDtoAsync(db, eventId, sp, ct));
    }

    private static async Task<IResult> ApprovePhoto(
        Guid eventId, Guid sid, HttpContext http, IAuthorizationService auth, HtnDbContext db, IAuditLog audit, ICurrentUser me, TimeProvider clock, CancellationToken ct)
    {
        var sp = await db.Speakers.FirstOrDefaultAsync(x => x.Id == sid && x.EventId == eventId && x.VoidedAt == null, ct);
        if (sp is null) return TypedResults.NotFound();
        if (await auth.DenyAsync(http, new EventScope(eventId), Policies.SpeakersManage) is { } denied) return denied;
        try { sp.ApprovePhoto(me.Id!.Value, clock.GetUtcNow()); }
        catch (DomainException ex) { return Problems.Field("photo", ex.Message, "no_photo"); }
        audit.Record(me.Id, "speaker.photo_approve", nameof(Domain.Speakers.Speaker), sp.Id.ToString());
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await DetailDtoAsync(db, eventId, sp, ct));
    }

    private static Task<IResult> CreateLink(
        Guid eventId, Guid sid, HttpContext http, IAuthorizationService auth, HtnDbContext db, IPublicIdGenerator ids, IAuditLog audit, ICurrentUser me, TimeProvider clock, CancellationToken ct) =>
        IssueLinkAsync(eventId, sid, "speaker.link_create", http, auth, db, ids, audit, me, clock, ct);

    /// <summary>
    /// "Resend" and "create" are the same operation: only the token's hash is ever stored, so the server cannot
    /// recover an earlier raw token to re-mail it unchanged. Reissuing is strictly safer than it sounds — a link
    /// that leaked (forwarded, screenshotted) stops working the moment a fresh one is sent — and the speaker always
    /// ends up with exactly one working link, which is what the design asks for either way.
    /// </summary>
    private static Task<IResult> ResendLink(
        Guid eventId, Guid sid, HttpContext http, IAuthorizationService auth, HtnDbContext db, IPublicIdGenerator ids, IAuditLog audit, ICurrentUser me, TimeProvider clock, CancellationToken ct) =>
        IssueLinkAsync(eventId, sid, "speaker.link_resend", http, auth, db, ids, audit, me, clock, ct);

    private static async Task<IResult> IssueLinkAsync(
        Guid eventId, Guid sid, string auditAction, HttpContext http, IAuthorizationService auth, HtnDbContext db, IPublicIdGenerator ids, IAuditLog audit, ICurrentUser me, TimeProvider clock, CancellationToken ct)
    {
        var sp = await db.Speakers.FirstOrDefaultAsync(x => x.Id == sid && x.EventId == eventId && x.VoidedAt == null, ct);
        if (sp is null) return TypedResults.NotFound();
        if (await auth.DenyAsync(http, new EventScope(eventId), Policies.SpeakersManage) is { } denied) return denied;
        var now = clock.GetUtcNow();

        // At most one live link per speaker (ux_speaker_links_open): revoke any still-live one first.
        var existing = await db.SpeakerLinks.FirstOrDefaultAsync(l => l.SpeakerId == sid && l.RevokedAt == null, ct);
        existing?.Revoke("Replaced by a new link", me.Id!.Value, now);

        var raw = ids.NewToken(32);
        var link = SpeakerLink.Create(sid, eventId, SpeakerLinksOps.Hash(raw), me.Id!.Value, now);
        link.MarkSent(now);
        db.SpeakerLinks.Add(link);
        var actor = await ActorNameAsync(db, me.Id, ct);
        db.SpeakerLinkAccess.Add(SpeakerLinkAccess.Create(link.Id, sid, eventId, SpeakerLinkAccessKind.LinkSent, now, me.Id, note: $"Link sent by {actor}"));
        db.Outbox.Add(OutboxProcessor.SpeakerLinkEmail(link.Id, raw, now));
        audit.Record(me.Id, auditAction, nameof(SpeakerLink), link.Id.ToString(), after: new { speakerId = sid });
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await DetailDtoAsync(db, eventId, sp, ct));
    }

    private static async Task<IResult> RevokeLink(
        Guid eventId, Guid sid, SpeakerReasonRequest req, HttpContext http, IAuthorizationService auth, HtnDbContext db, IAuditLog audit, ICurrentUser me, TimeProvider clock, CancellationToken ct)
    {
        var sp = await db.Speakers.FirstOrDefaultAsync(x => x.Id == sid && x.EventId == eventId && x.VoidedAt == null, ct);
        if (sp is null) return TypedResults.NotFound();
        if (await auth.DenyAsync(http, new EventScope(eventId), Policies.SpeakersManage) is { } denied) return denied;
        var link = await db.SpeakerLinks.FirstOrDefaultAsync(l => l.SpeakerId == sid && l.RevokedAt == null, ct);
        if (link is null) return Problems.Result(StatusCodes.Status409Conflict, "That cannot be done right now.", "No live link to revoke.", SpeakerLinkErrorCodes.NoLiveLink);
        var now = clock.GetUtcNow();
        link.Revoke(req.Reason!, me.Id!.Value, now);
        db.SpeakerLinkAccess.Add(SpeakerLinkAccess.Create(link.Id, sid, eventId, SpeakerLinkAccessKind.Revoked, now, me.Id, note: $"Link revoked by {await ActorNameAsync(db, me.Id, ct)}"));
        audit.Record(me.Id, "speaker.link_revoke", nameof(SpeakerLink), link.Id.ToString(), reason: req.Reason);
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await DetailDtoAsync(db, eventId, sp, ct));
    }

    private static async Task<IResult> SendUnsent(
        Guid eventId, HttpContext http, IAuthorizationService auth, HtnDbContext db, IPublicIdGenerator ids, IAuditLog audit, ICurrentUser me, TimeProvider clock, CancellationToken ct)
    {
        if (!await db.Events.AnyAsync(e => e.Id == eventId, ct)) return TypedResults.NotFound();
        if (await auth.DenyAsync(http, new EventScope(eventId), Policies.SpeakersManage) is { } denied) return denied;
        var now = clock.GetUtcNow();
        var speakerIds = await db.Speakers.Where(sp => sp.EventId == eventId && sp.VoidedAt == null).Select(sp => sp.Id).ToListAsync(ct);
        var links = await db.SpeakerLinks.Where(l => l.EventId == eventId && l.RevokedAt == null).ToListAsync(ct);
        var bySpeaker = links.ToDictionary(l => l.SpeakerId);
        var toSend = speakerIds.Where(id => SpeakersViews.CanSend(bySpeaker.GetValueOrDefault(id), now)).ToList();
        foreach (var sid in toSend)
            await IssueLinkAsync(eventId, sid, "speaker.link_send_unsent", http, auth, db, ids, audit, me, clock, ct);
        return TypedResults.Ok(new SendUnsentResultDto(toSend.Count));
    }

    private static async Task<IResult> PatchTravel(
        Guid eventId, Guid sid, SpeakerTravelPatchRequest req, HttpContext http, IAuthorizationService auth, HtnDbContext db, IAuditLog audit, ICurrentUser me, TimeProvider clock, CancellationToken ct)
    {
        var sp = await db.Speakers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == sid && x.EventId == eventId && x.VoidedAt == null, ct);
        if (sp is null) return TypedResults.NotFound();
        if (await auth.DenyAsync(http, new EventScope(eventId), Policies.SpeakersManage) is { } denied) return denied;
        var travel = await db.SpeakerTravel.FirstOrDefaultAsync(t => t.SpeakerId == sid, ct);
        if (travel is null) { travel = SpeakerTravel.Create(sid, eventId); db.SpeakerTravel.Add(travel); }
        try { travel.UpdateByStaff(req.ArrivalDate, req.DepartureDate, req.RoomNote, clock.GetUtcNow()); }
        catch (DomainException ex) { return Problems.Field("departureDate", ex.Message, "invalid_dates"); }
        audit.Record(me.Id, "speaker.travel_update", nameof(SpeakerTravel), sid.ToString(), after: new { req.ArrivalDate, req.DepartureDate });
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await DetailDtoAsync(db, eventId, (await db.Speakers.AsNoTracking().FirstAsync(x => x.Id == sid, ct)), ct));
    }

    private static async Task<IResult> RemovePlusOne(
        Guid eventId, Guid sid, SpeakerReasonRequest req, HttpContext http, IAuthorizationService auth, HtnDbContext db, IAuditLog audit, ICurrentUser me, TimeProvider clock, CancellationToken ct)
    {
        var sp = await db.Speakers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == sid && x.EventId == eventId && x.VoidedAt == null, ct);
        if (sp is null) return TypedResults.NotFound();
        if (await auth.DenyAsync(http, new EventScope(eventId), Policies.SpeakersManage) is { } denied) return denied;
        var plus = await db.SpeakerPlusOnes.FirstOrDefaultAsync(p => p.SpeakerId == sid && p.RemovedAt == null, ct);
        if (plus is null) return TypedResults.NotFound();
        var now = clock.GetUtcNow();
        plus.Remove(req.Reason!, me.Id!.Value, now);
        if (plus.VoucherId is { } voucherId)
        {
            var voucher = await db.Vouchers.FirstOrDefaultAsync(v => v.Id == voucherId, ct);
            voucher?.Deactivate(now);
        }
        audit.Record(me.Id, "speaker.plus_one_remove", nameof(SpeakerPlusOne), plus.Id.ToString(), reason: req.Reason);
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await DetailDtoAsync(db, eventId, (await db.Speakers.AsNoTracking().FirstAsync(x => x.Id == sid, ct)), ct));
    }

    private static async Task<IResult> SetTravelMode(
        Guid eventId, TravelModeRequest req, HtnDbContext db, IAuditLog audit, ICurrentUser me, TimeProvider clock, CancellationToken ct)
    {
        var ev = await db.Events.FirstOrDefaultAsync(e => e.Id == eventId, ct);
        if (ev is null) return TypedResults.NotFound();
        ev.UpdateTravelSettings(req.TravelMode!.Value, clock.GetUtcNow(), me.Id);
        audit.Record(me.Id, "event.travel_mode", nameof(Event), eventId.ToString(), after: new { travelMode = ev.TravelMode.ToString() });
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<string> ActorNameAsync(HtnDbContext db, Guid? personId, CancellationToken ct)
    {
        if (personId is not { } id) return "the system";
        var name = await db.People.AsNoTracking().Where(p => p.Id == id).Select(p => p.FirstName + " " + p.LastName).FirstOrDefaultAsync(ct);
        return name ?? "a board member";
    }
}
