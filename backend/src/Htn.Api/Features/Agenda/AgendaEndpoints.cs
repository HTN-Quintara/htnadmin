using System.Globalization;
using Htn.Api.Auth;
using Htn.Api.Common;
using Htn.Domain.Agenda;
using Htn.Domain.Common;
using Htn.Domain.Speakers;
using Htn.Infrastructure.Audit;
using Htn.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Htn.Api.Features.Agenda;

/// <summary>
/// Frame 16: the event programme. Reads are open to anyone on the event (<c>Events.View</c>); writes are the board or
/// the Programs committee lead (<c>Speakers.Manage</c>, re-checked per event). Day headings, time slots and the
/// "Tentative" flag are all derived in Manila time; nothing about the calendar or tentativeness is stored.
/// </summary>
public static class AgendaEndpoints
{
    public const string RoomBusy = "room_busy";
    public const string RoomRequired = "room_required";
    public const string UnknownRoom = "unknown_room";

    public static RouteGroupBuilder MapAgenda(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/events/{eventId:guid}").WithTags("Agenda");

        g.MapGet("/agenda", Read).RequireAuthorization(Policies.EventsView);

        g.MapGet("/rooms", ListRooms).RequireAuthorization(Policies.EventsView);
        g.MapPost("/rooms", CreateRoom).RequireAuthorization(Policies.SpeakersManage)
            .RequireRateLimiting(SponsorsRateLimiting.Write).Validate<RoomRequest>();
        g.MapPut("/rooms/{roomId:guid}", UpdateRoom).RequireAuthorization(Policies.SpeakersManage)
            .RequireRateLimiting(SponsorsRateLimiting.Write).Validate<RoomRequest>();
        g.MapPost("/rooms/{roomId:guid}/deactivate", DeactivateRoom).RequireAuthorization(Policies.SpeakersManage)
            .RequireRateLimiting(SponsorsRateLimiting.Write);

        g.MapPost("/sessions", CreateSession).RequireAuthorization(Policies.SpeakersManage)
            .RequireRateLimiting(SponsorsRateLimiting.Write).Validate<SessionRequest>();
        g.MapPut("/sessions/{sessionId:guid}", UpdateSession).RequireAuthorization(Policies.SpeakersManage)
            .RequireRateLimiting(SponsorsRateLimiting.Write).Validate<SessionRequest>();
        g.MapDelete("/sessions/{sessionId:guid}", DeleteSession).RequireAuthorization(Policies.SpeakersManage)
            .RequireRateLimiting(SponsorsRateLimiting.Write);
        g.MapPut("/sessions/{sessionId:guid}/speakers", SetSessionSpeakers).RequireAuthorization(Policies.SpeakersManage)
            .RequireRateLimiting(SponsorsRateLimiting.Write).Validate<SessionSpeakersRequest>();

        return g;
    }

    // ---------- Read ----------

    private static async Task<IResult> Read(
        Guid eventId, HttpContext http, IAuthorizationService auth, HtnDbContext db, CancellationToken ct)
    {
        var ev = await db.Events.AsNoTracking().Where(e => e.Id == eventId)
            .Select(e => new { e.Id, e.Name }).FirstOrDefaultAsync(ct);
        if (ev is null) return TypedResults.NotFound();
        var canManage = await auth.AllowsAsync(http, new EventScope(eventId), Policies.SpeakersManage);
        return TypedResults.Ok(await AgendaViews.BuildAsync(db, eventId, ev.Name, canManage, ct));
    }

    private static async Task<IResult> ListRooms(Guid eventId, HtnDbContext db, CancellationToken ct)
    {
        if (!await db.Events.AnyAsync(e => e.Id == eventId, ct)) return TypedResults.NotFound();
        var rooms = await db.EventRooms.AsNoTracking().Where(r => r.EventId == eventId)
            .OrderBy(r => r.SortOrder).ThenBy(r => r.Name).ToListAsync(ct);
        return TypedResults.Ok(rooms.Select(AgendaViews.Room).ToList());
    }

    // ---------- Rooms ----------

