using Htn.Domain.Common;

namespace Htn.Domain.Speakers;

/// <summary>
/// A speaker's plus-one (frame 23): stays free in the speaker's shared room, but must register for a ticket, so a
/// single-use 50%-off voucher is auto-issued to them on submit. Removed with a reason — never deleted — and a
/// changed name becomes a new row (the old one keeps the history the voucher it issued belongs to).
/// </summary>
public sealed class SpeakerPlusOne
{
    public const int NameMaxLength = 120;
    public const int NameSegmentMaxLength = 10;
    public const int EmailMaxLength = 254;
    public const int ReasonMaxLength = 500;

    private SpeakerPlusOne() { }

    public Guid Id { get; private set; }
    public Guid SpeakerId { get; private set; }
    public Guid EventId { get; private set; }
    public string Name { get; private set; } = default!;

    /// <summary>The transliterated fragment baked into the voucher code, e.g. "GOMEZ".</summary>
    public string NameSegment { get; private set; } = default!;

    public string Email { get; private set; } = default!;
    public bool SharesRoom { get; private set; }
    public Guid? VoucherId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? RemovedAt { get; private set; }
    public Guid? RemovedById { get; private set; }
    public string? RemoveReason { get; private set; }

    public bool IsRemoved => RemovedAt is not null;

    public static SpeakerPlusOne Create(
        Guid speakerId, Guid eventId, string name, string nameSegment, string email, bool sharesRoom, DateTimeOffset at)
    {
        if (speakerId == Guid.Empty) throw new DomainException("A speaker is required.");
        var cleanName = Required(name, "Name", NameMaxLength);
        var segment = nameSegment?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(segment) || segment.Length > NameSegmentMaxLength)
            throw new DomainException("Invalid name segment.");
        var cleanEmail = email?.Trim() ?? "";
        if (cleanEmail.Length == 0 || cleanEmail.Length > EmailMaxLength || !cleanEmail.Contains('@', StringComparison.Ordinal))
            throw new DomainException("A valid email is required.");
        return new SpeakerPlusOne
        {
            Id = Guid.CreateVersion7(at),
            SpeakerId = speakerId,
            EventId = eventId,
            Name = cleanName,
            NameSegment = segment,
            Email = cleanEmail,
            SharesRoom = sharesRoom,
            CreatedAt = at,
        };
    }

    public void AttachVoucher(Guid voucherId)
    {
        if (VoucherId is not null) throw new DomainException("This plus-one already has a voucher.");
        VoucherId = voucherId;
    }

    public void Remove(string reason, Guid actorId, DateTimeOffset at)
    {
        if (IsRemoved) throw new DomainException("This plus-one is already removed.");
        RemoveReason = Required(reason, "Reason", ReasonMaxLength);
        RemovedAt = at;
        RemovedById = actorId;
    }

    private static string Required(string? value, string field, int max)
    {
        var v = value?.Trim();
        if (string.IsNullOrEmpty(v)) throw new DomainException($"{field} is required.");
        if (v.Length > max) throw new DomainException($"{field} must be at most {max} characters.");
        return v;
    }
}
