namespace Htn.Domain.Invitations;

/// <summary>The editable fields of an event's invitation letter template.</summary>
public sealed record NewEventInvitationTemplate(string Subject, string Body);

/// <summary>
/// One per event: the letter subject/body used by <see cref="InvitationMerge"/> to build the merged preview and,
/// at send time, the frozen snapshot on an <see cref="EventInvitation"/>. The body carries the literal merge
/// tokens <c>[date]</c>, <c>[addressee.name]</c>, <c>[addressee.position]</c>, <c>[addressee.salutation]</c>, and
/// blank lines mark paragraph breaks (the email template wraps each paragraph in its own <c>&lt;p&gt;</c>, HTML-encoded).
/// Unlike the other Phase 7 entities, this is a mutable per-event settings row — like <c>EventPartnerSettings</c> —
/// so it carries no append-only guard at the database.
/// </summary>
public sealed class EventInvitationTemplate
{
    public const int SubjectMaxLength = 200;
    public const int BodyMaxLength = 8000;

    private EventInvitationTemplate() { }

    public Guid Id { get; private set; }
    public Guid EventId { get; private set; }
    public string Subject { get; private set; } = default!;
    public string Body { get; private set; } = default!;
    public DateTimeOffset UpdatedAt { get; private set; }
    public Guid? UpdatedById { get; private set; }

    public static EventInvitationTemplate Create(Guid eventId, NewEventInvitationTemplate data, Guid actorId, DateTimeOffset at)
    {
        if (eventId == Guid.Empty) throw new Common.DomainException("An event is required.");
        var template = new EventInvitationTemplate { Id = Guid.CreateVersion7(at), EventId = eventId };
        template.Update(data, actorId, at);
        return template;
    }

    public void Update(NewEventInvitationTemplate data, Guid actorId, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(data);
        Subject = InvitationRules.Required(data.Subject, "Subject", SubjectMaxLength);
        Body = InvitationRules.Required(data.Body, "Body", BodyMaxLength);
        UpdatedAt = at;
        UpdatedById = actorId;
    }
}
