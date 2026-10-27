using FluentValidation;
using Htn.Domain.Finance;

namespace Htn.Api.Features.Finance;

/// <summary>
/// Rule builders shared by the validators and the multipart handlers. A receipt upload cannot be bound as JSON, so
/// those handlers validate by hand — calling these, so the two paths give the same message for the same mistake.
/// </summary>
internal static class FinanceValidationRules
{
    public const decimal MaxAmount = 9_999_999_999.99m;

    public static IRuleBuilderOptions<T, decimal?> Money<T>(this IRuleBuilder<T, decimal?> rule, string what = "An amount") =>
        rule.NotNull().WithMessage($"{what} is required.")
            .GreaterThan(0m).WithMessage($"{what} must be more than ₱0.")
            .LessThanOrEqualTo(MaxAmount).WithMessage($"{what} is too large.")
            .Must(v => v is null || decimal.Round(v.Value, 2) == v.Value)
            .WithMessage("Use at most 2 decimal places, like 1500 or 1500.50.");

    public static IRuleBuilderOptions<T, string?> Reason<T>(this IRuleBuilder<T, string?> rule, int max = 500) =>
        rule.NotEmpty().WithMessage("A reason is required.")
            .MaximumLength(max).WithMessage($"Keep the reason under {max} characters.");

    public static IRuleBuilderOptions<T, DateOnly?> EntryDate<T>(this IRuleBuilder<T, DateOnly?> rule) =>
        rule.NotNull().WithMessage("A date is required.")
            .Must(d => d is null || d.Value.Year is >= 2020 and <= 2100).WithMessage("Pick a date in this decade.");

    public static IRuleBuilderOptions<T, Guid?> Id<T>(this IRuleBuilder<T, Guid?> rule, string what) =>
        rule.NotNull().WithMessage($"{what} is required.")
            .NotEqual(Guid.Empty).WithMessage($"{what} is required.");
}

public sealed class CreateTransferValidator : AbstractValidator<CreateTransferRequest>
{
    public CreateTransferValidator()
    {
        RuleFor(r => r.FromFundId).Id("A fund to transfer from");
        RuleFor(r => r.ToFundId).Id("A fund to transfer to");
        RuleFor(r => r.ToFundId).NotEqual(r => r.FromFundId)
            .WithMessage("Pick two different funds for a transfer.");
        RuleFor(r => r.EntryDate).EntryDate();
        RuleFor(r => r.Amount).Money();
        RuleFor(r => r.Description).NotEmpty().WithMessage("Say what the transfer is for.")
            .MaximumLength(LedgerEntry.DescriptionMaxLength);
    }
}

public sealed class SweepSurplusValidator : AbstractValidator<SweepSurplusRequest>
{
    public SweepSurplusValidator()
    {
        RuleFor(r => r.EntryDate).EntryDate();
        RuleFor(r => r.Description).MaximumLength(LedgerEntry.DescriptionMaxLength);
    }
}

public sealed class FinanceReasonValidator : AbstractValidator<FinanceReasonRequest>
{
    public FinanceReasonValidator() => RuleFor(r => r.Reason).Reason();
}

public sealed class CloseMonthValidator : AbstractValidator<CloseMonthRequest>
{
    public CloseMonthValidator() => RuleFor(r => r.Note).MaximumLength(MonthClose.NoteMaxLength);
}

public sealed class ReconcileAccountValidator : AbstractValidator<ReconcileAccountRequest>
{
    public ReconcileAccountValidator()
    {
        RuleFor(r => r.Year).NotNull().InclusiveBetween(2020, 2100).WithMessage("Pick a year in this decade.");
        RuleFor(r => r.Month).NotNull().InclusiveBetween(1, 12).WithMessage("Pick a month from 1 to 12.");
        // A statement balance may be negative (an overdrawn account) or zero (an emptied cash box), so only the
        // precision is checked here, not the sign.
        RuleFor(r => r.StatementBalance).NotNull().WithMessage("The statement balance is required.")
            .Must(v => v is null || decimal.Round(v.Value, 2) == v.Value)
            .WithMessage("Use at most 2 decimal places.")
            .Must(v => v is null || Math.Abs(v.Value) <= FinanceValidationRules.MaxAmount)
            .WithMessage("That balance is too large.");
        RuleFor(r => r.Note).MaximumLength(AccountReconciliation.NoteMaxLength);
    }
}

public sealed class SaveBudgetValidator : AbstractValidator<SaveBudgetRequest>
{
    public SaveBudgetValidator()
    {
        RuleFor(r => r.Reason).Reason(EventBudgetVersion.ReasonMaxLength);
        RuleFor(r => r.Lines).NotEmpty().WithMessage("A budget needs at least one line.");
        RuleFor(r => r.Lines!).Must(lines => lines.Count <= 200)
            .WithMessage("A budget holds at most 200 lines.")
            .When(r => r.Lines is not null);
        // The same category twice on one side would make an actual ambiguous: which line does the money belong to?
        RuleFor(r => r.Lines!).Must(lines =>
                lines.Where(l => l.CategoryId is not null && l.Kind is not null)
                    .GroupBy(l => (l.CategoryId, l.Kind)).All(g => g.Count() == 1))
            .WithMessage("Each category appears once per income or expense side. Combine the duplicate lines.")
            .When(r => r.Lines is not null);
        RuleForEach(r => r.Lines).SetValidator(new SaveBudgetLineValidator());
    }
}

public sealed class SaveBudgetLineValidator : AbstractValidator<SaveBudgetLineRequest>
{
    public SaveBudgetLineValidator()
    {
        RuleFor(l => l.CategoryId).Id("A category");
        RuleFor(l => l.Kind).NotNull().WithMessage("Say whether the line is income or expense.")
            .IsInEnum().WithMessage("Say whether the line is income or expense.");
        RuleFor(l => l.Label).NotEmpty().WithMessage("Name the line.")
            .MaximumLength(EventBudgetLine.LabelMaxLength);
        RuleFor(l => l.AmountBudgeted).Money("A budgeted amount");
        RuleFor(l => l.Note).MaximumLength(EventBudgetLine.NoteMaxLength);
    }
}

public sealed class CreateExpenseRequestValidator : AbstractValidator<CreateExpenseRequestRequest>
{
    public CreateExpenseRequestValidator()
    {
        RuleFor(r => r.Title).NotEmpty().WithMessage("Say what the money is for.")
            .MaximumLength(ExpenseRequest.TitleMaxLength);
        RuleFor(r => r.Description).MaximumLength(ExpenseRequest.DescriptionMaxLength);
        RuleFor(r => r.Amount).Money();
        RuleFor(r => r.CategoryId).Id("A category");
    }
}

/// <summary>An approval may carry a note; a rejection must carry a reason. The endpoints pick the right one.</summary>
public sealed class DecideExpenseRequestValidator : AbstractValidator<DecideExpenseRequestRequest>
{
    public DecideExpenseRequestValidator() =>
        RuleFor(r => r.Reason).MaximumLength(ExpenseRequest.ReasonMaxLength);
}
