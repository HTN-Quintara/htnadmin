using Htn.Domain.Leads;

namespace Htn.Api.Features.Leads;

public sealed record LeadRequest(
    LeadKind? Kind, string? Subject, string? ContactName, string? ContactRole, string? ContactEmail,
    string? ContactMobile, Guid? AskTierId, decimal? AskAmount, string? AskNote, Guid? OwnerPersonId,
    string? NextStep, DateOnly? NextStepDue);

public sealed record LeadStatusRequest(LeadStatus? Status, string? Reason);

public sealed record LeadReasonRequest(string? Reason);

// Responses.

public sealed record LeadCardDto(
    Guid Id, LeadKind Kind, LeadStatus Status, string Subject,
    string? ContactName, string? ContactRole, string? ContactEmail, string? ContactMobile,
    Guid? AskTierId, string? AskTierName, decimal? AskAmount, string? AskNote, string Ask,
    Guid? OwnerPersonId, string? OwnerName, string? OwnerInitials,
    string? NextStep, DateOnly? NextStepDue, bool Overdue, bool FromDirectory,
    Guid? SponsorId, Guid? SpeakerId);

public sealed record LeadColumnDto(LeadStatus Status, string Name, int Count, IReadOnlyList<LeadCardDto> Cards);

public sealed record LeadStatDto(string Label, string Value);

public sealed record LeadsBoardDto(LeadKind Kind, IReadOnlyList<LeadStatDto> Stats, IReadOnlyList<LeadColumnDto> Columns);

public sealed record SpeakerSummaryDto(Guid Id, string Name, string Status);

public sealed record ConfirmLeadResultDto(Guid LeadId, Guid? SponsorId, Guid? SpeakerId);
