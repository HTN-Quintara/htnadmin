using Htn.Domain.Agenda;

namespace Htn.Api.Features.Agenda;

public sealed record RoomRequest(string? Name, string? ColorHex, int? SortOrder);

public sealed record SessionSpeakerRequest(Guid? SpeakerId, SessionSpeakerRole? Role, int? SortOrder);

public sealed record SessionRequest(
    string? Title, SessionKind? Kind, DateTimeOffset? StartsAt, int? DurationMinutes,
    Guid? OwnerCommitteeId, bool? IsPublic, string? Notes,
    IReadOnlyList<Guid>? RoomIds, IReadOnlyList<SessionSpeakerRequest>? Speakers);

public sealed record SessionSpeakersRequest(IReadOnlyList<SessionSpeakerRequest>? Speakers);

// Responses.

public sealed record RoomDto(Guid Id, string Name, string ColorHex, int SortOrder, bool IsActive);

public sealed record SessionSpeakerDto(Guid SpeakerId, string Name, string Role, int SortOrder, bool Tentative);

public sealed record SessionDto(
    Guid Id, string Title, string Kind, DateTimeOffset StartsAt, int DurationMinutes,
    string Time, Guid? OwnerCommitteeId, bool IsPublic, string? Notes,
    IReadOnlyList<RoomDto> Rooms, IReadOnlyList<SessionSpeakerDto> Speakers, bool Tentative);

public sealed record AgendaSlotDto(string Time, IReadOnlyList<SessionDto> Sessions);

public sealed record AgendaDayDto(DateOnly Date, string Heading, int SessionCount, int TentativeSpeakers, IReadOnlyList<AgendaSlotDto> Slots);

public sealed record AgendaDto(
    Guid EventId, string EventName, IReadOnlyList<RoomDto> Rooms, IReadOnlyList<AgendaDayDto> Days, bool CanManage);
