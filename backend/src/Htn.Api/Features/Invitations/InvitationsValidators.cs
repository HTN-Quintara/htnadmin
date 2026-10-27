using FluentValidation;
using Htn.Domain.Invitations;

namespace Htn.Api.Features.Invitations;

public sealed class InvitationTemplateRequestValidator : AbstractValidator<InvitationTemplateRequest>
{
    public InvitationTemplateRequestValidator()
    {
        RuleFor(x => x.Subject).NotEmpty().MaximumLength(EventInvitationTemplate.SubjectMaxLength);
        RuleFor(x => x.Body).NotEmpty().MaximumLength(EventInvitationTemplate.BodyMaxLength);
    }
}

public sealed class CreateInvitationsRequestValidator : AbstractValidator<CreateInvitationsRequest>
{
    public CreateInvitationsRequestValidator()
    {
        RuleFor(x => x.OrgIds).NotNull().Must(ids => ids!.Length > 0).WithMessage("Pick at least one organization.");
    }
}

public sealed class InvitationStatusRequestValidator : AbstractValidator<InvitationStatusRequest>
{
    public InvitationStatusRequestValidator()
    {
        RuleFor(x => x.Status).NotNull().IsInEnum()
            .Must(s => s != InvitationStatus.NotSent).WithMessage("Cannot move an invitation back to NotSent.");
        RuleFor(x => x.Note).MaximumLength(InvitationEvent.NoteMaxLength);
    }
}
