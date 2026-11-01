using Htn.Domain.Events;
using Htn.Domain.Files;
using Htn.Domain.Finance;
using Htn.Domain.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Htn.Infrastructure.Data.Configurations;

internal sealed class FundConfiguration : IEntityTypeConfiguration<Fund>
{
    public const string OrganizationIndex = "ux_funds_organization";
    public const string EventIndex = "ux_funds_event";

    public void Configure(EntityTypeBuilder<Fund> b)
    {
        b.ToTable("funds", t =>
        {
            t.HasCheckConstraint("ck_funds_kind", CheckConstraints.In("kind", Enum.GetNames<FundKind>()));
            // The Organization fund has no event; an event fund always has one.
            t.HasCheckConstraint("ck_funds_event", "(kind = 'Event') = (event_id IS NOT NULL)");
            t.HasCheckConstraint("ck_funds_swept",
                "(swept_at IS NULL) = (swept_by_transfer_group_id IS NULL) " +
                "AND (swept_at IS NULL OR kind = 'Event') AND (swept_at IS NULL OR swept_at >= created_at)");
            // Only a swept event fund closes, and never before it was swept.
            t.HasCheckConstraint("ck_funds_closed",
                "closed_at IS NULL OR kind = 'Organization' OR (swept_at IS NOT NULL AND closed_at >= swept_at)");
        });
        b.HasKey(f => f.Id);
        b.Property(f => f.Id).ValueGeneratedNever();
        b.Property(f => f.Kind).HasConversion<string>().HasMaxLength(16);
        b.Property(f => f.Name).HasMaxLength(Fund.NameMaxLength).IsRequired();
        b.Property<uint>(EventConfiguration.RowVersion).HasColumnName("xmin").HasColumnType("xid").IsRowVersion();
        b.Ignore(f => f.IsClosed);
        b.Ignore(f => f.IsSwept);
        // Exactly one Organization fund, and at most one fund per event.
        b.HasIndex(f => f.Kind).IsUnique().HasDatabaseName(OrganizationIndex).HasFilter("kind = 'Organization'");
        b.HasIndex(f => f.EventId).IsUnique().HasDatabaseName(EventIndex).HasFilter("event_id IS NOT NULL");
        b.HasOne<Event>().WithMany().HasForeignKey(f => f.EventId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class LedgerCategoryConfiguration : IEntityTypeConfiguration<LedgerCategory>
{
    public const string CodeIndex = "ux_ledger_categories_code";

    public void Configure(EntityTypeBuilder<LedgerCategory> b)
    {
        b.ToTable("ledger_categories", t =>
        {
            t.HasCheckConstraint("ck_ledger_categories_kind", CheckConstraints.In("kind", Enum.GetNames<BudgetLineKind>()));
            t.HasCheckConstraint("ck_ledger_categories_code", "code ~ '^[a-z0-9_]+$'");
            t.HasCheckConstraint("ck_ledger_categories_deactivated",
                "(is_active AND deactivated_at IS NULL AND deactivation_reason IS NULL) OR " +
                "(NOT is_active AND deactivated_at IS NOT NULL AND deactivation_reason IS NOT NULL)");
            // A seeded category is relied on by automatic posting, so it can never be deactivated.
            t.HasCheckConstraint("ck_ledger_categories_system_active", "NOT is_system OR is_active");
        });
        b.HasKey(c => c.Id);
        b.Property(c => c.Id).ValueGeneratedNever();
        b.Property(c => c.Code).HasMaxLength(LedgerCategory.CodeMaxLength).IsRequired();
        b.Property(c => c.Name).HasMaxLength(LedgerCategory.NameMaxLength).IsRequired();
        b.Property(c => c.Kind).HasConversion<string>().HasMaxLength(16);
        b.Property(c => c.DeactivationReason).HasMaxLength(LedgerCategory.ReasonMaxLength);
        b.Property<uint>(EventConfiguration.RowVersion).HasColumnName("xmin").HasColumnType("xid").IsRowVersion();
        b.HasIndex(c => c.Code).IsUnique().HasDatabaseName(CodeIndex);
        b.HasIndex(c => new { c.IsActive, c.SortOrder });
    }
}

internal sealed class LedgerEntryConfiguration : IEntityTypeConfiguration<LedgerEntry>
{
    public const string SourceIndex = "ux_ledger_entries_source";
    public const string PendingIndex = "ix_ledger_entries_pending";
    public const string CategoryIndex = "ix_ledger_entries_category";
    public const string TransferGroupIndex = "ix_ledger_entries_transfer_group";

    public void Configure(EntityTypeBuilder<LedgerEntry> b)
    {
        b.ToTable("ledger_entries", t =>
        {
            t.HasCheckConstraint("ck_ledger_entries_direction", CheckConstraints.In("direction", Enum.GetNames<LedgerDirection>()));
            t.HasCheckConstraint("ck_ledger_entries_status", CheckConstraints.In("status", Enum.GetNames<LedgerStatus>()));
            t.HasCheckConstraint("ck_ledger_entries_source", CheckConstraints.In("source", Enum.GetNames<LedgerSource>()));
            // Money never moves by zero, and never by fractions of a centavo.
            t.HasCheckConstraint("ck_ledger_entries_amount", "amount > 0 AND round(amount, 2) = amount");
            t.HasCheckConstraint("ck_ledger_entries_description", "char_length(description) > 0");
            // Pending is never approved and Posted always is. A Voided row keeps whatever approval it had: voiding a
            // posted entry does not unmake the fact that it was approved, and voiding a pending one never approved it.
            t.HasCheckConstraint("ck_ledger_entries_approved",
                "(status <> 'Pending' OR approved_at IS NULL) AND (status <> 'Posted' OR approved_at IS NOT NULL) " +
                "AND (approved_at IS NULL OR approved_at >= entered_at)");
            t.HasCheckConstraint("ck_ledger_entries_void",
                "(status = 'Voided') = (voided_at IS NOT NULL) AND (voided_at IS NULL) = (void_reason IS NULL) " +
                "AND (voided_at IS NULL OR voided_at >= entered_at)");
            // A receipt is required for everything a person types in; a system row has none to attach.
            t.HasCheckConstraint("ck_ledger_entries_receipt",
                $"source NOT IN ('{nameof(LedgerSource.Manual)}', '{nameof(LedgerSource.ExpenseRequest)}') " +
                "OR receipt_file_id IS NOT NULL");
            t.HasCheckConstraint("ck_ledger_entries_transfer",
                "(source IN ('Transfer', 'SurplusSweep')) = (transfer_group_id IS NOT NULL)");
            t.HasCheckConstraint("ck_ledger_entries_contra", "(source = 'Contra') = (contra_of_entry_id IS NOT NULL)");
            // Only a posted row is ever stamped into a closed month.
            t.HasCheckConstraint("ck_ledger_entries_closed", "month_close_id IS NULL OR status = 'Posted'");
        });
        b.HasKey(e => e.Id);
        b.Property(e => e.Id).ValueGeneratedNever();
        b.Property(e => e.Direction).HasConversion<string>().HasMaxLength(8);
        b.Property(e => e.Status).HasConversion<string>().HasMaxLength(16);
        b.Property(e => e.Source).HasConversion<string>().HasMaxLength(24);
        b.Property(e => e.Reference).HasMaxLength(LedgerEntry.ReferenceMaxLength);
        b.Property(e => e.Description).HasMaxLength(LedgerEntry.DescriptionMaxLength).IsRequired();
        b.Property(e => e.VoidReason).HasMaxLength(LedgerEntry.ReasonMaxLength);
        b.Property<uint>(EventConfiguration.RowVersion).HasColumnName("xmin").HasColumnType("xid").IsRowVersion();
        b.Ignore(e => e.SignedAmount);
        b.Ignore(e => e.IsPosted);
        b.Ignore(e => e.IsVoided);
        b.Ignore(e => e.IsClosed);

        // Fund balance, cashflow and budget actuals: WHERE fund_id = ? AND status = 'Posted' [AND entry_date range].
        b.HasIndex(e => new { e.FundId, e.Status, e.EntryDate });
        // The balancing screen: book balance per account up to a date.
        b.HasIndex(e => new { e.PaymentAccountId, e.Status, e.EntryDate });
        // Budget actuals per category, for the event fund.
        b.HasIndex(e => new { e.FundId, e.CategoryId }).HasDatabaseName(CategoryIndex).HasFilter("status = 'Posted'");
        // The month-close precondition: is anything still pending?
        b.HasIndex(e => e.EntryDate).HasDatabaseName(PendingIndex).HasFilter("status = 'Pending'");
        // One live entry per source row, forever: the idempotency guarantee for automatic posting. A voided entry
        // drops out of the index on purpose, so a corrected payment can be posted again.
        b.HasIndex(e => new { e.Source, e.SourceId }).IsUnique().HasDatabaseName(SourceIndex)
            .HasFilter("source_id IS NOT NULL AND voided_at IS NULL");
        b.HasIndex(e => e.TransferGroupId).HasDatabaseName(TransferGroupIndex).HasFilter("transfer_group_id IS NOT NULL");
        // Default ledger order: newest first, with a stable tie-break.
        b.HasIndex(e => new { e.EntryDate, e.Id });

        b.HasOne<Fund>().WithMany().HasForeignKey(e => e.FundId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<PaymentAccount>().WithMany().HasForeignKey(e => e.PaymentAccountId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<LedgerCategory>().WithMany().HasForeignKey(e => e.CategoryId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<StoredFile>().WithMany().HasForeignKey(e => e.ReceiptFileId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<MonthClose>().WithMany().HasForeignKey(e => e.MonthCloseId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<LedgerEntry>().WithMany().HasForeignKey(e => e.ContraOfEntryId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class MonthCloseConfiguration : IEntityTypeConfiguration<MonthClose>
{
    public const string OpenIndex = "ux_month_closes_open";

    public void Configure(EntityTypeBuilder<MonthClose> b)
    {
        b.ToTable("month_closes", t =>
        {
            t.HasCheckConstraint("ck_month_closes_period", "year BETWEEN 2020 AND 2100 AND month BETWEEN 1 AND 12");
            t.HasCheckConstraint("ck_month_closes_reopened",
                "(reopened_at IS NULL) = (reopen_reason IS NULL) AND (reopened_at IS NULL) = (reopened_by_id IS NULL) " +
                "AND (reopened_at IS NULL OR reopened_at >= closed_at)");
        });
        b.HasKey(m => m.Id);
        b.Property(m => m.Id).ValueGeneratedNever();
        b.Property(m => m.Note).HasMaxLength(MonthClose.NoteMaxLength);
        b.Property(m => m.ReopenReason).HasMaxLength(MonthClose.ReasonMaxLength);
        b.Property<uint>(EventConfiguration.RowVersion).HasColumnName("xmin").HasColumnType("xid").IsRowVersion();
        b.Ignore(m => m.IsOpen);
        // A month is closed at most once at a time; reopening frees it to be closed again.
        b.HasIndex(m => new { m.Year, m.Month }).IsUnique().HasDatabaseName(OpenIndex).HasFilter("reopened_at IS NULL");
    }
}

internal sealed class AccountReconciliationConfiguration : IEntityTypeConfiguration<AccountReconciliation>
{
    public const string OpenIndex = "ux_account_reconciliations_open";

    public void Configure(EntityTypeBuilder<AccountReconciliation> b)
    {
        b.ToTable("account_reconciliations", t =>
        {
            t.HasCheckConstraint("ck_account_reconciliations_period", "year BETWEEN 2020 AND 2100 AND month BETWEEN 1 AND 12");
            t.HasCheckConstraint("ck_account_reconciliations_amounts",
                "round(statement_balance, 2) = statement_balance AND round(book_balance, 2) = book_balance " +
                "AND difference = statement_balance - book_balance");
            t.HasCheckConstraint("ck_account_reconciliations_superseded",
                "(superseded_at IS NULL) = (supersede_reason IS NULL) AND (superseded_at IS NULL) = (superseded_by_id IS NULL) " +
                "AND (superseded_at IS NULL OR superseded_at >= reconciled_at)");
        });
        b.HasKey(r => r.Id);
        b.Property(r => r.Id).ValueGeneratedNever();
        b.Property(r => r.Note).HasMaxLength(AccountReconciliation.NoteMaxLength);
        b.Property(r => r.SupersedeReason).HasMaxLength(AccountReconciliation.ReasonMaxLength);
        b.Property<uint>(EventConfiguration.RowVersion).HasColumnName("xmin").HasColumnType("xid").IsRowVersion();
        b.Ignore(r => r.IsLive);
        b.Ignore(r => r.IsBalanced);
        // One live reconciliation per account and month: the newest one is the truth.
        b.HasIndex(r => new { r.PaymentAccountId, r.Year, r.Month }).IsUnique().HasDatabaseName(OpenIndex)
            .HasFilter("superseded_at IS NULL");
        b.HasOne<PaymentAccount>().WithMany().HasForeignKey(r => r.PaymentAccountId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class EventBudgetConfiguration : IEntityTypeConfiguration<EventBudget>
{
    public const string EventIndex = "ux_event_budgets_event";

    public void Configure(EntityTypeBuilder<EventBudget> b)
    {
        b.ToTable("event_budgets", t =>
            t.HasCheckConstraint("ck_event_budgets_versions", "version_count >= 0 AND updated_at >= created_at"));
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property<uint>(EventConfiguration.RowVersion).HasColumnName("xmin").HasColumnType("xid").IsRowVersion();
        b.HasIndex(x => x.EventId).IsUnique().HasDatabaseName(EventIndex);
        b.HasOne<Event>().WithMany().HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class EventBudgetVersionConfiguration : IEntityTypeConfiguration<EventBudgetVersion>
{
    public const string NumberIndex = "ux_event_budget_versions_number";

    public void Configure(EntityTypeBuilder<EventBudgetVersion> b)
    {
        b.ToTable("event_budget_versions", t =>
            t.HasCheckConstraint("ck_event_budget_versions_number", "version_number >= 1 AND char_length(reason) > 0"));
        b.HasKey(v => v.Id);
        b.Property(v => v.Id).ValueGeneratedNever();
        b.Property(v => v.Reason).HasMaxLength(EventBudgetVersion.ReasonMaxLength).IsRequired();
        b.HasIndex(v => new { v.BudgetId, v.VersionNumber }).IsUnique().HasDatabaseName(NumberIndex);
        b.HasOne<EventBudget>().WithMany().HasForeignKey(v => v.BudgetId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class EventBudgetLineConfiguration : IEntityTypeConfiguration<EventBudgetLine>
{
    public const string CategoryIndex = "ux_event_budget_lines_category";

    public void Configure(EntityTypeBuilder<EventBudgetLine> b)
    {
        b.ToTable("event_budget_lines", t =>
        {
            t.HasCheckConstraint("ck_event_budget_lines_kind", CheckConstraints.In("kind", Enum.GetNames<BudgetLineKind>()));
            t.HasCheckConstraint("ck_event_budget_lines_amount",
                "amount_budgeted > 0 AND round(amount_budgeted, 2) = amount_budgeted");
        });
        b.HasKey(l => l.Id);
        b.Property(l => l.Id).ValueGeneratedNever();
        b.Property(l => l.Kind).HasConversion<string>().HasMaxLength(16);
        b.Property(l => l.Label).HasMaxLength(EventBudgetLine.LabelMaxLength).IsRequired();
        b.Property(l => l.Note).HasMaxLength(EventBudgetLine.NoteMaxLength);
        // Each category appears once per side within a version, so an actual maps to exactly one line.
        b.HasIndex(l => new { l.VersionId, l.CategoryId, l.Kind }).IsUnique().HasDatabaseName(CategoryIndex);
        b.HasOne<EventBudgetVersion>().WithMany().HasForeignKey(l => l.VersionId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<LedgerCategory>().WithMany().HasForeignKey(l => l.CategoryId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ExpenseRequestConfiguration : IEntityTypeConfiguration<ExpenseRequest>
{
    public void Configure(EntityTypeBuilder<ExpenseRequest> b)
    {
        b.ToTable("expense_requests", t =>
        {
            t.HasCheckConstraint("ck_expense_requests_status", CheckConstraints.In("status", Enum.GetNames<ExpenseRequestStatus>()));
            t.HasCheckConstraint("ck_expense_requests_amount", "amount > 0 AND round(amount, 2) = amount");
            // Decided is exactly Approved, Rejected or Paid; a rejection always carries its reason.
            t.HasCheckConstraint("ck_expense_requests_decided",
                "(status IN ('Approved', 'Rejected', 'Paid')) = (decided_at IS NOT NULL) " +
                "AND (status <> 'Rejected' OR decision_reason IS NOT NULL) " +
                "AND (decided_at IS NULL OR decided_at >= requested_at)");
            t.HasCheckConstraint("ck_expense_requests_paid",
                "(status = 'Paid') = (paid_at IS NOT NULL) AND (paid_at IS NULL) = (paid_entry_id IS NULL) " +
                "AND (paid_at IS NULL OR paid_at >= decided_at)");
            t.HasCheckConstraint("ck_expense_requests_cancelled",
                "(status = 'Cancelled') = (cancelled_at IS NOT NULL) AND (cancelled_at IS NULL) = (cancel_reason IS NULL)");
        });
        b.HasKey(r => r.Id);
        b.Property(r => r.Id).ValueGeneratedNever();
        b.Property(r => r.Status).HasConversion<string>().HasMaxLength(16);
        b.Property(r => r.Title).HasMaxLength(ExpenseRequest.TitleMaxLength).IsRequired();
        b.Property(r => r.Description).HasMaxLength(ExpenseRequest.DescriptionMaxLength);
        b.Property(r => r.DecisionReason).HasMaxLength(ExpenseRequest.ReasonMaxLength);
        b.Property(r => r.CancelReason).HasMaxLength(ExpenseRequest.ReasonMaxLength);
        b.Property<uint>(EventConfiguration.RowVersion).HasColumnName("xmin").HasColumnType("xid").IsRowVersion();
        b.Ignore(r => r.IsDecided);
        b.HasIndex(r => new { r.Status, r.RequestedAt });
        b.HasIndex(r => r.RequestedById);
        b.HasOne<LedgerCategory>().WithMany().HasForeignKey(r => r.CategoryId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<LedgerEntry>().WithMany().HasForeignKey(r => r.PaidEntryId).OnDelete(DeleteBehavior.Restrict);
    }
}
