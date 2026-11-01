using Htn.Domain.Common;
using Htn.Domain.Events;
using Htn.Domain.Finance;

namespace Htn.Domain.Tests;

public class LedgerEntryTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 8, 0, 0, TimeSpan.Zero);
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly Guid Account = Guid.NewGuid();

    private static NewLedgerEntry Data(
        LedgerDirection direction = LedgerDirection.Out,
        decimal amount = 1500m,
        LedgerSource source = LedgerSource.TicketPayment,
        Guid? receiptFileId = null,
        string description = "Venue deposit") =>
        new(Funds.OrganizationId, Account, LedgerCategories.TicketsId, new DateOnly(2026, 10, 5),
            direction, amount, "OR-1001", description, receiptFileId, source, null);

    [Fact]
    public void A_posted_entry_counts_immediately_and_records_its_own_approval()
    {
        var entry = LedgerEntry.Posted(Data(), Actor, Now);

        Assert.Equal(LedgerStatus.Posted, entry.Status);
        Assert.Equal(Actor, entry.EnteredById);
        Assert.Equal(Actor, entry.ApprovedById);
        Assert.Equal(Now, entry.ApprovedAt);
        Assert.False(entry.IsClosed);
    }

    [Fact]
    public void An_assistant_entry_is_not_money_until_it_is_approved()
    {
        var entry = LedgerEntry.Pending(Data(), Actor, Now);

        Assert.Equal(LedgerStatus.Pending, entry.Status);
        Assert.Null(entry.ApprovedById);
        Assert.Null(entry.ApprovedAt);

        var approver = Guid.NewGuid();
        entry.Approve(approver, Now.AddHours(1));

        Assert.Equal(LedgerStatus.Posted, entry.Status);
        Assert.Equal(approver, entry.ApprovedById);
        Assert.Equal(Now.AddHours(1), entry.ApprovedAt);
    }

    [Fact]
    public void The_sign_comes_from_the_direction_never_from_the_amount()
    {
        Assert.Equal(1500m, LedgerEntry.Posted(Data(LedgerDirection.In), Actor, Now).SignedAmount);
        Assert.Equal(-1500m, LedgerEntry.Posted(Data(LedgerDirection.Out), Actor, Now).SignedAmount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    [InlineData(10.005)]
    public void Money_never_moves_by_zero_or_fractions_of_a_centavo(decimal amount) =>
        Assert.Throws<DomainException>(() => LedgerEntry.Posted(Data(amount: amount), Actor, Now));

    [Fact]
    public void An_entry_a_person_types_in_needs_a_receipt()
    {
        Assert.Throws<DomainException>(() => LedgerEntry.Posted(Data(source: LedgerSource.Manual), Actor, Now));
        Assert.Throws<DomainException>(() => LedgerEntry.Posted(Data(source: LedgerSource.ExpenseRequest), Actor, Now));

        // A system row has no external receipt to attach.
        var auto = LedgerEntry.Posted(Data(source: LedgerSource.TicketPayment), Actor, Now);
        Assert.Null(auto.ReceiptFileId);

        var manual = LedgerEntry.Posted(Data(source: LedgerSource.Manual, receiptFileId: Guid.NewGuid()), Actor, Now);
        Assert.NotNull(manual.ReceiptFileId);
    }

    [Fact]
    public void A_description_is_required_and_a_blank_reference_becomes_null()
    {
        Assert.Throws<DomainException>(() => LedgerEntry.Posted(Data(description: "   "), Actor, Now));

        var entry = LedgerEntry.Posted(
            new NewLedgerEntry(Funds.OrganizationId, Account, LedgerCategories.TicketsId, new DateOnly(2026, 10, 5),
                LedgerDirection.In, 10m, "  ", "Ticket", null, LedgerSource.TicketPayment, null),
            Actor, Now);
        Assert.Null(entry.Reference);
    }

    [Fact]
    public void Voiding_needs_a_reason_and_happens_once()
    {
        var entry = LedgerEntry.Posted(Data(), Actor, Now);

        Assert.Throws<DomainException>(() => entry.Void("  ", Actor, Now));

        entry.Void("Wrong amount", Actor, Now.AddHours(2));

        Assert.Equal(LedgerStatus.Voided, entry.Status);
        Assert.Equal("Wrong amount", entry.VoidReason);
        Assert.Throws<DomainException>(() => entry.Void("Again", Actor, Now.AddHours(3)));
    }

    [Fact]
    public void A_voided_entry_can_no_longer_be_approved_and_a_posted_one_is_not_approved_twice()
    {
        var voided = LedgerEntry.Pending(Data(), Actor, Now);
        voided.Void("Duplicate", Actor, Now);
        Assert.Throws<DomainException>(() => voided.Approve(Actor, Now));

        var posted = LedgerEntry.Posted(Data(), Actor, Now);
        Assert.Throws<DomainException>(() => posted.Approve(Actor, Now));
    }

    [Fact]
    public void An_entry_in_a_closed_month_is_corrected_with_a_contra_entry_not_a_void()
    {
        var entry = LedgerEntry.Posted(Data(), Actor, Now);
        entry.StampClosed(Guid.NewGuid());

        Assert.True(entry.IsClosed);
        Assert.Throws<DomainException>(() => entry.Void("Wrong amount", Actor, Now));

        var contra = LedgerEntry.Contra(entry, new DateOnly(2026, 11, 2), "Wrong amount", Actor, Now);

        Assert.Equal(LedgerSource.Contra, contra.Source);
        Assert.Equal(entry.Id, contra.ContraOfEntryId);
        Assert.Equal(LedgerDirection.In, contra.Direction);          // the original was Out
        Assert.Equal(entry.Amount, contra.Amount);
        Assert.Equal(entry.FundId, contra.FundId);
        Assert.Equal(entry.CategoryId, contra.CategoryId);
        Assert.Equal(0m, entry.SignedAmount + contra.SignedAmount);  // together they cancel
        Assert.Contains("Wrong amount", contra.Description);
        Assert.Equal(LedgerStatus.Posted, contra.Status);
    }

    [Fact]
    public void A_contra_entry_needs_a_reason_a_posted_original_and_is_never_offset_again()
    {
        var entry = LedgerEntry.Posted(Data(), Actor, Now);
        Assert.Throws<DomainException>(() => LedgerEntry.Contra(entry, new DateOnly(2026, 11, 2), " ", Actor, Now));

        var pending = LedgerEntry.Pending(Data(), Actor, Now);
        Assert.Throws<DomainException>(() => LedgerEntry.Contra(pending, new DateOnly(2026, 11, 2), "Why", Actor, Now));

        var contra = LedgerEntry.Contra(entry, new DateOnly(2026, 11, 2), "Wrong amount", Actor, Now);
        Assert.Throws<DomainException>(() => LedgerEntry.Contra(contra, new DateOnly(2026, 11, 3), "Again", Actor, Now));
    }

    [Fact]
    public void Only_a_posted_entry_is_closed_and_only_once()
    {
        var pending = LedgerEntry.Pending(Data(), Actor, Now);
        Assert.Throws<DomainException>(() => pending.StampClosed(Guid.NewGuid()));

        var entry = LedgerEntry.Posted(Data(), Actor, Now);
        entry.StampClosed(Guid.NewGuid());
        Assert.Throws<DomainException>(() => entry.StampClosed(Guid.NewGuid()));

        entry.StampReopened();
        Assert.False(entry.IsClosed);
    }
}

public class LedgerTransferTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Date = new(2026, 10, 27);

    private static Fund EventFund() =>
        Fund.ForEvent(
            Event.Create("HTN 2026", "htn-2026", Now.AddMonths(1), Now.AddMonths(1).AddDays(1), "BGC", 300, null, Now, Guid.NewGuid()),
            Now);

    [Fact]
    public void A_transfer_is_two_opposite_legs_sharing_one_group()
    {
        var org = Fund.Organization(Now);
        var ev = EventFund();

        var (outLeg, inLeg) = LedgerEntry.Transfer(ev, org, LedgerCategories.TransferId, Date, 5000m, "Surplus", Guid.NewGuid(), Now);

        Assert.Equal(outLeg.TransferGroupId, inLeg.TransferGroupId);
        Assert.NotNull(outLeg.TransferGroupId);
        Assert.Equal(LedgerDirection.Out, outLeg.Direction);
        Assert.Equal(LedgerDirection.In, inLeg.Direction);
        Assert.Equal(ev.Id, outLeg.FundId);
        Assert.Equal(org.Id, inLeg.FundId);
        Assert.Equal(outLeg.Amount, inLeg.Amount);
        Assert.Equal(outLeg.EntryDate, inLeg.EntryDate);
        // The two legs net to nothing across the organization: money moved, it did not appear.
        Assert.Equal(0m, outLeg.SignedAmount + inLeg.SignedAmount);
        Assert.Equal(LedgerStatus.Posted, outLeg.Status);
        Assert.Equal(LedgerStatus.Posted, inLeg.Status);
    }

    [Fact]
    public void A_transfer_needs_two_different_open_funds_and_a_real_amount()
    {
        var org = Fund.Organization(Now);
        var ev = EventFund();

        Assert.Throws<DomainException>(() =>
            LedgerEntry.Transfer(org, org, LedgerCategories.TransferId, Date, 100m, "Same fund", null, Now));
        Assert.Throws<DomainException>(() =>
            LedgerEntry.Transfer(ev, org, LedgerCategories.TransferId, Date, 0m, "Nothing", null, Now));
        Assert.Throws<DomainException>(() =>
            LedgerEntry.Transfer(ev, org, LedgerCategories.TransferId, Date, 100m, "Wrong source", null, Now, LedgerSource.Manual));

        org.Close(Now);
        Assert.Throws<DomainException>(() =>
            LedgerEntry.Transfer(ev, org, LedgerCategories.TransferId, Date, 100m, "Closed", null, Now));
    }

    [Fact]
    public void An_event_fund_is_swept_once_and_only_closes_afterwards()
    {
        var ev = EventFund();

        Assert.Throws<DomainException>(() => ev.Close(Now));   // nothing swept yet

        var group = Guid.NewGuid();
        ev.MarkSwept(group, Now);

        Assert.True(ev.IsSwept);
        Assert.Equal(group, ev.SweptByTransferGroupId);
        Assert.Throws<DomainException>(() => ev.MarkSwept(Guid.NewGuid(), Now));

        ev.Close(Now.AddHours(1));
        Assert.True(ev.IsClosed);
        Assert.Throws<DomainException>(() => ev.EnsureOpen());
    }

    [Fact]
    public void The_organization_fund_is_never_swept_and_has_a_fixed_id()
    {
        var org = Fund.Organization(Now);

        Assert.Equal(Funds.OrganizationId, org.Id);
        Assert.Equal(FundKind.Organization, org.Kind);
        Assert.Null(org.EventId);
        Assert.Throws<DomainException>(() => org.MarkSwept(Guid.NewGuid(), Now));
    }
}

