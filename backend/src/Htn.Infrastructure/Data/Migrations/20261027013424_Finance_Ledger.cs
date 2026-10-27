using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Htn.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Finance_Ledger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "account_reconciliations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    payment_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    year = table.Column<int>(type: "integer", nullable: false),
                    month = table.Column<int>(type: "integer", nullable: false),
                    statement_balance = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    book_balance = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    difference = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    reconciled_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reconciled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    superseded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    superseded_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    supersede_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_account_reconciliations", x => x.id);
                    table.CheckConstraint("ck_account_reconciliations_amounts", "round(statement_balance, 2) = statement_balance AND round(book_balance, 2) = book_balance AND difference = statement_balance - book_balance");
                    table.CheckConstraint("ck_account_reconciliations_period", "year BETWEEN 2020 AND 2100 AND month BETWEEN 1 AND 12");
                    table.CheckConstraint("ck_account_reconciliations_superseded", "(superseded_at IS NULL) = (supersede_reason IS NULL) AND (superseded_at IS NULL) = (superseded_by_id IS NULL) AND (superseded_at IS NULL OR superseded_at >= reconciled_at)");
                    table.ForeignKey(
                        name: "fk_account_reconciliations_payment_accounts_payment_account_id",
                        column: x => x.payment_account_id,
                        principalTable: "payment_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "event_budgets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    current_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_count = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_event_budgets", x => x.id);
                    table.CheckConstraint("ck_event_budgets_versions", "version_count >= 0 AND updated_at >= created_at");
                    table.ForeignKey(
                        name: "fk_event_budgets_events_event_id",
                        column: x => x.event_id,
                        principalTable: "events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "funds",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: true),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    swept_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    swept_by_transfer_group_id = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_funds", x => x.id);
                    table.CheckConstraint("ck_funds_closed", "closed_at IS NULL OR kind = 'Organization' OR (swept_at IS NOT NULL AND closed_at >= swept_at)");
                    table.CheckConstraint("ck_funds_event", "(kind = 'Event') = (event_id IS NOT NULL)");
                    table.CheckConstraint("ck_funds_kind", "kind IN ('Organization', 'Event')");
                    table.CheckConstraint("ck_funds_swept", "(swept_at IS NULL) = (swept_by_transfer_group_id IS NULL) AND (swept_at IS NULL OR kind = 'Event') AND (swept_at IS NULL OR swept_at >= created_at)");
                    table.ForeignKey(
                        name: "fk_funds_events_event_id",
                        column: x => x.event_id,
                        principalTable: "events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ledger_categories",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    is_system = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deactivated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deactivation_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ledger_categories", x => x.id);
                    table.CheckConstraint("ck_ledger_categories_code", "code ~ '^[a-z0-9_]+$'");
                    table.CheckConstraint("ck_ledger_categories_deactivated", "(is_active AND deactivated_at IS NULL AND deactivation_reason IS NULL) OR (NOT is_active AND deactivated_at IS NOT NULL AND deactivation_reason IS NOT NULL)");
                    table.CheckConstraint("ck_ledger_categories_kind", "kind IN ('Income', 'Expense')");
                    table.CheckConstraint("ck_ledger_categories_system_active", "NOT is_system OR is_active");
                });

            migrationBuilder.CreateTable(
                name: "month_closes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    year = table.Column<int>(type: "integer", nullable: false),
                    month = table.Column<int>(type: "integer", nullable: false),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    closed_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reopened_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reopened_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reopen_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_month_closes", x => x.id);
                    table.CheckConstraint("ck_month_closes_period", "year BETWEEN 2020 AND 2100 AND month BETWEEN 1 AND 12");
                    table.CheckConstraint("ck_month_closes_reopened", "(reopened_at IS NULL) = (reopen_reason IS NULL) AND (reopened_at IS NULL) = (reopened_by_id IS NULL) AND (reopened_at IS NULL OR reopened_at >= closed_at)");
                });

            migrationBuilder.CreateTable(
                name: "event_budget_versions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    budget_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_number = table.Column<int>(type: "integer", nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_event_budget_versions", x => x.id);
                    table.CheckConstraint("ck_event_budget_versions_number", "version_number >= 1 AND char_length(reason) > 0");
                    table.ForeignKey(
                        name: "fk_event_budget_versions_event_budgets_budget_id",
                        column: x => x.budget_id,
                        principalTable: "event_budgets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ledger_entries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    fund_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payment_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    category_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entry_date = table.Column<DateOnly>(type: "date", nullable: false),
                    direction = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    receipt_file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    source = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: true),
                    transfer_group_id = table.Column<Guid>(type: "uuid", nullable: true),
                    contra_of_entry_id = table.Column<Guid>(type: "uuid", nullable: true),
                    month_close_id = table.Column<Guid>(type: "uuid", nullable: true),
                    entered_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    entered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    approved_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    voided_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    voided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    void_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ledger_entries", x => x.id);
                    table.CheckConstraint("ck_ledger_entries_amount", "amount > 0 AND round(amount, 2) = amount");
                    table.CheckConstraint("ck_ledger_entries_approved", "(status <> 'Pending' OR approved_at IS NULL) AND (status <> 'Posted' OR approved_at IS NOT NULL) AND (approved_at IS NULL OR approved_at >= entered_at)");
                    table.CheckConstraint("ck_ledger_entries_closed", "month_close_id IS NULL OR status = 'Posted'");
                    table.CheckConstraint("ck_ledger_entries_contra", "(source = 'Contra') = (contra_of_entry_id IS NOT NULL)");
                    table.CheckConstraint("ck_ledger_entries_description", "char_length(description) > 0");
                    table.CheckConstraint("ck_ledger_entries_direction", "direction IN ('In', 'Out')");
                    table.CheckConstraint("ck_ledger_entries_receipt", "source NOT IN ('Manual', 'ExpenseRequest') OR receipt_file_id IS NOT NULL");
                    table.CheckConstraint("ck_ledger_entries_source", "source IN ('Manual', 'TicketPayment', 'Contra', 'Transfer', 'SurplusSweep', 'ExpenseRequest', 'SponsorPayment', 'SupplierPayment', 'MerchSale', 'MembershipPayment', 'PopUpWriteOff')");
                    table.CheckConstraint("ck_ledger_entries_status", "status IN ('Pending', 'Posted', 'Voided')");
                    table.CheckConstraint("ck_ledger_entries_transfer", "(source IN ('Transfer', 'SurplusSweep')) = (transfer_group_id IS NOT NULL)");
                    table.CheckConstraint("ck_ledger_entries_void", "(status = 'Voided') = (voided_at IS NOT NULL) AND (voided_at IS NULL) = (void_reason IS NULL) AND (voided_at IS NULL OR voided_at >= entered_at)");
                    table.ForeignKey(
                        name: "fk_ledger_entries_funds_fund_id",
                        column: x => x.fund_id,
                        principalTable: "funds",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ledger_entries_ledger_categories_category_id",
                        column: x => x.category_id,
                        principalTable: "ledger_categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ledger_entries_ledger_entries_contra_of_entry_id",
                        column: x => x.contra_of_entry_id,
                        principalTable: "ledger_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ledger_entries_month_closes_month_close_id",
                        column: x => x.month_close_id,
                        principalTable: "month_closes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ledger_entries_payment_accounts_payment_account_id",
                        column: x => x.payment_account_id,
                        principalTable: "payment_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ledger_entries_stored_files_receipt_file_id",
                        column: x => x.receipt_file_id,
                        principalTable: "stored_files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "event_budget_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    category_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    label = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    amount_budgeted = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    note = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_event_budget_lines", x => x.id);
                    table.CheckConstraint("ck_event_budget_lines_amount", "amount_budgeted > 0 AND round(amount_budgeted, 2) = amount_budgeted");
                    table.CheckConstraint("ck_event_budget_lines_kind", "kind IN ('Income', 'Expense')");
                    table.ForeignKey(
                        name: "fk_event_budget_lines_event_budget_versions_version_id",
                        column: x => x.version_id,
                        principalTable: "event_budget_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_event_budget_lines_ledger_categories_category_id",
                        column: x => x.category_id,
                        principalTable: "ledger_categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "expense_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    category_id = table.Column<Guid>(type: "uuid", nullable: false),
                    needed_by = table.Column<DateOnly>(type: "date", nullable: true),
                    requested_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    decided_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    decision_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    paid_entry_id = table.Column<Guid>(type: "uuid", nullable: true),
                    paid_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    paid_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancelled_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    cancel_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_expense_requests", x => x.id);
                    table.CheckConstraint("ck_expense_requests_amount", "amount > 0 AND round(amount, 2) = amount");
                    table.CheckConstraint("ck_expense_requests_cancelled", "(status = 'Cancelled') = (cancelled_at IS NOT NULL) AND (cancelled_at IS NULL) = (cancel_reason IS NULL)");
                    table.CheckConstraint("ck_expense_requests_decided", "(status IN ('Approved', 'Rejected', 'Paid')) = (decided_at IS NOT NULL) AND (status <> 'Rejected' OR decision_reason IS NOT NULL) AND (decided_at IS NULL OR decided_at >= requested_at)");
                    table.CheckConstraint("ck_expense_requests_paid", "(status = 'Paid') = (paid_at IS NOT NULL) AND (paid_at IS NULL) = (paid_entry_id IS NULL) AND (paid_at IS NULL OR paid_at >= decided_at)");
                    table.CheckConstraint("ck_expense_requests_status", "status IN ('Pending', 'Approved', 'Rejected', 'Paid', 'Cancelled')");
                    table.ForeignKey(
                        name: "fk_expense_requests_ledger_categories_category_id",
                        column: x => x.category_id,
                        principalTable: "ledger_categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_expense_requests_ledger_entries_paid_entry_id",
                        column: x => x.paid_entry_id,
                        principalTable: "ledger_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_account_reconciliations_open",
                table: "account_reconciliations",
                columns: new[] { "payment_account_id", "year", "month" },
                unique: true,
                filter: "superseded_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_event_budget_lines_category_id",
                table: "event_budget_lines",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "ux_event_budget_lines_category",
                table: "event_budget_lines",
                columns: new[] { "version_id", "category_id", "kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_event_budget_versions_number",
                table: "event_budget_versions",
                columns: new[] { "budget_id", "version_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_event_budgets_event",
                table: "event_budgets",
                column: "event_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_expense_requests_category_id",
                table: "expense_requests",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "ix_expense_requests_paid_entry_id",
                table: "expense_requests",
                column: "paid_entry_id");

            migrationBuilder.CreateIndex(
                name: "ix_expense_requests_requested_by_id",
                table: "expense_requests",
                column: "requested_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_expense_requests_status_requested_at",
                table: "expense_requests",
                columns: new[] { "status", "requested_at" });

            migrationBuilder.CreateIndex(
                name: "ux_funds_event",
                table: "funds",
                column: "event_id",
                unique: true,
                filter: "event_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_funds_organization",
                table: "funds",
                column: "kind",
                unique: true,
                filter: "kind = 'Organization'");

            migrationBuilder.CreateIndex(
                name: "ix_ledger_categories_is_active_sort_order",
                table: "ledger_categories",
                columns: new[] { "is_active", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ux_ledger_categories_code",
                table: "ledger_categories",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ledger_entries_category",
                table: "ledger_entries",
                columns: new[] { "fund_id", "category_id" },
                filter: "status = 'Posted'");

            migrationBuilder.CreateIndex(
                name: "ix_ledger_entries_category_id",
                table: "ledger_entries",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "ix_ledger_entries_contra_of_entry_id",
                table: "ledger_entries",
                column: "contra_of_entry_id");

            migrationBuilder.CreateIndex(
                name: "ix_ledger_entries_entry_date_id",
                table: "ledger_entries",
                columns: new[] { "entry_date", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_ledger_entries_fund_id_status_entry_date",
                table: "ledger_entries",
                columns: new[] { "fund_id", "status", "entry_date" });

            migrationBuilder.CreateIndex(
                name: "ix_ledger_entries_month_close_id",
                table: "ledger_entries",
                column: "month_close_id");

            migrationBuilder.CreateIndex(
                name: "ix_ledger_entries_payment_account_id_status_entry_date",
                table: "ledger_entries",
                columns: new[] { "payment_account_id", "status", "entry_date" });

            migrationBuilder.CreateIndex(
                name: "ix_ledger_entries_pending",
                table: "ledger_entries",
                column: "entry_date",
                filter: "status = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "ix_ledger_entries_receipt_file_id",
                table: "ledger_entries",
                column: "receipt_file_id");

            migrationBuilder.CreateIndex(
                name: "ix_ledger_entries_transfer_group",
                table: "ledger_entries",
                column: "transfer_group_id",
                filter: "transfer_group_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_ledger_entries_source",
                table: "ledger_entries",
                columns: new[] { "source", "source_id" },
                unique: true,
                filter: "source_id IS NOT NULL AND voided_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_month_closes_open",
                table: "month_closes",
                columns: new[] { "year", "month" },
                unique: true,
                filter: "reopened_at IS NULL");

            // The Organization fund and the system categories, with the literal ids held in Htn.Domain.Finance
            // (Funds.OrganizationId, LedgerCategories.*): automatic posting resolves them by id, so the migration,
            // the seeder and the posting code must all agree.
            migrationBuilder.Sql("""
                INSERT INTO funds (id, kind, event_id, name, created_at)
                VALUES ('0199c4a0-0000-7000-8000-000000000001', 'Organization', NULL, 'Organization', now())
                ON CONFLICT DO NOTHING;

                INSERT INTO ledger_categories (id, code, name, kind, is_system, is_active, sort_order, created_at)
                VALUES
                    ('0199c4a0-0000-7000-8000-000000000011', 'tickets',         'Ticket sales',           'Income',  true, true,  10, now()),
                    ('0199c4a0-0000-7000-8000-000000000012', 'sponsorship',     'Sponsorship',            'Income',  true, true,  20, now()),
                    ('0199c4a0-0000-7000-8000-000000000013', 'merch',           'Merch sales',            'Income',  true, true,  30, now()),
                    ('0199c4a0-0000-7000-8000-000000000014', 'membership_dues', 'Membership dues',        'Income',  true, true,  40, now()),
                    ('0199c4a0-0000-7000-8000-000000000015', 'transfer',        'Transfer between funds', 'Income',  true, true, 900, now()),
                    ('0199c4a0-0000-7000-8000-000000000016', 'write_off',       'Write-off',              'Expense', true, true, 910, now())
                ON CONFLICT DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "account_reconciliations");

            migrationBuilder.DropTable(
                name: "event_budget_lines");

            migrationBuilder.DropTable(
                name: "expense_requests");

            migrationBuilder.DropTable(
                name: "event_budget_versions");

            migrationBuilder.DropTable(
                name: "ledger_entries");

            migrationBuilder.DropTable(
                name: "event_budgets");

            migrationBuilder.DropTable(
                name: "funds");

            migrationBuilder.DropTable(
                name: "ledger_categories");

            migrationBuilder.DropTable(
                name: "month_closes");
        }
    }
}
