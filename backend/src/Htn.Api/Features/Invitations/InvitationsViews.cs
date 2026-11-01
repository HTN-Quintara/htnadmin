using Htn.Domain.Invitations;

namespace Htn.Api.Features.Invitations;

/// <summary>Projections from the invitations entities to the DTOs the screen renders.</summary>
public static class InvitationsViews
{
    public static EventInvitationTemplateDto Template(EventInvitationTemplate t) =>
        new(t.EventId, t.Subject, t.Body, t.UpdatedAt);

    public static InvitationEventDto Event(InvitationEvent e) => new(e.Id, e.Kind, e.At, e.ById, e.Note);

    public static InvitationDetailDto Detail(EventInvitation inv, string orgName, IReadOnlyList<InvitationEvent> history) =>
        new(inv.Id, inv.EventId, inv.DirectoryOrgId, orgName, inv.Status, inv.ContactId, inv.SentAt, inv.LastStatusAt,
            inv.LetterSubjectSnapshot, inv.LetterBodySnapshot, [.. history.OrderByDescending(h => h.At).Select(Event)]);
}