public class MonthCloseTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_close_records_who_closed_the_period()
    {
        var actor = Guid.NewGuid();
        var close = MonthClose.Create(2026, 10, "Everything balanced", actor, Now);

        Assert.Equal(2026, close.Year);
        Assert.Equal(10, close.Month);
        Assert.Equal(actor, close.ClosedById);
        Assert.False(close.IsOpen);
    }

    [Fact]
    public void Reopening_needs_a_reason_and_happens_once()
    {
        var close = MonthClose.Create(2026, 10, null, Guid.NewGuid(), Now);

        Assert.Throws<DomainException>(() => close.Reopen("  ", Guid.NewGuid(), Now));

        close.Reopen("A supplier invoice arrived late", Guid.NewGuid(), Now.AddDays(1));

        Assert.True(close.IsOpen);
        Assert.Throws<DomainException>(() => close.Reopen("Again", Guid.NewGuid(), Now.AddDays(2)));
    }

    [Theory]
    [InlineData(2026, 0)]
    [InlineData(2026, 13)]
    [InlineData(1999, 6)]
    public void A_period_is_a_real_month(int year, int month) =>
        Assert.Throws<DomainException>(() => MonthClose.Create(year, month, null, Guid.NewGuid(), Now));
}

public class AccountReconciliationTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void The_difference_is_the_statement_minus_the_book_and_is_stored()
    {
        var r = AccountReconciliation.Create(Guid.NewGuid(), 2026, 10, 10_500m, 10_000m, "Interest", Guid.NewGuid(), Now);

        Assert.Equal(500m, r.Difference);
        Assert.False(r.IsBalanced);
        Assert.True(r.IsLive);

        var balanced = AccountReconciliation.Create(Guid.NewGuid(), 2026, 10, 10_000m, 10_000m, null, Guid.NewGuid(), Now);
        Assert.True(balanced.IsBalanced);
    }

    [Fact]
    public void A_statement_balance_may_be_negative_but_never_sub_centavo()
    {
        var overdrawn = AccountReconciliation.Create(Guid.NewGuid(), 2026, 10, -250m, 0m, null, Guid.NewGuid(), Now);
        Assert.Equal(-250m, overdrawn.Difference);

        Assert.Throws<DomainException>(() =>
            AccountReconciliation.Create(Guid.NewGuid(), 2026, 10, 10.005m, 0m, null, Guid.NewGuid(), Now));
    }

    [Fact]
    public void Reconciling_again_supersedes_the_previous_row_with_a_reason()
    {
        var r = AccountReconciliation.Create(Guid.NewGuid(), 2026, 10, 10_500m, 10_000m, null, Guid.NewGuid(), Now);

        Assert.Throws<DomainException>(() => r.Supersede(" ", Guid.NewGuid(), Now));

        r.Supersede("Bank corrected the statement", Guid.NewGuid(), Now.AddDays(1));

        Assert.False(r.IsLive);
        Assert.Throws<DomainException>(() => r.Supersede("Again", Guid.NewGuid(), Now.AddDays(2)));
    }
}

