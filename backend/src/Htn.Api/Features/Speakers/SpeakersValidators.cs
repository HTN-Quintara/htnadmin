using FluentValidation;

namespace Htn.Api.Features.Speakers;

public sealed class SpeakerPatchRequestValidator : AbstractValidator<SpeakerPatchRequest>
{
    public SpeakerPatchRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(120).When(x => x.Name is not null);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(254).When(x => x.Email is not null);
        RuleFor(x => x.Affiliation).MaximumLength(120);
    }
}

public sealed class SpeakerTravelPatchRequestValidator : AbstractValidator<SpeakerTravelPatchRequest>
{
    public SpeakerTravelPatchRequestValidator()
    {
        RuleFor(x => x.RoomNote).MaximumLength(120);
        RuleFor(x => x).Must(x => x.ArrivalDate is null || x.DepartureDate is null || x.DepartureDate >= x.ArrivalDate)
            .WithMessage("Departure must be on or after arrival.").WithName("departureDate");
    }
}

public sealed class TravelModeRequestValidator : AbstractValidator<TravelModeRequest>
{
    public TravelModeRequestValidator() => RuleFor(x => x.TravelMode).NotNull().IsInEnum();
}

public sealed class SpeakerReasonRequestValidator : AbstractValidator<SpeakerReasonRequest>
{
    public SpeakerReasonRequestValidator() => RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
}

public sealed class VerifyRequestValidator : AbstractValidator<VerifyRequest>
{
    public VerifyRequestValidator() => RuleFor(x => x.Code).NotEmpty().Matches("^[0-9]{6}$").WithMessage("Enter the 6-digit code.");
}

public sealed class SubmitRequestValidator : AbstractValidator<SubmitRequest>
{
    public SubmitRequestValidator()
    {
        RuleFor(x => x.Consent).Equal(true).WithMessage("Please allow HackTheNorth.PH to use these details to arrange your travel.");
        RuleFor(x => x).Must(x => x.ArrivalDate is null || x.DepartureDate is null || x.DepartureDate >= x.ArrivalDate)
            .WithMessage("Departure must be on or after arrival.").WithName("departureDate");
        RuleFor(x => x.FlyingFrom).MaximumLength(120);
        RuleFor(x => x.PreferredArrivalTime).MaximumLength(40);
        RuleFor(x => x.IdFullName).MaximumLength(160);
        RuleFor(x => x.BusFrom).MaximumLength(120);
        RuleFor(x => x.BusEta).MaximumLength(40);
        RuleFor(x => x.NeedsNotes).MaximumLength(1000);
        RuleFor(x => x.PlusOneName).NotEmpty().MaximumLength(120).When(x => x.HasPlusOne == true);
        RuleFor(x => x.PlusOneEmail).NotEmpty().EmailAddress().MaximumLength(254).When(x => x.HasPlusOne == true);
    }
}
