namespace Htn.Domain.Invitations;

/// <summary>
/// The lifecycle of one <see cref="EventInvitation"/>. "Manual CRM-style" (Phase 7B): only <see cref="Sent"/> is
/// set automatically, by the send endpoint. Every later move is an explicit staff action via
/// <see cref="EventInvitation.AdvanceStatus"/> — there is no webhook that flips these.
/// </summary>
public enum InvitationStatus
{
    /// <summary>The organization has been picked for this event, but no letter has gone out yet.</summary>
    NotSent,

    /// <summary>The letter was sent to the contact that was verified at send time.</summary>
    Sent,

    Opened,
    Replied,

    /// <summary>Terminal: the organization confirmed.</summary>
    Confirmed,

    /// <summary>Terminal: the organization declined.</summary>
    Declined,

    /// <summary>
    /// The email bounced. Not terminal: once the organization's contact info is fixed, staff can clear this back
    /// to <see cref="Sent"/> and try again (see <see cref="EventInvitation.AdvanceStatus"/> for the whitelist).
    /// </summary>
    Bounced,
}