public class EventBudgetTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 8, 0, 0, TimeSpan.Zero);
    private static readonly Guid Actor = Guid.NewGuid();

    private static NewBudgetLine Line(Guid? categoryId = null, BudgetLineKind kind = BudgetLineKind.Expense, decimal amount = 25_000m) =>
        new(categoryId ?? LedgerCategories.WriteOffId, kind, "Venue", amount, 10, null);

    [Fact]
    public void Every_save_creates_the_next_version_with_a_reason()
    {
        var budget = EventBudget.Create(Guid.NewGuid(), Now);

        var (v1, lines1) = budget.SaveVersion([Line()], "First draft", Actor, Now);

        Assert.Equal(1, v1.VersionNumber);
        Assert.Equal("First draft", v1.Reason);
        Assert.Equal(v1.Id, budget.CurrentVersionId);
        Assert.Single(lines1);
        Assert.Equal(v1.Id, lines1[0].VersionId);

        var (v2, _) = budget.SaveVersion([Line(amount: 30_000m)], "Venue quote went up", Actor, Now.AddDays(1));

        Assert.Equal(2, v2.VersionNumber);
        Assert.Equal(v2.Id, budget.CurrentVersionId);
        Assert.Equal(2, budget.VersionCount);
    }

    [Fact]
    public void A_save_needs_a_reason_and_at_least_one_line()
    {
        var budget = EventBudget.Create(Guid.NewGuid(), Now);

        Assert.Throws<DomainException>(() => budget.SaveVersion([Line()], "   ", Actor, Now));
        Assert.Throws<DomainException>(() => budget.SaveVersion([], "Empty", Actor, Now));
    }

    [Fact]
    public void A_category_appears_once_per_side_so_an_actual_maps_to_one_line()
    {
        var budget = EventBudget.Create(Guid.NewGuid(), Now);
        var category = LedgerCategories.WriteOffId;

        Assert.Throws<DomainException>(() =>
            budget.SaveVersion([Line(category), Line(category)], "Duplicate", Actor, Now));

        // The same category on the other side is a different line, and is allowed.
        var (_, lines) = budget.SaveVersion(
            [Line(category), Line(category, BudgetLineKind.Income)], "Both sides", Actor, Now);
        Assert.Equal(2, lines.Count);
    }

    [Fact]
    public void A_budget_line_needs_a_label_a_category_and_a_real_amount()
    {
        var budget = EventBudget.Create(Guid.NewGuid(), Now);

        Assert.Throws<DomainException>(() =>
            budget.SaveVersion([new NewBudgetLine(LedgerCategories.WriteOffId, BudgetLineKind.Expense, " ", 10m, 1, null)], "No label", Actor, Now));
        Assert.Throws<DomainException>(() =>
            budget.SaveVersion([new NewBudgetLine(Guid.Empty, BudgetLineKind.Expense, "Venue", 10m, 1, null)], "No category", Actor, Now));
        Assert.Throws<DomainException>(() =>
            budget.SaveVersion([Line(amount: 0m)], "No amount", Actor, Now));
    }
}

