using FluentValidation;
using Htn.Domain.Agenda;

namespace Htn.Api.Features.Agenda;

public sealed class RoomRequestValidator : AbstractValidator<RoomRequest>
{
    public RoomRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(EventRoom.NameMaxLength);
        RuleFor(x => x.ColorHex).NotEmpty().Matches("^#[0-9A-Fa-f]{6}$").WithMessage("Colour must be a hex value like #B71C1C.");
    }
}

public sealed class SessionRequestValidator : AbstractValidator<SessionRequest>
{
    public SessionRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(Session.TitleMaxLength);
        RuleFor(x => x.Kind).NotNull().IsInEnum();
        RuleFor(x => x.StartsAt).NotNull();
        RuleFor(x => x.DurationMinutes).NotNull().InclusiveBetween(Session.MinDuration, Session.MaxDuration);
        RuleFor(x => x.Notes).MaximumLength(Session.NotesMaxLength);
        RuleForEach(x => x.Speakers).ChildRules(sp =>
        {
            sp.RuleFor(x => x.SpeakerId).NotNull().NotEqual(Guid.Empty);
            sp.RuleFor(x => x.Role).IsInEnum().When(x => x.Role is not null);
        });
    }
}

public sealed class SessionSpeakersRequestValidator : AbstractValidator<SessionSpeakersRequest>
{
    public SessionSpeakersRequestValidator() =>
        RuleForEach(x => x.Speakers).ChildRules(sp => sp.RuleFor(x => x.SpeakerId).NotNull().NotEqual(Guid.Empty));
}
