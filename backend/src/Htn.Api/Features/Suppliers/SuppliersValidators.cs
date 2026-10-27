using FluentValidation;
using Htn.Domain.Suppliers;

namespace Htn.Api.Features.Suppliers;

public sealed class SupplierRequestValidator : AbstractValidator<SupplierRequest>
{
    public SupplierRequestValidator()
    {
        RuleFor(x => x.Category).NotNull().IsInEnum();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(Supplier.NameMaxLength);
        RuleFor(x => x.ContactName).MaximumLength(Supplier.ContactNameMaxLength);
        RuleFor(x => x.ContactEmail).EmailAddress().MaximumLength(Supplier.ContactEmailMaxLength)
            .When(x => !string.IsNullOrWhiteSpace(x.ContactEmail));
        RuleFor(x => x.ContactPhone).MaximumLength(Supplier.ContactPhoneMaxLength);
        RuleFor(x => x.IssuesOr).NotNull();
        RuleFor(x => x.Tin).MaximumLength(Supplier.TinMaxLength);
        RuleFor(x => x.BankDetails).MaximumLength(Supplier.BankDetailsMaxLength);
        RuleFor(x => x.Notes).MaximumLength(Supplier.NotesMaxLength);
    }
}

public sealed class SupplierUpdateRequestValidator : AbstractValidator<SupplierUpdateRequest>
{
    public SupplierUpdateRequestValidator()
    {
        RuleFor(x => x.ContactName).MaximumLength(Supplier.ContactNameMaxLength);
        RuleFor(x => x.ContactEmail).EmailAddress().MaximumLength(Supplier.ContactEmailMaxLength)
            .When(x => !string.IsNullOrWhiteSpace(x.ContactEmail));
        RuleFor(x => x.ContactPhone).MaximumLength(Supplier.ContactPhoneMaxLength);
        RuleFor(x => x.IssuesOr).NotNull();
        RuleFor(x => x.Tin).MaximumLength(Supplier.TinMaxLength);
        RuleFor(x => x.BankDetails).MaximumLength(Supplier.BankDetailsMaxLength);
        RuleFor(x => x.Notes).MaximumLength(Supplier.NotesMaxLength);
    }
}

public sealed class ReasonRequestValidator : AbstractValidator<ReasonRequest>
{
    public ReasonRequestValidator() =>
        RuleFor(x => x.Reason).NotEmpty().WithMessage("Give a reason.").MaximumLength(Supplier.ReasonMaxLength);
}

public sealed class NewHireRequestValidator : AbstractValidator<NewHireRequest>
{
    public NewHireRequestValidator()
    {
        RuleFor(x => x.SupplierId).NotEmpty().WithMessage("A supplier is required.");
        RuleFor(x => x.CategoryId).NotEmpty().WithMessage("A budget category is required.");
        RuleFor(x => x.OwnerCommitteeId).NotEmpty().WithMessage("An owning committee is required.");
        RuleFor(x => x.Note).MaximumLength(EventSupplier.NoteMaxLength);
    }
}

public sealed class UpdateHireRequestValidator : AbstractValidator<UpdateHireRequest>
{
    public UpdateHireRequestValidator()
    {
        RuleFor(x => x.ContractAmount).GreaterThanOrEqualTo(0m).When(x => x.ContractAmount is not null);
        RuleFor(x => x.PaidAmount).GreaterThanOrEqualTo(0m).When(x => x.PaidAmount is not null);
        RuleFor(x => x.NextPaymentAmount).GreaterThanOrEqualTo(0m).When(x => x.NextPaymentAmount is not null);
        RuleFor(x => x.Note).MaximumLength(EventSupplier.NoteMaxLength);
        RuleFor(x => x.Status).IsInEnum().When(x => x.Status is not null);
    }
}

public sealed class RatingRequestValidator : AbstractValidator<RatingRequest>
{
    public RatingRequestValidator()
    {
        RuleFor(x => x.Stars).NotNull().InclusiveBetween(1, 5);
        RuleFor(x => x.HireAgain).NotNull().IsInEnum();
        RuleFor(x => x.Note).MaximumLength(SupplierRating.NoteMaxLength);
        RuleForEach(x => x.Tags).Must(t => SupplierRatingTags.All.Contains(t))
            .WithMessage("That is not a known rating tag.")
            .When(x => x.Tags is not null);
    }
}