public class ExpenseRequestTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 8, 0, 0, TimeSpan.Zero);

    private static ExpenseRequest New() =>
        ExpenseRequest.Create("Year-end party", "Venue and food for 40", 40_000m,
            LedgerCategories.WriteOffId, new DateOnly(2026, 12, 15), Guid.NewGuid(), Now);

    [Fact]
    public void A_request_starts_pending()
    {
        var r = New();

        Assert.Equal(ExpenseRequestStatus.Pending, r.Status);
        Assert.False(r.IsDecided);
        Assert.Equal(40_000m, r.Amount);
    }

    [Fact]
    public void Approving_then_paying_records_the_ledger_entry()
    {
        var r = New();
        var cfo = Guid.NewGuid();

        r.Approve(cfo, "Booked at the usual place", Now.AddDays(1));

        Assert.Equal(ExpenseRequestStatus.Approved, r.Status);
        Assert.Equal(cfo, r.DecidedById);
        Assert.True(r.IsDecided);

        var entryId = Guid.NewGuid();
        r.MarkPaid(entryId, cfo, Now.AddDays(2));

        Assert.Equal(ExpenseRequestStatus.Paid, r.Status);
        Assert.Equal(entryId, r.PaidEntryId);
    }

    [Fact]
    public void A_rejection_always_carries_its_reason()
    {
        var r = New();

        Assert.Throws<DomainException>(() => r.Reject("  ", Guid.NewGuid(), Now));

        r.Reject("No budget this quarter", Guid.NewGuid(), Now);

        Assert.Equal(ExpenseRequestStatus.Rejected, r.Status);
        Assert.Equal("No budget this quarter", r.DecisionReason);
    }

    [Fact]
    public void A_request_is_paid_only_after_it_is_approved_and_only_once()
    {
        var r = New();
        var cfo = Guid.NewGuid();

        Assert.Throws<DomainException>(() => r.MarkPaid(Guid.NewGuid(), cfo, Now));

        r.Approve(cfo, null, Now);
        r.MarkPaid(Guid.NewGuid(), cfo, Now.AddDays(1));

        Assert.Throws<DomainException>(() => r.MarkPaid(Guid.NewGuid(), cfo, Now.AddDays(2)));
    }

    [Fact]
    public void A_decided_request_is_not_decided_again_or_cancelled()
    {
        var r = New();
        var cfo = Guid.NewGuid();
        r.Approve(cfo, null, Now);

        Assert.Throws<DomainException>(() => r.Approve(cfo, null, Now));
        Assert.Throws<DomainException>(() => r.Reject("Changed my mind", cfo, Now));
        Assert.Throws<DomainException>(() => r.Cancel("Not needed", cfo, Now));
    }

    [Fact]
    public void A_pending_request_is_cancelled_with_a_reason()
    {
        var r = New();

        Assert.Throws<DomainException>(() => r.Cancel(" ", Guid.NewGuid(), Now));

        r.Cancel("We used the sponsor's venue", Guid.NewGuid(), Now);

        Assert.Equal(ExpenseRequestStatus.Cancelled, r.Status);
        Assert.Equal("We used the sponsor's venue", r.CancelReason);
    }
}

