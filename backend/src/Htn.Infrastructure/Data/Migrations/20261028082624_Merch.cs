using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Htn.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Merch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "apprentice_merch_discount_pct",
                table: "org_settings",
                type: "integer",
                nullable: false,
                defaultValue: 15);

            migrationBuilder.AddColumn<int>(
                name: "board_merch_discount_pct",
                table: "org_settings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "goon_merch_discount_pct",
                table: "org_settings",
                type: "integer",
                nullable: false,
                defaultValue: 20);

            migrationBuilder.AddColumn<int>(
                name: "member_merch_discount_pct",
                table: "org_settings",
                type: "integer",
                nullable: false,
                defaultValue: 10);

            migrationBuilder.CreateTable(
                name: "event_merch_settings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    goon_pct = table.Column<int>(type: "integer", nullable: false),
                    apprentice_pct = table.Column<int>(type: "integer", nullable: false),
                    member_pct = table.Column<int>(type: "integer", nullable: false),
                    board_pct = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_event_merch_settings", x => x.id);
                    table.CheckConstraint("ck_event_merch_settings_pct", "goon_pct BETWEEN 0 AND 100 AND apprentice_pct BETWEEN 0 AND 100 AND member_pct BETWEEN 0 AND 100 AND board_pct BETWEEN 0 AND 100");
                    table.ForeignKey(
                        name: "fk_event_merch_settings_events_event_id",
                        column: x => x.event_id,
                        principalTable: "events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "merch_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    price = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    swatch_color = table.Column<string>(type: "character(7)", fixedLength: true, maxLength: 7, nullable: true),
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
                    table.PrimaryKey("pk_merch_items", x => x.id);
                    table.CheckConstraint("ck_merch_items_deactivated", "(deactivated_at IS NULL) = (deactivate_reason IS NULL) AND (deactivated_at IS NULL) = (deactivated_by_id IS NULL)");
                    table.CheckConstraint("ck_merch_items_price", "price >= 0");
                    table.CheckConstraint("ck_merch_items_swatch", "swatch_color IS NULL OR swatch_color ~ '^#[0-9A-Fa-f]{6}$'");
                    table.ForeignKey(
                        name: "fk_merch_items_events_event_id",
                        column: x => x.event_id,
                        principalTable: "events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "merch_sales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    buyer_person_id = table.Column<Guid>(type: "uuid", nullable: true),
                    buyer_attendee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    buyer_name_snapshot = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    buyer_level_snapshot = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    subtotal = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    discount_pct = table.Column<int>(type: "integer", nullable: false),
                    discount_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    total = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    method = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    cashier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    station = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    sold_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_merch_sales", x => x.id);
                    table.CheckConstraint("ck_merch_sales_discount", "discount_pct BETWEEN 0 AND 100");
                    table.CheckConstraint("ck_merch_sales_level", "buyer_level_snapshot IN ('Board', 'Goon', 'Apprentice', 'Member') OR buyer_level_snapshot IS NULL");
                    table.CheckConstraint("ck_merch_sales_method", "method IN ('Cash', 'GCash', 'Bank')");
                    table.CheckConstraint("ck_merch_sales_money", "subtotal >= 0 AND discount_amount >= 0 AND total >= 0 AND total = subtotal - discount_amount");
                    table.ForeignKey(
                        name: "fk_merch_sales_attendees_buyer_attendee_id",
                        column: x => x.buyer_attendee_id,
                        principalTable: "attendees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_merch_sales_events_event_id",
                        column: x => x.event_id,
                        principalTable: "events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_merch_sales_people_buyer_person_id",
                        column: x => x.buyer_person_id,
                        principalTable: "people",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "merch_variants",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    label = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    stock_on_hand = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("pk_merch_variants", x => x.id);
                    table.CheckConstraint("ck_merch_variants_deactivated", "(deactivated_at IS NULL) = (deactivate_reason IS NULL) AND (deactivated_at IS NULL) = (deactivated_by_id IS NULL)");
                    table.CheckConstraint("ck_merch_variants_stock", "stock_on_hand BETWEEN 0 AND 1000000");
                    table.ForeignKey(
                        name: "fk_merch_variants_merch_items_item_id",
                        column: x => x.item_id,
                        principalTable: "merch_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "merch_sale_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sale_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    variant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    qty = table.Column<int>(type: "integer", nullable: false),
                    unit_price_snapshot = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    line_total = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_merch_sale_lines", x => x.id);
                    table.CheckConstraint("ck_merch_sale_lines_money", "unit_price_snapshot >= 0 AND line_total >= 0 AND line_total = round(unit_price_snapshot * qty, 2)");
                    table.CheckConstraint("ck_merch_sale_lines_qty", "qty > 0");
                    table.ForeignKey(
                        name: "fk_merch_sale_lines_merch_items_item_id",
                        column: x => x.item_id,
                        principalTable: "merch_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_merch_sale_lines_merch_sales_sale_id",
                        column: x => x.sale_id,
                        principalTable: "merch_sales",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_merch_sale_lines_merch_variants_variant_id",
                        column: x => x.variant_id,
                        principalTable: "merch_variants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_org_settings_merch_discounts",
                table: "org_settings",
                sql: "goon_merch_discount_pct BETWEEN 0 AND 100 AND apprentice_merch_discount_pct BETWEEN 0 AND 100 AND member_merch_discount_pct BETWEEN 0 AND 100 AND board_merch_discount_pct BETWEEN 0 AND 100");

            migrationBuilder.CreateIndex(
                name: "ux_event_merch_settings_event",
                table: "event_merch_settings",
                column: "event_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_merch_items_event",
                table: "merch_items",
                column: "event_id");

            migrationBuilder.CreateIndex(
                name: "ix_merch_sale_lines_item_id",
                table: "merch_sale_lines",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "ix_merch_sale_lines_sale",
                table: "merch_sale_lines",
                column: "sale_id");

            migrationBuilder.CreateIndex(
                name: "ix_merch_sale_lines_variant",
                table: "merch_sale_lines",
                column: "variant_id");

            migrationBuilder.CreateIndex(
                name: "ix_merch_sales_buyer_attendee_id",
                table: "merch_sales",
                column: "buyer_attendee_id");

            migrationBuilder.CreateIndex(
                name: "ix_merch_sales_buyer_person_id",
                table: "merch_sales",
                column: "buyer_person_id");

            migrationBuilder.CreateIndex(
                name: "ix_merch_sales_event",
                table: "merch_sales",
                column: "event_id");

            migrationBuilder.CreateIndex(
                name: "ix_merch_sales_sold_at",
                table: "merch_sales",
                column: "sold_at");

            migrationBuilder.CreateIndex(
                name: "ux_merch_sales_event_client_request_id",
                table: "merch_sales",
                columns: new[] { "event_id", "client_request_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_merch_variants_item_label",
                table: "merch_variants",
                columns: new[] { "item_id", "label" },
                unique: true,
                filter: "deactivated_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "event_merch_settings");

            migrationBuilder.DropTable(
                name: "merch_sale_lines");

            migrationBuilder.DropTable(
                name: "merch_sales");

            migrationBuilder.DropTable(
                name: "merch_variants");

            migrationBuilder.DropTable(
                name: "merch_items");

            migrationBuilder.DropCheckConstraint(
                name: "ck_org_settings_merch_discounts",
                table: "org_settings");

            migrationBuilder.DropColumn(
                name: "apprentice_merch_discount_pct",
                table: "org_settings");

            migrationBuilder.DropColumn(
                name: "board_merch_discount_pct",
                table: "org_settings");

            migrationBuilder.DropColumn(
                name: "goon_merch_discount_pct",
                table: "org_settings");

            migrationBuilder.DropColumn(
                name: "member_merch_discount_pct",
                table: "org_settings");
        }
    }
}
