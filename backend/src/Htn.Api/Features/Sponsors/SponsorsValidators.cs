using FluentValidation;
using Htn.Domain.Sponsors;

namespace Htn.Api.Features.Sponsors;

internal static class SponsorValidationRules
{
    public static IRuleBuilderOptions<T, decimal?> MoneyAmount<T>(this IRuleBuilder<T, decimal?> rule, string field) =>
        rule.Must(v => v is null or > 0m).WithMessage($"{field} must be more than 0.")
            .Must(v => v is null || decimal.Round(v.Value, 2) == v.Value)
            .WithMessage($"{field} can have at most 2 decimal places.");

    public static IRuleBuilderOptions<T, string?> ReasonText<T>(this IRuleBuilder<T, string?> rule) =>
        rule.NotEmpty().WithMessage("Give a reason.").MaximumLength(Sponsor.ReasonMaxLength);
}

public sealed class SponsorTierRequestValidator : AbstractValidator<SponsorTierRequest>
{
    public SponsorTierRequestValidator()
    {
        RuleFor(x => x.Class).NotNull().IsInEnum();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(SponsorTier.NameMaxLength);
        RuleFor(x => x.CashAmount).MoneyAmount("Cash amount");
        RuleFor(x => x.PriceNote).MaximumLength(SponsorTier.PriceNoteMaxLength);
        RuleFor(x => x.InKindText).MaximumLength(SponsorTier.InKindMaxLength);
        RuleFor(x => x.InKindItemCount).InclusiveBetween(1, 100_000).When(x => x.InKindItemCount is not null);
        RuleFor(x => x.Slots).InclusiveBetween(1, SponsorTier.MaxSlots).When(x => x.Slots is not null);
        RuleFor(x => x.CompedSeats).InclusiveBetween(0, SponsorTier.MaxCompedSeats).When(x => x.CompedSeats is not null);
        RuleForEach(x => x.Perks).MaximumLength(SponsorTier.PerkMaxLength);
        RuleFor(x => x.Perks).Must(p => p is null || p.Count <= SponsorTier.MaxPerks)
            .WithMessage($"A tier can list at most {SponsorTier.MaxPerks} perks.");
        RuleFor(x => x).Must(x => x.CashAmount is not null || !string.IsNullOrWhiteSpace(x.InKindText))
            .WithMessage("A tier needs a cash amount, an in-kind description, or both.")
            .WithName("cashAmount");
    }
}

public sealed class SponsorRequestValidator : AbstractValidator<SponsorRequest>
{
    public SponsorRequestValidator()
    {
        RuleFor(x => x.Kind).NotNull().IsInEnum();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(Sponsor.NameMaxLength);
        RuleFor(x => x.DisplayName).MaximumLength(Sponsor.NameMaxLength);
        RuleFor(x => x.ContactName).MaximumLength(Sponsor.ContactNameMaxLength);
        RuleFor(x => x.ContactRole).MaximumLength(Sponsor.ContactRoleMaxLength);
        RuleFor(x => x.ContactEmail).EmailAddress().MaximumLength(Sponsor.EmailMaxLength)
            .When(x => !string.IsNullOrWhiteSpace(x.ContactEmail));
        RuleFor(x => x.ContactMobile).MaximumLength(Sponsor.MobileMaxLength);
        RuleFor(x => x.CashAmount).MoneyAmount("Cash amount");
        RuleFor(x => x.InKindDescription).MaximumLength(Sponsor.InKindMaxLength);
        RuleFor(x => x.InKindItemCount).InclusiveBetween(1, 100_000).When(x => x.InKindItemCount is not null);
        RuleFor(x => x.CompedSeats).InclusiveBetween(0, Sponsor.MaxCompedSeats).When(x => x.CompedSeats is not null);
        RuleFor(x => x.WebsiteUrl).MaximumLength(Sponsor.WebsiteMaxLength)
            .Must(u => u!.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || u.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            .WithMessage("Website must start with http:// or https://").When(x => !string.IsNullOrWhiteSpace(x.WebsiteUrl));
        RuleFor(x => x).Must(x => x.CashAmount is not null || !string.IsNullOrWhiteSpace(x.InKindDescription))
            .WithMessage("A sponsor needs a cash amount, an in-kind contribution, or both.")
            .WithName("cashAmount");
    }
}

public sealed class ReasonRequestValidator : AbstractValidator<ReasonRequest>
{
    public ReasonRequestValidator() => RuleFor(x => x.Reason).ReasonText();
}

public sealed class CashReceivedRequestValidator : AbstractValidator<CashReceivedRequest>
{
    public CashReceivedRequestValidator()
    {
        RuleFor(x => x.PaymentAccountId).NotNull().NotEqual(Guid.Empty);
        RuleFor(x => x.ReceivedOn).NotNull();
        RuleFor(x => x.Reference).MaximumLength(Sponsor.ReferenceMaxLength);
    }
}

public sealed class SponsorPerkRequestValidator : AbstractValidator<SponsorPerkRequest>
{
    public SponsorPerkRequestValidator() =>
        RuleFor(x => x.Text).NotEmpty().MaximumLength(SponsorPerk.TextMaxLength);
}

public sealed class PerkDeliveredRequestValidator : AbstractValidator<PerkDeliveredRequest>
{
    public PerkDeliveredRequestValidator() => RuleFor(x => x.Delivered).NotNull();
}

public sealed class PartnerSettingsRequestValidator : AbstractValidator<PartnerSettingsRequest>
{
    public PartnerSettingsRequestValidator()
    {
        RuleFor(x => x.Threshold).NotNull()
            .InclusiveBetween(EventPartnerSettings.MinThreshold, EventPartnerSettings.MaxThreshold)
            .Must(t => t!.Value % EventPartnerSettings.ThresholdStep == 0)
            .WithMessage($"Threshold moves in steps of {EventPartnerSettings.ThresholdStep}.");
        RuleFor(x => x.CountBasis).NotNull().IsInEnum();
        RuleFor(x => x.GroupLinkEnabled).NotNull();
    }
}

public sealed class TagPartnerRequestValidator : AbstractValidator<TagPartnerRequest>
{
    public TagPartnerRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(EventPartner.NameMaxLength);
        RuleFor(x => x.OrgType).MaximumLength(EventPartner.OrgTypeMaxLength);
    }
}

public sealed class PartnerWebsiteRequestValidator : AbstractValidator<PartnerWebsiteRequest>
{
    public PartnerWebsiteRequestValidator() =>
        RuleFor(x => x.WebsiteUrl).MaximumLength(EventPartner.WebsiteMaxLength)
            .Must(u => u!.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || u.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            .WithMessage("Website must start with http:// or https://").When(x => !string.IsNullOrWhiteSpace(x.WebsiteUrl));
}
