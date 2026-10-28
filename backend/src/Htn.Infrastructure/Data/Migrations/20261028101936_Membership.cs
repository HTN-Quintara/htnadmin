using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Htn.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Membership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_merch_sales_money",
                table: "merch_sales");

            migrationBuilder.AddColumn<decimal>(
                name: "membership_fee_amount",
                table: "merch_sales",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "membership_fee_included",
                table: "merch_sales",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "memberships",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    membership_year = table.Column<int>(type: "integer", nullable: false),
                    fee_paid = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    paid_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    paid_via = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: true),
                    recorded_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_memberships", x => x.id);
                    table.CheckConstraint("ck_memberships_fee", "fee_paid >= 0");
                    table.CheckConstraint("ck_memberships_paid_via", "paid_via IN ('Pos', 'RecordedManually')");
                    table.CheckConstraint("ck_memberships_valid_until", "valid_until > paid_at");
                    table.CheckConstraint("ck_memberships_year", "membership_year BETWEEN 2000 AND 2100");
                    table.ForeignKey(
                        name: "fk_memberships_events_event_id",
                        column: x => x.event_id,
                        principalTable: "events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_memberships_people_person_id",
                        column: x => x.person_id,
                        principalTable: "people",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_merch_sales_money",
                table: "merch_sales",
                sql: "subtotal >= 0 AND discount_amount >= 0 AND total >= 0 AND total = subtotal - discount_amount + COALESCE(membership_fee_amount, 0) AND (membership_fee_included = FALSE) = (membership_fee_amount IS NULL) AND (membership_fee_amount IS NULL OR membership_fee_amount >= 0)");

            migrationBuilder.CreateIndex(
                name: "ix_memberships_event_id",
                table: "memberships",
                column: "event_id");

            migrationBuilder.CreateIndex(
                name: "ux_memberships_person_year",
                table: "memberships",
                columns: new[] { "person_id", "membership_year" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "memberships");

            migrationBuilder.DropCheckConstraint(
                name: "ck_merch_sales_money",
                table: "merch_sales");

            migrationBuilder.DropColumn(
                name: "membership_fee_amount",
                table: "merch_sales");

            migrationBuilder.DropColumn(
                name: "membership_fee_included",
                table: "merch_sales");

            migrationBuilder.AddCheckConstraint(
                name: "ck_merch_sales_money",
                table: "merch_sales",
                sql: "subtotal >= 0 AND discount_amount >= 0 AND total >= 0 AND total = subtotal - discount_amount");
        }
    }
}