public class LedgerCategoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_code_is_lower_snake_case()
    {
        var category = LedgerCategory.Create("Venue_Costs", "Venue", BudgetLineKind.Expense, 10, Now);
        Assert.Equal("venue_costs", category.Code);

        Assert.Throws<DomainException>(() => LedgerCategory.Create("venue costs", "Venue", BudgetLineKind.Expense, 10, Now));
        Assert.Throws<DomainException>(() => LedgerCategory.Create("venue-costs", "Venue", BudgetLineKind.Expense, 10, Now));
        Assert.Throws<DomainException>(() => LedgerCategory.Create("  ", "Venue", BudgetLineKind.Expense, 10, Now));
    }

    [Fact]
    public void A_system_category_is_never_deactivated_because_posting_relies_on_it()
    {
        var system = LedgerCategory.System(LedgerCategories.TicketsId, LedgerCategories.TicketsCode, "Ticket sales", BudgetLineKind.Income, 10, Now);

        Assert.True(system.IsSystem);
        Assert.Throws<DomainException>(() => system.Deactivate("Not used", Now));

        var own = LedgerCategory.Create("venue", "Venue", BudgetLineKind.Expense, 20, Now);
        own.Deactivate("We moved venues", Now);

        Assert.False(own.IsActive);
        Assert.Throws<DomainException>(() => own.Rename("Venue 2", 20));
        Assert.Throws<DomainException>(() => own.EnsureActive());
    }

    [Fact]
    public void Every_seeded_category_is_a_system_category_with_a_distinct_id_and_code()
    {
        var seeded = LedgerCategories.Seeded;

        Assert.Equal(seeded.Count, seeded.Select(c => c.Id).Distinct().Count());
        Assert.Equal(seeded.Count, seeded.Select(c => c.Code).Distinct().Count());
        foreach (var (id, code, name, kind, sort) in seeded)
        {
            var category = LedgerCategory.System(id, code, name, kind, sort, Now);
            Assert.True(category.IsSystem);
            Assert.True(category.IsActive);
            Assert.Equal(code, category.Code);
        }
    }
}

