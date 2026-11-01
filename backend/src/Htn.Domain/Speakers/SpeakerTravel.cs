using Htn.Domain.Common;

namespace Htn.Domain.Speakers;

/// <summary>What the speaker submitted, adapted to the event's travel mode.</summary>
public sealed record TravelSubmission(
    DateOnly? ArrivalDate,
    DateOnly? DepartureDate,
    string? FlyingFrom,
    string? PreferredArrivalTime,
    string? IdFullName,
    string? BusFrom,
    string? BusEta,
    bool NeedsTerminalPickup,
    string? RoomNote,
    string? NeedsNotes,
    string ConsentVersion);

/// <summary>
/// A speaker's travel details (frame 22/23), 1:1 with <see cref="Speaker"/> and kept in its own table so the
/// 30-day purge and the admin/Programs-lead-only read can be surgical. The ID name and flight details
/// (<see cref="FlyingFrom"/>, <see cref="PreferredArrivalTime"/>, <see cref="IdFullName"/>) are nulled 30 days
/// after the event by <c>SpeakerDataRetentionWorker</c>; once purged they can never be written again.
/// </summary>
public sealed class SpeakerTravel
{
    public const int PlaceMaxLength = 120;
    public const int TimeTextMaxLength = 40;
    public const int IdNameMaxLength = 160;
    public const int RoomNoteMaxLength = 120;
    public const int NeedsNotesMaxLength = 1000;
    public const int ConsentVersionMaxLength = 20;
    public const string CurrentConsentVersion = "2026-10";

    private SpeakerTravel() { }

    public Guid SpeakerId { get; private set; }
    public Guid EventId { get; private set; }
    public DateOnly? ArrivalDate { get; private set; }
    public DateOnly? DepartureDate { get; private set; }

    /// <summary>Purged 30 days after the event.</summary>
    public string? FlyingFrom { get; private set; }

    /// <summary>Purged 30 days after the event.</summary>
    public string? PreferredArrivalTime { get; private set; }

    /// <summary>Purged 30 days after the event: the only reason this table keeps a government-ID name at all.</summary>
    public string? IdFullName { get; private set; }

    public string? BusFrom { get; private set; }
    public string? BusEta { get; private set; }
    public bool NeedsTerminalPickup { get; private set; }
    public string? RoomNote { get; private set; }
    public string? NeedsNotes { get; private set; }
    public DateTimeOffset? ConsentAt { get; private set; }
    public string? ConsentVersion { get; private set; }
    public DateTimeOffset? SubmittedAt { get; private set; }
    public DateTimeOffset? LastSubmittedAt { get; private set; }
    public DateTimeOffset? PurgedAt { get; private set; }

    public bool IsPurged => PurgedAt is not null;
    public bool HasSubmitted => SubmittedAt is not null;

    public static SpeakerTravel Create(Guid speakerId, Guid eventId) => new()
    {
        SpeakerId = speakerId,
        EventId = eventId,
    };

    /// <summary>Records a (re)submission from the public form. The speaker may submit again any time before the purge.</summary>
    public void Submit(TravelSubmission data, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (IsPurged) throw new DomainException("This event's travel window has closed.");
        if (data.ArrivalDate is { } a && data.DepartureDate is { } d && d < a)
            throw new DomainException("Departure must be on or after arrival.");
        ArrivalDate = data.ArrivalDate;
        DepartureDate = data.DepartureDate;
        FlyingFrom = Optional(data.FlyingFrom, "Flying from", PlaceMaxLength);
        PreferredArrivalTime = Optional(data.PreferredArrivalTime, "Preferred arrival time", TimeTextMaxLength);
        IdFullName = Optional(data.IdFullName, "Name as on ID", IdNameMaxLength);
        BusFrom = Optional(data.BusFrom, "Departing from", PlaceMaxLength);
        BusEta = Optional(data.BusEta, "Estimated arrival", TimeTextMaxLength);
        NeedsTerminalPickup = data.NeedsTerminalPickup;
        RoomNote = Optional(data.RoomNote, "Room note", RoomNoteMaxLength);
        NeedsNotes = Optional(data.NeedsNotes, "Dietary or accessibility notes", NeedsNotesMaxLength);
        var version = Required(data.ConsentVersion, "Consent version", ConsentVersionMaxLength);
        ConsentAt ??= at;
        ConsentVersion ??= version;
        SubmittedAt ??= at;
        LastSubmittedAt = at;
    }

    /// <summary>Staff-side edit of the non-sensitive fields (arrival/departure, room note).</summary>
    public void UpdateByStaff(DateOnly? arrivalDate, DateOnly? departureDate, string? roomNote, DateTimeOffset at)
    {
        if (IsPurged) throw new DomainException("This event's travel window has closed.");
        if (arrivalDate is { } a && departureDate is { } d && d < a)
            throw new DomainException("Departure must be on or after arrival.");
        ArrivalDate = arrivalDate;
        DepartureDate = departureDate;
        RoomNote = Optional(roomNote, "Room note", RoomNoteMaxLength);
        LastSubmittedAt = at;
    }

    /// <summary>Nulls the flight and ID-name fields 30 days after the event. Idempotent; never reversible.</summary>
    public (bool HadIdName, bool HadFlyingFrom, bool HadPreferredArrivalTime) Purge(DateTimeOffset at)
    {
        if (IsPurged) return (false, false, false);
        var had = (IdFullName is not null, FlyingFrom is not null, PreferredArrivalTime is not null);
        IdFullName = null;
        FlyingFrom = null;
        PreferredArrivalTime = null;
        PurgedAt = at;
        return had;
    }

    private static string? Optional(string? value, string field, int max)
    {
        var v = value?.Trim();
        if (string.IsNullOrEmpty(v)) return null;
        if (v.Length > max) throw new DomainException($"{field} must be at most {max} characters.");
        return v;
    }

    private static string Required(string? value, string field, int max)
    {
        var v = value?.Trim();
        if (string.IsNullOrEmpty(v)) throw new DomainException($"{field} is required.");
        if (v.Length > max) throw new DomainException($"{field} must be at most {max} characters.");
        return v;
    }
}
