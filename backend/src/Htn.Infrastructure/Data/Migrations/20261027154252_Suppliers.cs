using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Htn.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Suppliers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "suppliers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    category = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    name_key = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    contact_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    contact_email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    contact_phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    issues_or = table.Column<bool>(type: "boolean", nullable: false),
                    tin = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    bank_details = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    logo_file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    deactivated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deactivated_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    deactivate_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_suppliers", x => x.id);
                    table.CheckConstraint("ck_suppliers_category", "category IN ('Venue', 'Catering', 'Printing', 'TechAV', 'Merch', 'Transport')");
                    table.CheckConstraint("ck_suppliers_deactivated", "(deactivated_at IS NULL) = (deactivate_reason IS NULL) AND (deactivated_at IS NULL) = (deactivated_by_id IS NULL)");
                    table.ForeignKey(
                        name: "fk_suppliers_stored_files_logo_file_id",
                        column: x => x.logo_file_id,
                        principalTable: "stored_files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "event_suppliers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    supplier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    category_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_committee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    contract_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    paid_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    next_payment_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    next_payment_due = table.Column<DateOnly>(type: "date", nullable: true),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    voided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    voided_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    void_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_event_suppliers", x => x.id);
                    table.CheckConstraint("ck_event_suppliers_money", "(contract_amount IS NULL OR contract_amount >= 0) AND (paid_amount IS NULL OR paid_amount >= 0) AND (next_payment_amount IS NULL OR next_payment_amount >= 0)");
                    table.CheckConstraint("ck_event_suppliers_status", "status IN ('Quotation', 'Booked', 'DepositPaid', 'FullyPaid')");
                    table.CheckConstraint("ck_event_suppliers_void", "(voided_at IS NULL) = (void_reason IS NULL) AND (voided_at IS NULL) = (voided_by_id IS NULL)");
                    table.ForeignKey(
                        name: "fk_event_suppliers_committees_owner_committee_id",
                        column: x => x.owner_committee_id,
                        principalTable: "committees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_event_suppliers_events_event_id",
                        column: x => x.event_id,
                        principalTable: "events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_event_suppliers_ledger_categories_category_id",
                        column: x => x.category_id,
                        principalTable: "ledger_categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_event_suppliers_suppliers_supplier_id",
                        column: x => x.supplier_id,
                        principalTable: "suppliers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "supplier_documents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    supplier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    file_id = table.Column<Guid>(type: "uuid", nullable: false),
                    uploaded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    uploaded_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    voided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    voided_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    void_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_supplier_documents", x => x.id);
                    table.CheckConstraint("ck_supplier_documents_kind", "kind IN ('Bir2303', 'BusinessPermit', 'Other')");
                    table.CheckConstraint("ck_supplier_documents_void", "(voided_at IS NULL) = (void_reason IS NULL) AND (voided_at IS NULL) = (voided_by_id IS NULL)");
                    table.ForeignKey(
                        name: "fk_supplier_documents_stored_files_file_id",
                        column: x => x.file_id,
                        principalTable: "stored_files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_supplier_documents_suppliers_supplier_id",
                        column: x => x.supplier_id,
                        principalTable: "suppliers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "supplier_ratings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_supplier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rater_id = table.Column<Guid>(type: "uuid", nullable: false),
                    stars = table.Column<int>(type: "integer", nullable: false),
                    tags = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    hire_again = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_supplier_ratings", x => x.id);
                    table.CheckConstraint("ck_supplier_ratings_hire_again", "hire_again IN ('Yes', 'Maybe', 'No')");
                    table.CheckConstraint("ck_supplier_ratings_stars", "stars BETWEEN 1 AND 5");
                    table.ForeignKey(
                        name: "fk_supplier_ratings_event_suppliers_event_supplier_id",
                        column: x => x.event_supplier_id,
                        principalTable: "event_suppliers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_event_suppliers_category_id",
                table: "event_suppliers",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "ix_event_suppliers_event",
                table: "event_suppliers",
                column: "event_id");

            migrationBuilder.CreateIndex(
                name: "ix_event_suppliers_owner_committee",
                table: "event_suppliers",
                column: "owner_committee_id");

            migrationBuilder.CreateIndex(
                name: "ix_event_suppliers_supplier_id",
                table: "event_suppliers",
                column: "supplier_id");

            migrationBuilder.CreateIndex(
                name: "ux_event_suppliers_event_supplier",
                table: "event_suppliers",
                columns: new[] { "event_id", "supplier_id" },
                unique: true,
                filter: "voided_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_supplier_documents_file_id",
                table: "supplier_documents",
                column: "file_id");

            migrationBuilder.CreateIndex(
                name: "ix_supplier_documents_supplier",
                table: "supplier_documents",
                column: "supplier_id");

            migrationBuilder.CreateIndex(
                name: "ux_supplier_ratings_hire_rater",
                table: "supplier_ratings",
                columns: new[] { "event_supplier_id", "rater_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_suppliers_category",
                table: "suppliers",
                column: "category");

            migrationBuilder.CreateIndex(
                name: "ix_suppliers_logo_file_id",
                table: "suppliers",
                column: "logo_file_id");

            migrationBuilder.CreateIndex(
                name: "ux_suppliers_name_key",
                table: "suppliers",
                column: "name_key",
                unique: true,
                filter: "deactivated_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "supplier_documents");

            migrationBuilder.DropTable(
                name: "supplier_ratings");

            migrationBuilder.DropTable(
                name: "event_suppliers");

            migrationBuilder.DropTable(
                name: "suppliers");
        }
    }
}
