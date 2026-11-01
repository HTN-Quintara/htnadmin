using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Htn.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class PopUps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_merch_sales_event_client_request_id",
                table: "merch_sales");

            migrationBuilder.AlterColumn<Guid>(
                name: "event_id",
                table: "merch_sales",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "pop_up_id",
                table: "merch_sales",
                type: "uuid",
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "event_id",
                table: "merch_items",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "leftover_source_event_id",
                table: "merch_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "pop_ups_allowed",
                table: "committees",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "pop_up_stores",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    venue_directory_org_id = table.Column<Guid>(type: "uuid", nullable: true),
                    venue_text = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    starts_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    fee_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    discount_mode = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: false),
                    goon_pct = table.Column<int>(type: "integer", nullable: true),
                    apprentice_pct = table.Column<int>(type: "integer", nullable: true),
                    member_pct = table.Column<int>(type: "integer", nullable: true),
                    board_pct = table.Column<int>(type: "integer", nullable: true),
                    owner_committee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    cash_counted = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    missing_value = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    written_off_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    written_off_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    write_off_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pop_up_stores", x => x.id);
                    table.CheckConstraint("ck_pop_up_stores_dates", "ends_at > starts_at");
                    table.CheckConstraint("ck_pop_up_stores_discount_mode", "discount_mode IN ('SameAsDefault', 'None', 'Custom')");
                    table.CheckConstraint("ck_pop_up_stores_fee", "fee_amount >= 0");
                    table.CheckConstraint("ck_pop_up_stores_pct", "(goon_pct IS NULL OR goon_pct BETWEEN 0 AND 100) AND (apprentice_pct IS NULL OR apprentice_pct BETWEEN 0 AND 100) AND (member_pct IS NULL OR member_pct BETWEEN 0 AND 100) AND (board_pct IS NULL OR board_pct BETWEEN 0 AND 100) AND (discount_mode = 'Custom') = (goon_pct IS NOT NULL)");
                    table.CheckConstraint("ck_pop_up_stores_status", "status IN ('Planned', 'Open', 'NeedsCount', 'ClosedBalanced', 'ClosedMissing')");
                    table.CheckConstraint("ck_pop_up_stores_venue", "(venue_directory_org_id IS NOT NULL) <> (venue_text IS NOT NULL)");
                    table.CheckConstraint("ck_pop_up_stores_written_off", "(written_off_at IS NULL) = (written_off_by_id IS NULL) AND (written_off_at IS NULL) = (write_off_reason IS NULL)");
                    table.ForeignKey(
                        name: "fk_pop_up_stores_committees_owner_committee_id",
                        column: x => x.owner_committee_id,
                        principalTable: "committees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_pop_up_stores_directory_orgs_venue_directory_org_id",
                        column: x => x.venue_directory_org_id,
                        principalTable: "directory_orgs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "pop_up_allocations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    pop_up_id = table.Column<Guid>(type: "uuid", nullable: false),
                    variant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    qty_brought = table.Column<int>(type: "integer", nullable: false),
                    qty_returned = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pop_up_allocations", x => x.id);
                    table.CheckConstraint("ck_pop_up_allocations_qty", "qty_brought > 0 AND (qty_returned IS NULL OR qty_returned BETWEEN 0 AND qty_brought)");
                    table.ForeignKey(
                        name: "fk_pop_up_allocations_merch_variants_variant_id",
                        column: x => x.variant_id,
                        principalTable: "merch_variants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_pop_up_allocations_pop_up_stores_pop_up_id",
                        column: x => x.pop_up_id,
                        principalTable: "pop_up_stores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "pop_up_team_members",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    pop_up_id = table.Column<Guid>(type: "uuid", nullable: false),
                    person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pop_up_team_members", x => x.id);
                    table.ForeignKey(
                        name: "fk_pop_up_team_members_people_person_id",
                        column: x => x.person_id,
                        principalTable: "people",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_pop_up_team_members_pop_up_stores_pop_up_id",
                        column: x => x.pop_up_id,
                        principalTable: "pop_up_stores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_merch_sales_pop_up",
                table: "merch_sales",
                column: "pop_up_id");

            migrationBuilder.CreateIndex(
                name: "ux_merch_sales_event_client_request_id",
                table: "merch_sales",
                columns: new[] { "event_id", "client_request_id" },
                unique: true,
                filter: "event_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_merch_sales_pop_up_client_request_id",
                table: "merch_sales",
                columns: new[] { "pop_up_id", "client_request_id" },
                unique: true,
                filter: "pop_up_id IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_merch_sales_one_scope",
                table: "merch_sales",
                sql: "(event_id IS NOT NULL) <> (pop_up_id IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "ix_merch_items_leftover_source_event_id",
                table: "merch_items",
                column: "leftover_source_event_id");

            migrationBuilder.CreateIndex(
                name: "ix_pop_up_allocations_variant_id",
                table: "pop_up_allocations",
                column: "variant_id");

            migrationBuilder.CreateIndex(
                name: "ux_pop_up_allocations_pop_up_variant",
                table: "pop_up_allocations",
                columns: new[] { "pop_up_id", "variant_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_pop_up_stores_owner_committee",
                table: "pop_up_stores",
                column: "owner_committee_id");

            migrationBuilder.CreateIndex(
                name: "ix_pop_up_stores_status",
                table: "pop_up_stores",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_pop_up_stores_venue_directory_org_id",
                table: "pop_up_stores",
                column: "venue_directory_org_id");

            migrationBuilder.CreateIndex(
                name: "ix_pop_up_team_members_person_id",
                table: "pop_up_team_members",
                column: "person_id");

            migrationBuilder.CreateIndex(
                name: "ux_pop_up_team_members_pop_up_person",
                table: "pop_up_team_members",
                columns: new[] { "pop_up_id", "person_id" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_merch_items_events_leftover_source_event_id",
                table: "merch_items",
                column: "leftover_source_event_id",
                principalTable: "events",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_merch_sales_pop_up_stores_pop_up_id",
                table: "merch_sales",
                column: "pop_up_id",
                principalTable: "pop_up_stores",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_merch_items_events_leftover_source_event_id",
                table: "merch_items");

            migrationBuilder.DropForeignKey(
                name: "fk_merch_sales_pop_up_stores_pop_up_id",
                table: "merch_sales");

            migrationBuilder.DropTable(
                name: "pop_up_allocations");

            migrationBuilder.DropTable(
                name: "pop_up_team_members");

            migrationBuilder.DropTable(
                name: "pop_up_stores");

            migrationBuilder.DropIndex(
                name: "ix_merch_sales_pop_up",
                table: "merch_sales");

            migrationBuilder.DropIndex(
                name: "ux_merch_sales_event_client_request_id",
                table: "merch_sales");

            migrationBuilder.DropIndex(
                name: "ux_merch_sales_pop_up_client_request_id",
                table: "merch_sales");

            migrationBuilder.DropCheckConstraint(
                name: "ck_merch_sales_one_scope",
                table: "merch_sales");

            migrationBuilder.DropIndex(
                name: "ix_merch_items_leftover_source_event_id",
                table: "merch_items");

            migrationBuilder.DropColumn(
                name: "pop_up_id",
                table: "merch_sales");

            migrationBuilder.DropColumn(
                name: "leftover_source_event_id",
                table: "merch_items");

            migrationBuilder.DropColumn(
                name: "pop_ups_allowed",
                table: "committees");

            migrationBuilder.AlterColumn<Guid>(
                name: "event_id",
                table: "merch_sales",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "event_id",
                table: "merch_items",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_merch_sales_event_client_request_id",
                table: "merch_sales",
                columns: new[] { "event_id", "client_request_id" },
                unique: true);
        }
    }
}
