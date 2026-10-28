using FluentValidation;
using Htn.Domain.PopUps;

namespace Htn.Api.Features.PopUps;

public sealed class NewPopUpRequestValidator : AbstractValidator<NewPopUpRequest>
{
    public NewPopUpRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(PopUpStore.NameMaxLength);
        RuleFor(x => x).Must(x => (x.VenueDirectoryOrgId is null) != string.IsNullOrWhiteSpace(x.VenueText))
            .WithMessage("Give either a Directory venue or a venue name, not both or neither.").OverridePropertyName("venueText");
        RuleFor(x => x.StartsAt).NotNull();
        RuleFor(x => x.EndsAt).NotNull().GreaterThan(x => x.StartsAt ?? DateTimeOffset.MinValue)
            .WithMessage("End time must be after the start time.");
        RuleFor(x => x.FeeAmount).NotNull().GreaterThanOrEqualTo(0m);
        RuleFor(x => x.DiscountMode).NotNull().IsInEnum();
        RuleFor(x => x).Must(x => x.DiscountMode != PopUpDiscountMode.Custom
            || (x.GoonPct is not null && x.ApprenticePct is not null && x.MemberPct is not null && x.BoardPct is not null))
            .WithMessage("Custom discounts need all four percentages.").OverridePropertyName("goonPct");
        RuleFor(x => x.OwnerCommitteeId).NotEmpty().WithMessage("An owning committee is required.");
        RuleForEach(x => x.TeamPersonIds).NotEmpty().When(x => x.TeamPersonIds is not null);
    }
}

public sealed class AllocationLineRequestValidator : AbstractValidator<AllocationLineRequest>
{
    public AllocationLineRequestValidator()
    {
        RuleFor(x => x.VariantId).NotEmpty().WithMessage("A variant is required.");
        RuleFor(x => x.Qty).NotNull().GreaterThan(0);
    }
}

public sealed class AllocationsRequestValidator : AbstractValidator<AllocationsRequest>
{
    public AllocationsRequestValidator()
    {
        RuleFor(x => x.Lines).NotEmpty().WithMessage("At least one line is required.");
        RuleForEach(x => x.Lines).SetValidator(new AllocationLineRequestValidator()).When(x => x.Lines is not null);
    }
}

public sealed class CountLineRequestValidator : AbstractValidator<CountLineRequest>
{
    public CountLineRequestValidator()
    {
        RuleFor(x => x.VariantId).NotEmpty().WithMessage("A variant is required.");
        RuleFor(x => x.QtyReturned).NotNull().GreaterThanOrEqualTo(0);
    }
}

public sealed class CountRequestValidator : AbstractValidator<CountRequest>
{
    public CountRequestValidator()
    {
        RuleFor(x => x.CashCounted).NotNull().GreaterThanOrEqualTo(0m);
        RuleForEach(x => x.Returned).SetValidator(new CountLineRequestValidator()).When(x => x.Returned is not null);
    }
}

public sealed class WriteOffRequestValidator : AbstractValidator<WriteOffRequest>
{
    public WriteOffRequestValidator() =>
        RuleFor(x => x.Reason).NotEmpty().WithMessage("Give a reason.").MaximumLength(PopUpStore.ReasonMaxLength);
}

public sealed class MoveToStoreRequestValidator : AbstractValidator<MoveToStoreRequest>
{
    public MoveToStoreRequestValidator() =>
        RuleFor(x => x.VariantIds).NotEmpty().WithMessage("Pick at least one variant.");
}
