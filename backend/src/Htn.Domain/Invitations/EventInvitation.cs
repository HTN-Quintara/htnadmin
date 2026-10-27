using Htn.Domain.Common;
using Htn.Domain.Directory;

namespace Htn.Domain.Invitations;

/// <summary>
/// One row per (event, directory org) — the per-event invitation pick list and its tracked outcome. Lightweight on
/// purpose: unlike a sponsor or lead, this isn't deactivated/voided the way an org record is. Deleting a row is
/// allowed only while it is still <see cref="InvitationStatus.NotSent"/> (undoing a pick-list mistake before
/// anything irreversible happened); once a letter has gone out, "every send is logged" means the row is permanent —
/// the database guard (<c>Invitations_Guards</c>) enforces this too.
/// <para>
/// <see cref="ContactId"/> is a nullable snapshot of which live <c>DirectoryContact</c> the letter was sent to —
/// not a hard FK requirement, because a contact can later be voided (replaced, removed) without that making the
/// historical "who we actually emailed" record disappear or dangle. The contact's identity at send time is what
/// matters here, not whatever the contact row says today, which is why the subject/body are also frozen as
/// <see cref="LetterSubjectSnapshot"/>/<see cref="LetterBodySnapshot"/> rather than re-merged on read.
/// </para>
/// </summary>
public sealed class EventInvitation
{
    public const int SubjectMaxLength = EventInvitationTemplate.SubjectMaxLength;
    public const int BodyMaxLength = EventInvitationTemplate.BodyMaxLength;
    public const int NoteMaxLength = 500;

    /// <summary>
    /// The transition whitelist (Phase 7B decision): any non-terminal status may move forward to any later status
    /// except back to <see cref="InvitationStatus.NotSent"/>. <see cref="InvitationStatus.Confirmed"/> and
    /// <see cref="InvitationStatus.Declined"/> are terminal. <see cref="InvitationStatus.Bounced"/> is the one
    /// exception to "terminal-ish": a bounce re-flags the contact (see the endpoint), and once staff fix the
    /// organization's contact info they can manually clear it back to <see cref="InvitationStatus.Sent"/> to retry —
    /// so Bounced is reachable from anywhere live, and can itself move forward again.
    /// </summary>
    private static readonly Dictionary<InvitationStatus, InvitationStatus[]> Transitions = new()
    {
        [InvitationStatus.Sent] = [InvitationStatus.Opened, InvitationStatus.Replied, InvitationStatus.Confirmed, InvitationStatus.Declined, InvitationStatus.Bounced],
        [InvitationStatus.Opened] = [InvitationStatus.Replied, InvitationStatus.Confirmed, InvitationStatus.Declined, InvitationStatus.Bounced],
        [InvitationStatus.Replied] = [InvitationStatus.Confirmed, InvitationStatus.Declined, InvitationStatus.Bounced],
        [InvitationStatus.Bounced] = [InvitationStatus.Sent],
    };

    private EventInvitation() { }

    public Guid Id { get; private set; }
    public Guid EventId { get; private set; }
    public Guid DirectoryOrgId { get; private set; }

    /// <summary>The contact the letter was actually sent to; null until <see cref="Send"/>. See the class remarks.</summary>
    public Guid? ContactId { get; private set; }

    public InvitationStatus Status { get; private set; }
    public DateTimeOffset? SentAt { get; private set; }
    public Guid? SentById { get; private set; }
    public DateTimeOffset? LastStatusAt { get; private set; }

    /// <summary>The fully-merged subject as actually sent. Frozen once set; never re-merged on read.</summary>
    public string? LetterSubjectSnapshot { get; private set; }

    /// <summary>The fully-merged body as actually sent. Frozen once set.</summary>
    public string? LetterBodySnapshot { get; private set; }

    /// <summary>
    /// The contact's email/holder name at the moment of send, frozen alongside the letter. The outbox delivers to
    /// this, never a live re-join of <see cref="ContactId"/> — otherwise an edit to the contact row between enqueue
    /// and delivery could redirect, or re-address, a letter whose body already claims it went to someone else.
    /// </summary>
    public string? RecipientEmail { get; private set; }
    public string? RecipientName { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public Guid? CreatedById { get; private set; }

    public bool IsNotSent => Status == InvitationStatus.NotSent;

    public static EventInvitation Create(Guid eventId, Guid directoryOrgId, Guid actorId, DateTimeOffset at)
    {
        if (eventId == Guid.Empty) throw new DomainException("An event is required.");
        if (directoryOrgId == Guid.Empty) throw new DomainException("An organization is required.");
        return new EventInvitation
        {
            Id = Guid.CreateVersion7(at),
            EventId = eventId,
            DirectoryOrgId = directoryOrgId,
            Status = InvitationStatus.NotSent,
            CreatedAt = at,
            CreatedById = actorId,
        };
    }

    /// <summary>
    /// Sends the letter. <paramref name="contactIsVerified"/> is precomputed by the caller against the live
    /// <c>DirectoryContact</c> state — this entity doesn't own that data, so it stays a dumb, re-checkable gate
    /// rather than a stale cached flag.
    /// </summary>
    public void Send(
        Guid contactId, string recipientEmail, string recipientName, string mergedSubject, string mergedBody,
        bool contactIsVerified, Guid actorId, DateTimeOffset at)
    {
        if (Status != InvitationStatus.NotSent) throw new DomainException("This invitation has already been sent.");
        if (!contactIsVerified) throw new DomainException("The organization's contact must be verified before sending.");
        if (contactId == Guid.Empty) throw new DomainException("A contact is required.");
        ContactId = contactId;
        RecipientEmail = InvitationRules.Required(recipientEmail, "Recipient email", DirectoryContact.EmailMaxLength);
        RecipientName = InvitationRules.Required(recipientName, "Recipient name", DirectoryContact.HolderNameMaxLength);
        LetterSubjectSnapshot = InvitationRules.Required(mergedSubject, "Subject", SubjectMaxLength);
        LetterBodySnapshot = InvitationRules.Required(mergedBody, "Body", BodyMaxLength);
        Status = InvitationStatus.Sent;
        SentAt = at;
        SentById = actorId;
        LastStatusAt = at;
    }

    /// <summary>
    /// Manually advances the tracked status through <see cref="Transitions"/>. Throws on an unknown or disallowed
    /// move. The actor and note are recorded by the caller on the append-only <see cref="InvitationEvent"/> row,
    /// not here — this entity only remembers the current state and when it last changed.
    /// </summary>
    public void AdvanceStatus(InvitationStatus newStatus, DateTimeOffset at, string? note = null)
    {
        if (!Enum.IsDefined(newStatus)) throw new DomainException("Unknown status.");
        if (!Transitions.TryGetValue(Status, out var allowed) || !allowed.Contains(newStatus))
            throw new DomainException($"Cannot move an invitation from {Status} to {newStatus}.");
        _ = InvitationRules.Optional(note, "Note", NoteMaxLength);
        Status = newStatus;
        LastStatusAt = at;
    }
}
