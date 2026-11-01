using System.Globalization;
using Htn.Domain.Agenda;
using Htn.Domain.Common;
using Htn.Domain.Speakers;
using Htn.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Htn.Api.Features.Agenda;

public static class AgendaViews
{
    public static RoomDto Room(EventRoom r) => new(r.Id, r.Name, r.ColorHex, r.SortOrder, r.IsActive);

    /// <summary>A session's time label in Manila, e.g. "9:15 AM".</summary>
    public static string Time(DateTimeOffset startsAt) =>
        ManilaTime.ToManila(startsAt).ToString("h:mm tt", CultureInfo.InvariantCulture);

    private static string DayHeading(DateOnly date) =>
        date.ToDateTime(TimeOnly.MinValue).ToString("dddd, MMM d", CultureInfo.InvariantCulture);

    public static async Task<AgendaDto> BuildAsync(HtnDbContext db, Guid eventId, string eventName, bool canManage, CancellationToken ct)
    {
        var rooms = await db.EventRooms.AsNoTracking().Where(r => r.EventId == eventId)
            .OrderBy(r => r.SortOrder).ThenBy(r => r.Name).ToListAsync(ct);
        var roomById = rooms.ToDictionary(r => r.Id);

        var sessions = await db.Sessions.AsNoTracking().Where(s => s.EventId == eventId)
            .OrderBy(s => s.StartsAt).ToListAsync(ct);
        var sessionIds = sessions.Select(s => s.Id).ToList();

        var sessionRooms = await db.SessionRooms.AsNoTracking().Where(sr => sessionIds.Contains(sr.SessionId))
            .ToListAsync(ct);
        var roomsBySession = sessionRooms.GroupBy(sr => sr.SessionId).ToDictionary(g => g.Key, g => g.Select(x => x.RoomId).ToList());

        var links = await (
            from ss in db.SessionSpeakers.AsNoTracking()
            join sp in db.Speakers.AsNoTracking() on ss.SpeakerId equals sp.Id
            where sessionIds.Contains(ss.SessionId) && sp.VoidedAt == null
            select new { ss.SessionId, ss.SpeakerId, sp.PersonName, ss.Role, ss.SortOrder, sp.Status }).ToListAsync(ct);
        var speakersBySession = links.GroupBy(l => l.SessionId).ToDictionary(
            g => g.Key,
            g => g.OrderBy(x => x.SortOrder).Select(x => new SessionSpeakerDto(
                x.SpeakerId, x.PersonName, x.Role.ToString(), x.SortOrder, x.Status != SpeakerStatus.Confirmed)).ToList());

        var dtos = sessions.Select(s =>
        {
            var sessionRoomDtos = (roomsBySession.GetValueOrDefault(s.Id) ?? [])
                .Where(roomById.ContainsKey).Select(id => Room(roomById[id])).ToList();
            var speakers = speakersBySession.GetValueOrDefault(s.Id) ?? [];
            // A session is tentative if a speaker is unconfirmed, or a non-Break session has no speaker yet.
            var tentative = speakers.Any(sp => sp.Tentative) || (s.Kind != SessionKind.Break && speakers.Count == 0);
            return new SessionDto(s.Id, s.Title, s.Kind.ToString(), s.StartsAt, s.DurationMinutes, Time(s.StartsAt),
                s.OwnerCommitteeId, s.IsPublic, canManage ? s.Notes : null, sessionRoomDtos, speakers, tentative);
        }).ToList();

        var days = dtos
            .GroupBy(d => ManilaTime.ManilaDate(d.StartsAt))
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var slots = g.GroupBy(d => d.Time)
                    .OrderBy(x => x.First().StartsAt)
                    .Select(x => new AgendaSlotDto(x.Key, x.OrderBy(d => d.StartsAt).ToList()))
                    .ToList();
                var tentativeSpeakers = g.SelectMany(d => d.Speakers).Count(sp => sp.Tentative);
                return new AgendaDayDto(g.Key, DayHeading(g.Key), g.Count(), tentativeSpeakers, slots);
            })
            .ToList();

        return new AgendaDto(eventId, eventName, rooms.Select(Room).ToList(), days, canManage);
    }

    public static async Task<SessionDto?> SessionAsync(HtnDbContext db, Guid eventId, Guid sessionId, CancellationToken ct)
    {
        var s = await db.Sessions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == sessionId && x.EventId == eventId, ct);
        if (s is null) return null;
        var rooms = await (
            from sr in db.SessionRooms.AsNoTracking()
            join r in db.EventRooms.AsNoTracking() on sr.RoomId equals r.Id
            where sr.SessionId == sessionId
            orderby r.SortOrder, r.Name
            select r).ToListAsync(ct);
        var speakers = await (
            from ss in db.SessionSpeakers.AsNoTracking()
            join sp in db.Speakers.AsNoTracking() on ss.SpeakerId equals sp.Id
            where ss.SessionId == sessionId && sp.VoidedAt == null
            orderby ss.SortOrder
            select new { ss.SpeakerId, sp.PersonName, ss.Role, ss.SortOrder, sp.Status }).ToListAsync(ct);
        var speakerDtos = speakers.Select(x => new SessionSpeakerDto(
            x.SpeakerId, x.PersonName, x.Role.ToString(), x.SortOrder, x.Status != SpeakerStatus.Confirmed)).ToList();
        var tentative = speakerDtos.Any(sp => sp.Tentative) || (s.Kind != SessionKind.Break && speakerDtos.Count == 0);
        return new SessionDto(s.Id, s.Title, s.Kind.ToString(), s.StartsAt, s.DurationMinutes, Time(s.StartsAt),
            s.OwnerCommitteeId, s.IsPublic, s.Notes, rooms.Select(Room).ToList(), speakerDtos, tentative);
    }
}
