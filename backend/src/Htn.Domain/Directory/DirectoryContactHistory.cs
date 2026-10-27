using Htn.Domain.Common;

namespace Htn.Domain.Directory;

/// <summary>
/// An append-only trail of what happened to a <see cref="DirectoryOrg"/>'s contacts: "still correct" confirmations
/// and holder changes (and, in future, verification and bounce events). A <see cref="DirectoryContactHistoryKind.HolderChanged"/>
/// row snapshots the prior <see cref="PriorPosition"/>/<see cref="PriorHolderName"/> so the history reads on its own,
/// even after the contact row has moved on again. Never updated or deleted (guarded again at the database).
/// </summary>
public sealed class DirectoryContactHistory
{
    public const int NoteMaxLength = 500;
    public const int PositionMaxLength = DirectoryContact.PositionMaxLength;
    public const int HolderNameMaxLength = DirectoryContact.HolderNameMaxLength;

    private DirectoryContactHistory() { }

    public Guid Id { get; private set; }
    public Guid OrgId { get; private set; }
    public Guid? ContactId { get; private set; }
    public DirectoryContactHistoryKind Kind { get; private set; }
    public DateTimeOffset At { get; private set; }
    public Guid? ById { get; private set; }
    public string? Note { get; private set; }
    public string? PriorPosition { get; private set; }
    public string? PriorHolderName { get; private set; }

    public static DirectoryContactHistory StillCorrect(Guid orgId, Guid contactId, Guid actorId, DateTimeOffset at, string? note = null) =>
        Create(orgId, contactId, DirectoryContactHistoryKind.StillCorrect, actorId, at, note, null, null);

    public static DirectoryContactHistory HolderChanged(
        Guid orgId, Guid contactId, HolderChangeSnapshot prior, Guid actorId, DateTimeOffset at, string? note = null) =>
        Create(orgId, contactId, DirectoryContactHistoryKind.HolderChanged, actorId, at, note,
            prior.PriorPosition, prior.PriorHolderName);

    public static DirectoryContactHistory Bounced(Guid orgId, Guid contactId, Guid? actorId, DateTimeOffset at, string? note = null) =>
        Create(orgId, contactId, DirectoryContactHistoryKind.Bounced, actorId, at, note, null, null);

    private static DirectoryContactHistory Create(
        Guid orgId, Guid? contactId, DirectoryContactHistoryKind kind, Guid? actorId, DateTimeOffset at,
        string? note, string? priorPosition, string? priorHolderName)
    {
        if (orgId == Guid.Empty) throw new DomainException("An organization is required.");
        if (kind == DirectoryContactHistoryKind.HolderChanged
            && (string.IsNullOrWhiteSpace(priorPosition) || string.IsNullOrWhiteSpace(priorHolderName)))
            throw new DomainException("A holder-changed history row needs the prior position and holder name.");
        return new DirectoryContactHistory
        {
            Id = Guid.CreateVersion7(at),
            OrgId = orgId,
            ContactId = contactId,
            Kind = kind,
            At = at,
            ById = actorId,
            Note = DirectoryRules.Optional(note, "Note", NoteMaxLength),
            PriorPosition = priorPosition,
            PriorHolderName = priorHolderName,
        };
    }
}