    private static async Task<IResult> CreateRoom(
        Guid eventId, RoomRequest req, HttpContext http, IAuthorizationService auth, HtnDbContext db,
        IAuditLog audit, ICurrentUser me, TimeProvider clock, CancellationToken ct)
    {
        if (!await db.Events.AnyAsync(e => e.Id == eventId, ct)) return TypedResults.NotFound();
        if (await auth.DenyAsync(http, new EventScope(eventId), Policies.SpeakersManage) is { } denied) return denied;
        var room = EventRoom.Create(eventId, req.Name!, req.ColorHex!, req.SortOrder ?? 0, clock.GetUtcNow());
        db.EventRooms.Add(room);
        audit.Record(me.Id, AgendaAudit.RoomCreate, nameof(EventRoom), room.Id.ToString(), after: new { room.Name });
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(AgendaViews.Room(room));
    }

    private static async Task<IResult> UpdateRoom(
        Guid eventId, Guid roomId, RoomRequest req, HttpContext http, IAuthorizationService auth, HtnDbContext db,
        IAuditLog audit, ICurrentUser me, CancellationToken ct)
    {
        var room = await db.EventRooms.FirstOrDefaultAsync(r => r.Id == roomId && r.EventId == eventId, ct);
        if (room is null) return TypedResults.NotFound();
        if (await auth.DenyAsync(http, new EventScope(eventId), Policies.SpeakersManage) is { } denied) return denied;
        var roomBefore = new { room.Name, room.ColorHex, room.SortOrder };
        room.Update(req.Name!, req.ColorHex!, req.SortOrder ?? room.SortOrder);
        audit.Record(me.Id, AgendaAudit.RoomUpdate, nameof(EventRoom), room.Id.ToString(),
            roomBefore, new { room.Name, room.ColorHex, room.SortOrder });
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(AgendaViews.Room(room));
    }

    private static async Task<IResult> DeactivateRoom(
        Guid eventId, Guid roomId, HttpContext http, IAuthorizationService auth, HtnDbContext db,
        IAuditLog audit, ICurrentUser me, TimeProvider clock, CancellationToken ct)
    {
        var room = await db.EventRooms.FirstOrDefaultAsync(r => r.Id == roomId && r.EventId == eventId, ct);
        if (room is null) return TypedResults.NotFound();
        if (await auth.DenyAsync(http, new EventScope(eventId), Policies.SpeakersManage) is { } denied) return denied;
        if (room.IsActive) { room.Deactivate(clock.GetUtcNow()); audit.Record(me.Id, AgendaAudit.RoomDeactivate, nameof(EventRoom), room.Id.ToString()); await db.SaveChangesAsync(ct); }
        return TypedResults.NoContent();
    }

    // ---------- Sessions ----------

    private static async Task<IResult> CreateSession(
        Guid eventId, SessionRequest req, HttpContext http, IAuthorizationService auth, HtnDbContext db,
        IAuditLog audit, ICurrentUser me, TimeProvider clock, CancellationToken ct)
    {
        if (!await db.Events.AnyAsync(e => e.Id == eventId, ct)) return TypedResults.NotFound();
        if (await auth.DenyAsync(http, new EventScope(eventId), Policies.SpeakersManage) is { } denied) return denied;
        if (await CommitteeError(db, eventId, req.OwnerCommitteeId, ct) is { } ce) return ce;

        var session = Session.Create(eventId, ToNew(req), me.Id!.Value, clock.GetUtcNow());
        if (await LinkError(db, eventId, session, req.RoomIds, req.Speakers, ct) is { } error) return error;

        db.Sessions.Add(session);
        AddLinks(db, eventId, session.Id, req.RoomIds, req.Speakers);
        audit.Record(me.Id, AgendaAudit.SessionCreate, nameof(Session), session.Id.ToString(), after: new { session.Title, kind = session.Kind.ToString() });
        await db.SaveChangesAsync(ct);
        return await ReadSession(db, eventId, session.Id, ct);
    }

