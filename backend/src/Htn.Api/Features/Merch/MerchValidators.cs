using FluentValidation;
using Htn.Domain.Merch;

namespace Htn.Api.Features.Merch;

public sealed class MerchItemRequestValidator : AbstractValidator<MerchItemRequest>
{
    public MerchItemRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(MerchItem.NameMaxLength);
        RuleFor(x => x.Price).NotNull().GreaterThanOrEqualTo(0m);
        RuleFor(x => x.SwatchColor).Matches("^#[0-9A-Fa-f]{6}$").When(x => x.SwatchColor is not null)
            .WithMessage("Swatch color must look like #B71C1C.");
    }
}

public sealed class MerchVariantRequestValidator : AbstractValidator<MerchVariantRequest>
{
    public MerchVariantRequestValidator()
    {
        RuleFor(x => x.Label).NotEmpty().MaximumLength(MerchVariant.LabelMaxLength);
        RuleFor(x => x.StockOnHand).NotNull().InclusiveBetween(0, MerchVariant.MaxStock);
    }
}

public sealed class MerchVariantAdjustRequestValidator : AbstractValidator<MerchVariantAdjustRequest>
{
    public MerchVariantAdjustRequestValidator()
    {
        RuleFor(x => x.Label).MaximumLength(MerchVariant.LabelMaxLength).NotEmpty().When(x => x.Label is not null);
        RuleFor(x => x.StockOnHand).InclusiveBetween(0, MerchVariant.MaxStock).When(x => x.StockOnHand is not null);
    }
}

public sealed class ReasonRequestValidator : AbstractValidator<ReasonRequest>
{
    public ReasonRequestValidator() =>
        RuleFor(x => x.Reason).NotEmpty().WithMessage("Give a reason.").MaximumLength(MerchItem.ReasonMaxLength);
}

public sealed class EventMerchSettingsRequestValidator : AbstractValidator<EventMerchSettingsRequest>
{
    public EventMerchSettingsRequestValidator()
    {
        RuleFor(x => x.GoonPct).NotNull().InclusiveBetween(0, 100);
        RuleFor(x => x.ApprenticePct).NotNull().InclusiveBetween(0, 100);
        RuleFor(x => x.MemberPct).NotNull().InclusiveBetween(0, 100);
        RuleFor(x => x.BoardPct).NotNull().InclusiveBetween(0, 100);
    }
}

public sealed class MerchSaleLineRequestValidator : AbstractValidator<MerchSaleLineRequest>
{
    public MerchSaleLineRequestValidator()
    {
        RuleFor(x => x.VariantId).NotEmpty().WithMessage("A variant is required.");
        RuleFor(x => x.Qty).NotNull().GreaterThan(0);
    }
}

public sealed class MerchSaleRequestValidator : AbstractValidator<MerchSaleRequest>
{
    public MerchSaleRequestValidator()
    {
        RuleFor(x => x.ClientRequestId).NotNull().NotEqual(Guid.Empty).WithMessage("A client request id is required.");
        RuleFor(x => x.Method).NotNull().IsInEnum();
        RuleFor(x => x.Lines).NotNull().Must(l => l!.Count > 0).WithMessage("A sale needs at least one line.");
        RuleForEach(x => x.Lines).SetValidator(new MerchSaleLineRequestValidator()).When(x => x.Lines is not null);
        RuleFor(x => x.Station).MaximumLength(Htn.Domain.Merch.MerchSale.StationMaxLength);
        RuleFor(x => x.BuyerName).MaximumLength(Htn.Domain.Merch.MerchSale.BuyerNameMaxLength)
            .NotEmpty().WithMessage("A buyer name is required when no buyer is matched.")
            .When(x => x.BuyerPersonId is null && x.BuyerAttendeeId is null);
    }
}

public sealed class PosScanRequestValidator : AbstractValidator<PosScanRequest>
{
    public PosScanRequestValidator() =>
        RuleFor(x => x.Payload).NotEmpty().MaximumLength(Htn.Domain.Registrations.TicketTokenCodec.MaxTokenLength);
}
