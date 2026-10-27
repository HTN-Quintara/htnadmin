using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Htn.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Sponsors : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "event_partner_settings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    threshold = table.Column<int>(type: "integer", nullable: false),
                    count_basis = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    group_link_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_event_partner_settings", x => x.id);
                    table.CheckConstraint("ck_event_partner_settings_basis", "count_basis IN ('PaidOnly', 'AllRegistrants', 'CheckedIn')");
                    table.CheckConstraint("ck_event_partner_settings_threshold", "threshold BETWEEN 5 AND 1000 AND threshold % 5 = 0");
                    table.ForeignKey(
                        name: "fk_event_partner_settings_events_event_id",
                        column: x => x.event_id,
                        principalTable: "events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "event_partners",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name_key = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    display_name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    org_type = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    tagged_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tagged_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    untagged_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    untag_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    logo_file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    website_url = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_event_partners", x => x.id);
                    table.CheckConstraint("ck_event_partners_untagged", "(untagged_at IS NULL) = (untag_reason IS NULL) AND (website_url IS NULL OR website_url ~ '^https?://')");
                    table.ForeignKey(
                        name: "fk_event_partners_events_event_id",
                        column: x => x.event_id,
                        principalTable: "events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_event_partners_stored_files_logo_file_id",
                        column: x => x.logo_file_id,
                        principalTable: "stored_files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sponsor_tiers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    @class = table.Column<string>(name: "class", type: "character varying(16)", maxLength: 16, nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    cash_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    price_note = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    in_kind_text = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    in_kind_item_count = table.Column<int>(type: "integer", nullable: true),
                    slots = table.Column<int>(type: "integer", nullable: false),
                    comped_seats = table.Column<int>(type: "integer", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    perks = table.Column<string[]>(type: "text[]", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    deactivated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sponsor_tiers", x => x.id);
                    table.CheckConstraint("ck_sponsor_tiers_cash", "cash_amount IS NULL OR (cash_amount > 0 AND round(cash_amount, 2) = cash_amount)");
                    table.CheckConstraint("ck_sponsor_tiers_class", "class IN ('Platinum', 'Gold', 'Silver', 'Partner', 'Individual')");
                    table.CheckConstraint("ck_sponsor_tiers_comped", "comped_seats BETWEEN 0 AND 200");
                    table.CheckConstraint("ck_sponsor_tiers_contribution", "cash_amount IS NOT NULL OR in_kind_text IS NOT NULL");
                    table.CheckConstraint("ck_sponsor_tiers_deactivated", "is_active = (deactivated_at IS NULL)");
                    table.CheckConstraint("ck_sponsor_tiers_in_kind", "in_kind_item_count IS NULL OR in_kind_text IS NOT NULL");
                    table.CheckConstraint("ck_sponsor_tiers_slots", "slots BETWEEN 1 AND 100");
                    table.ForeignKey(
                        name: "fk_sponsor_tiers_events_event_id",
                        column: x => x.event_id,
                        principalTable: "events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "partner_group_links",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_partner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    revoke_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_partner_group_links", x => x.id);
                    table.CheckConstraint("ck_partner_group_links_revoke", "(revoked_at IS NULL) = (revoke_reason IS NULL) AND (revoked_at IS NULL) = (revoked_by_id IS NULL)");
                    table.CheckConstraint("ck_partner_group_links_token", "token_hash ~ '^[0-9a-f]{64}$'");
                    table.ForeignKey(
                        name: "fk_partner_group_links_event_partners_event_partner_id",
                        column: x => x.event_partner_id,
                        principalTable: "event_partners",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_partner_group_links_events_event_id",
                        column: x => x.event_id,
                        principalTable: "events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sponsors",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    name_key = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    display_name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    is_anonymous = table.Column<bool>(type: "boolean", nullable: false),
                    contact_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    contact_role = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    contact_email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    contact_mobile = table.Column<string>(type: "character varying(13)", maxLength: 13, nullable: true),
                    tier_id = table.Column<Guid>(type: "uuid", nullable: true),
                    cash_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    in_kind_description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    in_kind_item_count = table.Column<int>(type: "integer", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    confirmed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    confirmed_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    cash_received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cash_payment_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    cash_reference = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    comped_seats = table.Column<int>(type: "integer", nullable: false),
                    logo_file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    website_url = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
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
                    table.PrimaryKey("pk_sponsors", x => x.id);
                    table.CheckConstraint("ck_sponsors_anonymous", "NOT is_anonymous OR display_name IS NULL");
                    table.CheckConstraint("ck_sponsors_cash", "cash_amount IS NULL OR (cash_amount > 0 AND round(cash_amount, 2) = cash_amount)");
                    table.CheckConstraint("ck_sponsors_cash_received", "(cash_received_at IS NULL) = (cash_payment_account_id IS NULL) AND (cash_received_at IS NULL OR (cash_amount IS NOT NULL AND status = 'Confirmed'))");
                    table.CheckConstraint("ck_sponsors_comped", "comped_seats BETWEEN 0 AND 200");
                    table.CheckConstraint("ck_sponsors_confirmed", "(status = 'Confirmed') = (confirmed_at IS NOT NULL) AND (confirmed_at IS NULL) = (confirmed_by_id IS NULL)");
                    table.CheckConstraint("ck_sponsors_contribution", "cash_amount IS NOT NULL OR in_kind_description IS NOT NULL");
                    table.CheckConstraint("ck_sponsors_credit", "kind = 'Organization' OR is_anonymous OR display_name IS NOT NULL");
                    table.CheckConstraint("ck_sponsors_in_kind", "in_kind_item_count IS NULL OR in_kind_description IS NOT NULL");
                    table.CheckConstraint("ck_sponsors_kind", "kind IN ('Organization', 'Individual')");
                    table.CheckConstraint("ck_sponsors_org_not_anonymous", "kind = 'Individual' OR NOT is_anonymous");
                    table.CheckConstraint("ck_sponsors_status", "status IN ('AwaitingContract', 'Confirmed')");
                    table.CheckConstraint("ck_sponsors_void", "(voided_at IS NULL) = (void_reason IS NULL) AND (voided_at IS NULL) = (voided_by_id IS NULL) AND (voided_at IS NULL OR cash_received_at IS NULL)");
                    table.CheckConstraint("ck_sponsors_website", "website_url IS NULL OR website_url ~ '^https?://'");
                    table.ForeignKey(
                        name: "fk_sponsors_events_event_id",
                        column: x => x.event_id,
                        principalTable: "events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sponsors_payment_accounts_cash_payment_account_id",
                        column: x => x.cash_payment_account_id,
                        principalTable: "payment_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sponsors_sponsor_tiers_tier_id",
                        column: x => x.tier_id,
                        principalTable: "sponsor_tiers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sponsors_stored_files_logo_file_id",
                        column: x => x.logo_file_id,
                        principalTable: "stored_files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sponsor_comp_registrations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sponsor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    registration_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sponsor_comp_registrations", x => x.id);
                    table.ForeignKey(
                        name: "fk_sponsor_comp_registrations_registrations_registration_id",
                        column: x => x.registration_id,
                        principalTable: "registrations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sponsor_comp_registrations_sponsors_sponsor_id",
                        column: x => x.sponsor_id,
                        principalTable: "sponsors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sponsor_perks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sponsor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_tier_id = table.Column<Guid>(type: "uuid", nullable: true),
                    text = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    delivered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    delivered_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    removed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    remove_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sponsor_perks", x => x.id);
                    table.CheckConstraint("ck_sponsor_perks_delivered", "(delivered_at IS NULL) = (delivered_by_id IS NULL)");
                    table.CheckConstraint("ck_sponsor_perks_removed", "(removed_at IS NULL) = (remove_reason IS NULL)");
                    table.ForeignKey(
                        name: "fk_sponsor_perks_sponsor_tiers_source_tier_id",
                        column: x => x.source_tier_id,
                        principalTable: "sponsor_tiers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sponsor_perks_sponsors_sponsor_id",
                        column: x => x.sponsor_id,
                        principalTable: "sponsors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_event_partner_settings_event",
                table: "event_partner_settings",
                column: "event_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_event_partners_logo_file_id",
                table: "event_partners",
                column: "logo_file_id");

            migrationBuilder.CreateIndex(
                name: "ux_event_partners_event_name",
                table: "event_partners",
                columns: new[] { "event_id", "name_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_partner_group_links_event_id",
                table: "partner_group_links",
                column: "event_id");

            migrationBuilder.CreateIndex(
                name: "ux_partner_group_links_open",
                table: "partner_group_links",
                column: "event_partner_id",
                unique: true,
                filter: "revoked_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_partner_group_links_token",
                table: "partner_group_links",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sponsor_comp_registrations_sponsor",
                table: "sponsor_comp_registrations",
                column: "sponsor_id");

            migrationBuilder.CreateIndex(
                name: "ux_sponsor_comp_registrations_registration",
                table: "sponsor_comp_registrations",
                column: "registration_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sponsor_perks_source_tier_id",
                table: "sponsor_perks",
                column: "source_tier_id");

            migrationBuilder.CreateIndex(
                name: "ix_sponsor_perks_sponsor",
                table: "sponsor_perks",
                columns: new[] { "sponsor_id", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ix_sponsor_tiers_event",
                table: "sponsor_tiers",
                column: "event_id");

            migrationBuilder.CreateIndex(
                name: "ux_sponsor_tiers_event_name",
                table: "sponsor_tiers",
                columns: new[] { "event_id", "name" },
                unique: true,
                filter: "deactivated_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_sponsors_cash_payment_account_id",
                table: "sponsors",
                column: "cash_payment_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_sponsors_event_status",
                table: "sponsors",
                columns: new[] { "event_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_sponsors_logo_file_id",
                table: "sponsors",
                column: "logo_file_id");

            migrationBuilder.CreateIndex(
                name: "ix_sponsors_name_key",
                table: "sponsors",
                column: "name_key");

            migrationBuilder.CreateIndex(
                name: "ix_sponsors_tier_id",
                table: "sponsors",
                column: "tier_id");

            migrationBuilder.CreateIndex(
                name: "ux_sponsors_event_name",
                table: "sponsors",
                columns: new[] { "event_id", "name_key" },
                unique: true,
                filter: "voided_at IS NULL");

            // Partners are derived from registrations grouped by the agency/company field. A stored generated column
            // normalizes that free text the same way as NameKey.Of, so sponsors, partners and registrations all
            // group on one key; its index feeds the grouped partner query (SponsorsOps.CompanyGroupsAsync).
            migrationBuilder.Sql("""
                ALTER TABLE attendees
                    ADD COLUMN company_key text
                    GENERATED ALWAYS AS (nullif(btrim(regexp_replace(lower(btrim(company)), '[^a-z0-9]+', ' ', 'g')), '')) STORED;
                CREATE INDEX ix_attendees_event_company_key ON attendees (event_id, company_key)
                    WHERE company_key IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP INDEX IF EXISTS ix_attendees_event_company_key;
                ALTER TABLE attendees DROP COLUMN IF EXISTS company_key;
                """);

            migrationBuilder.DropTable(
                name: "event_partner_settings");

            migrationBuilder.DropTable(
                name: "partner_group_links");

            migrationBuilder.DropTable(
                name: "sponsor_comp_registrations");

            migrationBuilder.DropTable(
                name: "sponsor_perks");

            migrationBuilder.DropTable(
                name: "event_partners");

            migrationBuilder.DropTable(
                name: "sponsors");

            migrationBuilder.DropTable(
                name: "sponsor_tiers");
        }
    }
}
