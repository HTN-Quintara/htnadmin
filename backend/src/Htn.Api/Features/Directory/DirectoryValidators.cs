using FluentValidation;
using Htn.Domain.Directory;

namespace Htn.Api.Features.Directory;

internal static class DirectoryValidationRules
{
    public static IRuleBuilderOptions<T, string?> ReasonText<T>(this IRuleBuilder<T, string?> rule) =>
        rule.NotEmpty().WithMessage("Give a reason.").MaximumLength(DirectoryOrg.ReasonMaxLength);

    public static IRuleBuilderOptions<T, string?> WebsiteUrl<T>(this IRuleBuilder<T, string?> rule, int max) =>
        rule.MaximumLength(max)
            .Must(u => u!.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || u.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            .WithMessage("Website must start with http:// or https://");
}

public sealed class DirectoryOrgRequestValidator : AbstractValidator<DirectoryOrgRequest>
{
    public DirectoryOrgRequestValidator()
    {
        RuleFor(x => x.Kind).NotNull().IsInEnum();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(DirectoryOrg.NameMaxLength);
        RuleFor(x => x.AddresseeSalutation).MaximumLength(DirectoryOrg.SalutationMaxLength);
        RuleFor(x => x.Notes).MaximumLength(DirectoryOrg.NotesMaxLength);
        RuleFor(x => x.WebsiteUrl).WebsiteUrl(DirectoryOrg.WebsiteMaxLength).When(x => !string.IsNullOrWhiteSpace(x.WebsiteUrl));
    }
}

public sealed class DirectoryOrgUpdateRequestValidator : AbstractValidator<DirectoryOrgUpdateRequest>
{
    public DirectoryOrgUpdateRequestValidator()
    {
        RuleFor(x => x.AddresseeSalutation).MaximumLength(DirectoryOrg.SalutationMaxLength);
        RuleFor(x => x.Notes).MaximumLength(DirectoryOrg.NotesMaxLength);
        RuleFor(x => x.WebsiteUrl).WebsiteUrl(DirectoryOrg.WebsiteMaxLength).When(x => !string.IsNullOrWhiteSpace(x.WebsiteUrl));
    }
}

public sealed class ReasonRequestValidator : AbstractValidator<ReasonRequest>
{
    public ReasonRequestValidator() => RuleFor(x => x.Reason).ReasonText();
}

public sealed class DirectoryContactRequestValidator : AbstractValidator<DirectoryContactRequest>
{
    public DirectoryContactRequestValidator()
    {
        RuleFor(x => x.Position).NotEmpty().MaximumLength(DirectoryContact.PositionMaxLength);
        RuleFor(x => x.HolderName).NotEmpty().MaximumLength(DirectoryContact.HolderNameMaxLength);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(DirectoryContact.EmailMaxLength);
        RuleFor(x => x.Phone).MaximumLength(DirectoryContact.PhoneMaxLength);
        RuleFor(x => x.IsPrimary).NotNull();
        RuleFor(x => x.CcEmails).MaximumLength(DirectoryContact.CcEmailsMaxLength);
    }
}

public sealed class HeadChangedRequestValidator : AbstractValidator<HeadChangedRequest>
{
    public HeadChangedRequestValidator()
    {
        RuleFor(x => x.Position).NotEmpty().MaximumLength(DirectoryContact.PositionMaxLength);
        RuleFor(x => x.HolderName).NotEmpty().MaximumLength(DirectoryContact.HolderNameMaxLength);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(DirectoryContact.EmailMaxLength);
        RuleFor(x => x.Phone).MaximumLength(DirectoryContact.PhoneMaxLength);
    }
}