    private static async Task<IResult> UpdateSession(
        Guid eventId, Guid sessionId, SessionRequest req, HttpContext http, IAuthorizationService auth, HtnDbContext db,
        IAuditLog audit, ICurrentUser me, TimeProvider clock, CancellationToken ct)
    {
        var session = await db.Sessions.FirstOrDefaultAsync(s => s.Id == sessionId && s.EventId == eventId, ct);
        if (session is null) return TypedResults.NotFound();
        if (await auth.DenyAsync(http, new EventScope(eventId), Policies.SpeakersManage) is { } denied) return denied;
        if (await CommitteeError(db, eventId, req.OwnerCommitteeId, ct) is { } ce) return ce;

        var before = new
        {
            session.Title, kind = session.Kind.ToString(), session.StartsAt, session.DurationMinutes,
            roomIds = await db.SessionRooms.Where(r => r.SessionId == sessionId).Select(r => r.RoomId).ToListAsync(ct),
            speakerIds = await db.SessionSpeakers.Where(x => x.SessionId == sessionId).Select(x => x.SpeakerId).ToListAsync(ct),
        };
        session.Update(ToNew(req), me.Id!.Value, clock.GetUtcNow());
        if (await LinkError(db, eventId, session, req.RoomIds, req.Speakers, ct, excludeSessionId: sessionId) is { } error) return error;

        // Replace rooms and speakers wholesale.
        db.SessionRooms.RemoveRange(db.SessionRooms.Where(r => r.SessionId == sessionId));
        db.SessionSpeakers.RemoveRange(db.SessionSpeakers.Where(s => s.SessionId == sessionId));
        AddLinks(db, eventId, sessionId, req.RoomIds, req.Speakers);
        audit.Record(me.Id, AgendaAudit.SessionUpdate, nameof(Session), session.Id.ToString(),
            before, new { session.Title, kind = session.Kind.ToString(), session.StartsAt, session.DurationMinutes,
                roomIds = req.RoomIds, speakerIds = req.Speakers?.Select(x => x.SpeakerId) });
        await db.SaveChangesAsync(ct);
        return await ReadSession(db, eventId, sessionId, ct);
    }

