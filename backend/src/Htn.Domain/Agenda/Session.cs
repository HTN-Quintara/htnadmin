using Htn.Domain.Common;

namespace Htn.Domain.Agenda;

/// <summary>The editable fields of a session, shared by create and update (rooms and speakers are set separately).</summary>
public sealed record NewSession(
    string Title,
    SessionKind Kind,
    DateTimeOffset StartsAt,
    int DurationMinutes,
    Guid? OwnerCommitteeId,
    bool IsPublic,
    string? Notes);

/// <summary>
/// One slot in the programme (frame 16): a title, a kind, a start time and a duration, in one or more rooms, with
/// zero or more speakers. Day headings and time slots are derived from <see cref="StartsAt"/> in Manila; nothing
/// about the calendar is stored here. A session is the one thing in Phase 6 that is hard-deleted (with an audit
/// entry) — it holds no financial, registration or sponsor data.
/// </summary>
public sealed class Session
{
    public const int TitleMaxLength = 160;
    public const int NotesMaxLength = 500;
    public const int MinDuration = 5;
    public const int MaxDuration = 600;

    private Session() { }

    public Guid Id { get; private set; }
    public Guid EventId { get; private set; }
    public string Title { get; private set; } = default!;
    public SessionKind Kind { get; private set; }
    public DateTimeOffset StartsAt { get; private set; }
    public int DurationMinutes { get; private set; }
    public Guid? OwnerCommitteeId { get; private set; }
    public bool IsPublic { get; private set; }
    public string? Notes { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public Guid? CreatedById { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public Guid? UpdatedById { get; private set; }

    public DateTimeOffset EndsAt => StartsAt.AddMinutes(DurationMinutes);

    public static Session Create(Guid eventId, NewSession data, Guid actorId, DateTimeOffset at)
    {
        if (eventId == Guid.Empty) throw new DomainException("An event is required.");
        var session = new Session
        {
            Id = Guid.CreateVersion7(at),
            EventId = eventId,
            CreatedAt = at,
            CreatedById = actorId,
            UpdatedAt = at,
            UpdatedById = actorId,
        };
        session.Apply(data, actorId, at);
        return session;
    }

    public void Update(NewSession data, Guid actorId, DateTimeOffset at) => Apply(data, actorId, at);

    private void Apply(NewSession data, Guid actorId, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (!Enum.IsDefined(data.Kind)) throw new DomainException("Unknown session type.");
        var title = data.Title?.Trim();
        if (string.IsNullOrEmpty(title)) throw new DomainException("A title is required.");
        if (title.Length > TitleMaxLength) throw new DomainException($"Title must be at most {TitleMaxLength} characters.");
        if (data.DurationMinutes < MinDuration || data.DurationMinutes > MaxDuration)
            throw new DomainException($"Duration must be between {MinDuration} and {MaxDuration} minutes.");
        var notes = data.Notes?.Trim();
        if (notes is { Length: > NotesMaxLength }) throw new DomainException($"Notes must be at most {NotesMaxLength} characters.");
        Title = title;
        Kind = data.Kind;
        StartsAt = data.StartsAt.ToUniversalTime();
        DurationMinutes = data.DurationMinutes;
        OwnerCommitteeId = data.OwnerCommitteeId == Guid.Empty ? null : data.OwnerCommitteeId;
        IsPublic = data.IsPublic;
        Notes = string.IsNullOrEmpty(notes) ? null : notes;
        UpdatedAt = at;
        UpdatedById = actorId;
    }
}

/// <summary>A room a session runs in. "Both rooms" is two of these. Link row: edited and deleted freely.</summary>
public sealed class SessionRoom
{
    private SessionRoom() { }

    public Guid SessionId { get; private set; }
    public Guid RoomId { get; private set; }
    public Guid EventId { get; private set; }

    public static SessionRoom Create(Guid sessionId, Guid roomId, Guid eventId) => new()
    {
        SessionId = sessionId,
        RoomId = roomId,
        EventId = eventId,
    };
}

/// <summary>A speaker in a session, with their role. A panel is several Panellists plus one Moderator.</summary>
public sealed class SessionSpeaker
{
    private SessionSpeaker() { }

    public Guid SessionId { get; private set; }
    public Guid SpeakerId { get; private set; }
    public Guid EventId { get; private set; }
    public SessionSpeakerRole Role { get; private set; }
    public int SortOrder { get; private set; }

    public static SessionSpeaker Create(Guid sessionId, Guid speakerId, Guid eventId, SessionSpeakerRole role, int sortOrder)
    {
        if (!Enum.IsDefined(role)) throw new DomainException("Unknown speaker role.");
        return new SessionSpeaker { SessionId = sessionId, SpeakerId = speakerId, EventId = eventId, Role = role, SortOrder = sortOrder };
    }
}
