using Htn.Domain.Directory;

namespace Htn.Api.Features.Directory;

// Requests: every property nullable so FluentValidation, not the model binder, produces the message.

public sealed record DirectoryOrgRequest(
    DirectoryOrgKind? Kind, string? Name, string? AddresseeSalutation, string? WebsiteUrl, string? Notes);

public sealed record DirectoryOrgUpdateRequest(string? AddresseeSalutation, string? WebsiteUrl, string? Notes);

public sealed record ReasonRequest(string? Reason);

public sealed record DirectoryContactRequest(
    string? Position, string? HolderName, string? Email, string? Phone, bool? IsPrimary, string? CcEmails);

public sealed record HeadChangedRequest(string? Position, string? HolderName, string? Email, string? Phone);

// Responses.

public sealed record DirectoryContactDto(
    Guid Id, Guid OrgId, string Position, string HolderName, string Email, string? Phone,
    bool IsPrimary, DateTimeOffset? VerifiedAt, bool NeedsCheck, IReadOnlyList<string> CcEmails);

public sealed record DirectoryContactHistoryDto(
    Guid Id, Guid? ContactId, DirectoryContactHistoryKind Kind, DateTimeOffset At, Guid? ById,
    string? Note, string? PriorPosition, string? PriorHolderName);

public sealed record DirectoryOrgDto(
    Guid Id, DirectoryOrgKind Kind, string Name, string NameKey, string? AddresseeSalutation,
    string? WebsiteUrl, Guid? LogoFileId, string? Notes, bool IsActive, bool NeedsCheck);

public sealed record DirectoryOrgDetailDto(
    DirectoryOrgDto Org, IReadOnlyList<DirectoryContactDto> Contacts, IReadOnlyList<DirectoryContactHistoryDto> History);
