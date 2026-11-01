using Htn.Domain.Invitations;

namespace Htn.Api.Features.Invitations;

// Requests: every property nullable so FluentValidation, not the model binder, produces the message.

public sealed record InvitationTemplateRequest(string? Subject, string? Body);

public sealed record CreateInvitationsRequest(Guid[]? OrgIds);

public sealed record InvitationStatusRequest(InvitationStatus? Status, string? Note);

// Responses.

public sealed record EventInvitationTemplateDto(Guid EventId, string Subject, string Body, DateTimeOffset UpdatedAt);

public sealed record InvitationListItemDto(
    Guid Id, Guid DirectoryOrgId, string OrgName, string OrgKind, InvitationStatus Status,
    DateTimeOffset? SentAt, DateTimeOffset? LastStatusAt, bool PrimaryContactVerified);

public sealed record CreateInvitationsResultDto(IReadOnlyList<Guid> Created, IReadOnlyList<Guid> Skipped);

public sealed record InvitationPreviewDto(string Subject, string Body);

public sealed record InvitationEventDto(Guid Id, InvitationStatus Kind, DateTimeOffset At, Guid? ById, string? Note);

public sealed record InvitationDetailDto(
    Guid Id, Guid EventId, Guid DirectoryOrgId, string OrgName, InvitationStatus Status, Guid? ContactId,
    DateTimeOffset? SentAt, DateTimeOffset? LastStatusAt, string? LetterSubjectSnapshot, string? LetterBodySnapshot,
    IReadOnlyList<InvitationEventDto> History);
