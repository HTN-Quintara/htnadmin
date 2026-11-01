namespace Htn.Domain.Speakers;

/// <summary>What happened to a speaker link, for screen 22's "Link access log". Append-only; never the OTP, the raw token, or the session token.</summary>
public enum SpeakerLinkAccessKind
{
    LinkSent,
    Opened,
    CodeSent,
    CodeVerified,
    CodeFailed,
    CodeLocked,
    Submitted,
    Revoked,
}

/// <summary>
/// One row of the speaker link's access log. <see cref="ClientHint"/> is a coarse platform+browser label
/// ("iPhone, Safari"), never the raw user-agent; <see cref="IpHint"/> is the same masked key the rest of the system
/// uses for rate limiting, never a full address. Insert-only.
/// </summary>
public sealed class SpeakerLinkAccess
{
    public const int ClientHintMaxLength = 60;
    public const int IpHintMaxLength = 45;
    public const int NoteMaxLength = 200;

    private SpeakerLinkAccess() { }

    public Guid Id { get; private set; }
    public Guid LinkId { get; private set; }
    public Guid SpeakerId { get; private set; }
    public Guid EventId { get; private set; }
    public DateTimeOffset At { get; private set; }
    public SpeakerLinkAccessKind Kind { get; private set; }
    public Guid? ActorUserId { get; private set; }
    public string? ClientHint { get; private set; }
    public string? IpHint { get; private set; }
    public string? Note { get; private set; }

    public static SpeakerLinkAccess Create(
        Guid linkId, Guid speakerId, Guid eventId, SpeakerLinkAccessKind kind, DateTimeOffset at,
        Guid? actorUserId = null, string? clientHint = null, string? ipHint = null, string? note = null) => new()
    {
        Id = Guid.CreateVersion7(at),
        LinkId = linkId,
        SpeakerId = speakerId,
        EventId = eventId,
        At = at,
        Kind = kind,
        ActorUserId = actorUserId,
        ClientHint = Truncate(clientHint, ClientHintMaxLength),
        IpHint = Truncate(ipHint, IpHintMaxLength),
        Note = Truncate(note, NoteMaxLength),
    };

    private static string? Truncate(string? value, int max) =>
        string.IsNullOrEmpty(value) ? null : value.Length > max ? value[..max] : value;
}