    private static async Task<IResult> DeleteSession(
        Guid eventId, Guid sessionId, HttpContext http, IAuthorizationService auth, HtnDbContext db,
        IAuditLog audit, ICurrentUser me, CancellationToken ct)
    {
        var session = await db.Sessions.FirstOrDefaultAsync(s => s.Id == sessionId && s.EventId == eventId, ct);
        if (session is null) return TypedResults.NotFound();
        if (await auth.DenyAsync(http, new EventScope(eventId), Policies.SpeakersManage) is { } denied) return denied;
        // The one allowed hard delete in Phase 6: a session holds no financial/registration/sponsor data. Audited.
        db.SessionRooms.RemoveRange(db.SessionRooms.Where(r => r.SessionId == sessionId));
        db.SessionSpeakers.RemoveRange(db.SessionSpeakers.Where(s => s.SessionId == sessionId));
        db.Sessions.Remove(session);
        audit.Record(me.Id, AgendaAudit.SessionDelete, nameof(Session), session.Id.ToString(),
            before: new { session.Title, kind = session.Kind.ToString(), session.StartsAt });
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> SetSessionSpeakers(
        Guid eventId, Guid sessionId, SessionSpeakersRequest req, HttpContext http, IAuthorizationService auth,
        HtnDbContext db, IAuditLog audit, ICurrentUser me, CancellationToken ct)
    {
        var session = await db.Sessions.FirstOrDefaultAsync(s => s.Id == sessionId && s.EventId == eventId, ct);
        if (session is null) return TypedResults.NotFound();
        if (await auth.DenyAsync(http, new EventScope(eventId), Policies.SpeakersManage) is { } denied) return denied;
        if (await SpeakersError(db, eventId, req.Speakers, ct) is { } error) return error;
        var beforeSpeakers = await db.SessionSpeakers.Where(x => x.SessionId == sessionId).Select(x => x.SpeakerId).ToListAsync(ct);
        db.SessionSpeakers.RemoveRange(db.SessionSpeakers.Where(s => s.SessionId == sessionId));
        AddLinks(db, eventId, sessionId, null, req.Speakers);
        audit.Record(me.Id, AgendaAudit.SessionUpdate, nameof(Session), sessionId.ToString(),
            new { speakerIds = beforeSpeakers }, new { speakerIds = req.Speakers?.Select(x => x.SpeakerId) });
        await db.SaveChangesAsync(ct);
        return await ReadSession(db, eventId, sessionId, ct);
    }

    // ---------- Helpers ----------

    private static NewSession ToNew(SessionRequest r) => new(
        r.Title!, r.Kind!.Value, r.StartsAt!.Value, r.DurationMinutes!.Value, r.OwnerCommitteeId, r.IsPublic ?? false, r.Notes);

    private static void AddLinks(HtnDbContext db, Guid eventId, Guid sessionId, IReadOnlyList<Guid>? roomIds, IReadOnlyList<SessionSpeakerRequest>? speakers)
    {
        foreach (var roomId in (roomIds ?? []).Distinct())
            db.SessionRooms.Add(SessionRoom.Create(sessionId, roomId, eventId));
        var order = 0;
        foreach (var sp in speakers ?? [])
            db.SessionSpeakers.Add(SessionSpeaker.Create(sessionId, sp.SpeakerId!.Value, eventId, sp.Role ?? SessionSpeakerRole.Speaker, sp.SortOrder ?? order++));
    }

    /// <summary>Validates the rooms and speakers of a session, and refuses a double-booked room before the DB does.</summary>
    private static async Task<IResult?> LinkError(
        HtnDbContext db, Guid eventId, Session session, IReadOnlyList<Guid>? roomIds, IReadOnlyList<SessionSpeakerRequest>? speakers,
        CancellationToken ct, Guid? excludeSessionId = null)
    {
        var rooms = (roomIds ?? []).Distinct().ToList();
        if (session.Kind != SessionKind.Break && rooms.Count == 0)
            return Problems.Field("roomIds", "Pick at least one room for this session.", RoomRequired);
        foreach (var roomId in rooms)
            if (!await db.EventRooms.AnyAsync(r => r.Id == roomId && r.EventId == eventId && r.IsActive, ct))
                return Problems.Field("roomIds", "That room no longer exists.", UnknownRoom);

        // A room cannot hold two sessions whose times overlap.
        foreach (var roomId in rooms)
        {
            // EndsAt/overlap use AddMinutes, so filter in memory after a cheap event+room fetch.
            var busy = await (
                from sr in db.SessionRooms.AsNoTracking()
                join s2 in db.Sessions.AsNoTracking() on sr.SessionId equals s2.Id
                where sr.RoomId == roomId && sr.EventId == eventId && sr.SessionId != excludeSessionId
                select new { s2.StartsAt, s2.DurationMinutes }).ToListAsync(ct);
            if (busy.Any(x => x.StartsAt < session.EndsAt && x.StartsAt.AddMinutes(x.DurationMinutes) > session.StartsAt))
                return Problems.Result(StatusCodes.Status409Conflict, "That cannot be done right now.",
                    "That room is already booked for an overlapping time.", RoomBusy);
        }
        return await SpeakersError(db, eventId, speakers, ct);
    }

    private static async Task<IResult?> CommitteeError(HtnDbContext db, Guid eventId, Guid? committeeId, CancellationToken ct)
    {
        if (committeeId is not { } id || id == Guid.Empty) return null;
        if (!await db.Committees.AnyAsync(c => c.Id == id && c.EventId == eventId, ct))
            return Problems.Field("ownerCommitteeId", "That committee is not part of this event.", "unknown_committee");
        return null;
    }

    private static async Task<IResult?> SpeakersError(HtnDbContext db, Guid eventId, IReadOnlyList<SessionSpeakerRequest>? speakers, CancellationToken ct)
    {
        foreach (var sp in speakers ?? [])
        {
            if (sp.SpeakerId is not { } id || id == Guid.Empty)
                return Problems.Field("speakers", "A speaker is required.", "unknown_speaker");
            if (!await db.Speakers.AnyAsync(s => s.Id == id && s.EventId == eventId && s.VoidedAt == null, ct))
                return Problems.Field("speakers", "That speaker no longer exists.", "unknown_speaker");
        }
        return null;
    }

    private static async Task<IResult> ReadSession(HtnDbContext db, Guid eventId, Guid sessionId, CancellationToken ct)
    {
        var dto = await AgendaViews.SessionAsync(db, eventId, sessionId, ct);
        return dto is null ? TypedResults.NotFound() : TypedResults.Ok(dto);
    }
}

public static class AgendaAudit
{
    public const string RoomCreate = "room.create";
    public const string RoomUpdate = "room.update";
    public const string RoomDeactivate = "room.deactivate";
    public const string SessionCreate = "session.create";
    public const string SessionUpdate = "session.update";
    public const string SessionDelete = "session.delete";
}
