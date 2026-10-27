using FluentValidation;
using Htn.Domain.Leads;

namespace Htn.Api.Features.Leads;

public sealed class LeadRequestValidator : AbstractValidator<LeadRequest>
{
    public LeadRequestValidator()
    {
        RuleFor(x => x.Kind).NotNull().IsInEnum();
        RuleFor(x => x.Subject).NotEmpty().MaximumLength(Lead.SubjectMaxLength);
        RuleFor(x => x.ContactName).MaximumLength(Lead.ContactNameMaxLength);
        RuleFor(x => x.ContactRole).MaximumLength(Lead.ContactRoleMaxLength);
        RuleFor(x => x.ContactEmail).EmailAddress().MaximumLength(Lead.EmailMaxLength)
            .When(x => !string.IsNullOrWhiteSpace(x.ContactEmail));
        RuleFor(x => x.ContactMobile).MaximumLength(Lead.MobileMaxLength);
        RuleFor(x => x.AskAmount).GreaterThan(0).When(x => x.AskAmount is not null);
        RuleFor(x => x.AskNote).MaximumLength(Lead.AskNoteMaxLength);
        RuleFor(x => x.NextStep).MaximumLength(Lead.NextStepMaxLength);
    }
}

public sealed class LeadStatusRequestValidator : AbstractValidator<LeadStatusRequest>
{
    public LeadStatusRequestValidator()
    {
        RuleFor(x => x.Status).NotNull().IsInEnum();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(Lead.ReasonMaxLength)
            .When(x => x.Status == LeadStatus.Declined);
    }
}

public sealed record SpeakerCreateRequest(string? Name, string? Email, string? Affiliation);

public sealed class SpeakerCreateRequestValidator : AbstractValidator<SpeakerCreateRequest>
{
    public SpeakerCreateRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(254);
        RuleFor(x => x.Affiliation).MaximumLength(120);
    }
}
