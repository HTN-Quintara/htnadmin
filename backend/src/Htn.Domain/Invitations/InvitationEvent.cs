using Htn.Domain.Common;

namespace Htn.Domain.Invitations;

/// <summary>
/// An append-only row logging that one <see cref="EventInvitation"/> reached a given <see cref="InvitationStatus"/>
/// at a given time — the per-event "every send is logged" trail. Never updated or deleted (guarded again at the
/// database, like <c>DirectoryContactHistory</c>).
/// </summary>
public sealed class InvitationEvent
{
    public const int NoteMaxLength = 500;

    private InvitationEvent() { }

    public Guid Id { get; private set; }
    public Guid InvitationId { get; private set; }
    public InvitationStatus Kind { get; private set; }
    public DateTimeOffset At { get; private set; }
    public Guid? ById { get; private set; }
    public string? Note { get; private set; }

    public static InvitationEvent Create(Guid invitationId, InvitationStatus kind, Guid? actorId, DateTimeOffset at, string? note = null)
    {
        if (invitationId == Guid.Empty) throw new DomainException("An invitation is required.");
        if (!Enum.IsDefined(kind)) throw new DomainException("Unknown status.");
        return new InvitationEvent
        {
            Id = Guid.CreateVersion7(at),
            InvitationId = invitationId,
            Kind = kind,
            At = at,
            ById = actorId,
            Note = InvitationRules.Optional(note, "Note", NoteMaxLength),
        };
    }
}