public class FinancePeriodTests
{
    [Fact]
    public void A_receipt_gets_the_manila_calendar_date_not_the_utc_one()
    {
        // 31 January 23:00 UTC is already 1 February in Manila.
        Assert.Equal(new DateOnly(2026, 2, 1), ManilaTime.ManilaDate(new DateTimeOffset(2026, 1, 31, 23, 0, 0, TimeSpan.Zero)));
        // 1 January 00:30 +08:00 is 31 December in UTC, but January here.
        Assert.Equal(new DateOnly(2026, 1, 1), ManilaTime.ManilaDate(new DateTimeOffset(2026, 1, 1, 0, 30, 0, ManilaTime.Offset)));
    }

    [Fact]
    public void A_month_window_is_half_open_in_manila_time()
    {
        var january = ManilaTime.Month(new DateTimeOffset(2026, 1, 1, 0, 30, 0, ManilaTime.Offset));

        Assert.Equal(2026, january.Year);
        Assert.Equal(1, january.Month);
        Assert.Equal(new DateOnly(2026, 1, 1), january.Start);
        Assert.Equal(new DateOnly(2026, 2, 1), january.EndExclusive);
        Assert.True(january.Contains(new DateOnly(2026, 1, 31)));
        Assert.False(january.Contains(new DateOnly(2026, 2, 1)));
    }

    [Fact]
    public void December_rolls_into_the_next_year()
    {
        var december = ManilaTime.Month(2026, 12);

        Assert.Equal(new DateOnly(2026, 12, 1), december.Start);
        Assert.Equal(new DateOnly(2027, 1, 1), december.EndExclusive);
    }

    [Theory]
    [InlineData(2026, 0)]
    [InlineData(2026, 13)]
    [InlineData(2019, 1)]
    public void A_month_window_is_a_real_month(int year, int month) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => ManilaTime.Month(year, month));
}
