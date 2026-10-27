using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Htn.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Directory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "directory_org_id",
                table: "sponsors",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "directory_org_id",
                table: "leads",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "directory_org_id",
                table: "event_partners",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "directory_orgs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    name_key = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    addressee_salutation = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    website_url = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    logo_file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
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
                    table.PrimaryKey("pk_directory_orgs", x => x.id);
                    table.CheckConstraint("ck_directory_orgs_deactivated", "(deactivated_at IS NULL) = (deactivate_reason IS NULL) AND (deactivated_at IS NULL) = (deactivated_by_id IS NULL)");
                    table.CheckConstraint("ck_directory_orgs_kind", "kind IN ('Government', 'Company', 'University', 'NonProfit')");
                    table.CheckConstraint("ck_directory_orgs_website", "website_url IS NULL OR website_url ~ '^https?://'");
                    table.ForeignKey(
                        name: "fk_directory_orgs_stored_files_logo_file_id",
                        column: x => x.logo_file_id,
                        principalTable: "stored_files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "directory_contacts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    holder_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    is_primary = table.Column<bool>(type: "boolean", nullable: false),
                    verified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cc_emails = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
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
                    table.PrimaryKey("pk_directory_contacts", x => x.id);
                    table.CheckConstraint("ck_directory_contacts_void", "(voided_at IS NULL) = (void_reason IS NULL) AND (voided_at IS NULL) = (voided_by_id IS NULL)");
                    table.ForeignKey(
                        name: "fk_directory_contacts_directory_orgs_org_id",
                        column: x => x.org_id,
                        principalTable: "directory_orgs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "directory_contact_history",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contact_id = table.Column<Guid>(type: "uuid", nullable: true),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    prior_position = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    prior_holder_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_directory_contact_history", x => x.id);
                    table.CheckConstraint("ck_directory_contact_history_holder_changed", "kind <> 'HolderChanged' OR (prior_position IS NOT NULL AND prior_holder_name IS NOT NULL)");
                    table.CheckConstraint("ck_directory_contact_history_kind", "kind IN ('HolderChanged', 'Verified', 'StillCorrect', 'Bounced')");
                    table.ForeignKey(
                        name: "fk_directory_contact_history_directory_contacts_contact_id",
                        column: x => x.contact_id,
                        principalTable: "directory_contacts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_directory_contact_history_directory_orgs_org_id",
                        column: x => x.org_id,
                        principalTable: "directory_orgs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_sponsors_directory_org",
                table: "sponsors",
                column: "directory_org_id");

            migrationBuilder.CreateIndex(
                name: "ix_leads_directory_org",
                table: "leads",
                column: "directory_org_id");

            migrationBuilder.CreateIndex(
                name: "ix_event_partners_directory_org",
                table: "event_partners",
                column: "directory_org_id");

            migrationBuilder.CreateIndex(
                name: "ix_directory_contact_history_contact",
                table: "directory_contact_history",
                column: "contact_id");

            migrationBuilder.CreateIndex(
                name: "ix_directory_contact_history_org",
                table: "directory_contact_history",
                columns: new[] { "org_id", "at" });

            migrationBuilder.CreateIndex(
                name: "ux_directory_contacts_primary",
                table: "directory_contacts",
                column: "org_id",
                unique: true,
                filter: "is_primary AND voided_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_directory_orgs_kind",
                table: "directory_orgs",
                column: "kind");

            migrationBuilder.CreateIndex(
                name: "ix_directory_orgs_logo_file_id",
                table: "directory_orgs",
                column: "logo_file_id");

            migrationBuilder.CreateIndex(
                name: "ux_directory_orgs_name_key",
                table: "directory_orgs",
                column: "name_key",
                unique: true,
                filter: "deactivated_at IS NULL");

            migrationBuilder.AddForeignKey(
                name: "fk_event_partners_directory_orgs_directory_org_id",
                table: "event_partners",
                column: "directory_org_id",
                principalTable: "directory_orgs",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_leads_directory_orgs_directory_org_id",
                table: "leads",
                column: "directory_org_id",
                principalTable: "directory_orgs",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_sponsors_directory_orgs_directory_org_id",
                table: "sponsors",
                column: "directory_org_id",
                principalTable: "directory_orgs",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_event_partners_directory_orgs_directory_org_id",
                table: "event_partners");

            migrationBuilder.DropForeignKey(
                name: "fk_leads_directory_orgs_directory_org_id",
                table: "leads");

            migrationBuilder.DropForeignKey(
                name: "fk_sponsors_directory_orgs_directory_org_id",
                table: "sponsors");

            migrationBuilder.DropTable(
                name: "directory_contact_history");

            migrationBuilder.DropTable(
                name: "directory_contacts");

            migrationBuilder.DropTable(
                name: "directory_orgs");

            migrationBuilder.DropIndex(
                name: "ix_sponsors_directory_org",
                table: "sponsors");

            migrationBuilder.DropIndex(
                name: "ix_leads_directory_org",
                table: "leads");

            migrationBuilder.DropIndex(
                name: "ix_event_partners_directory_org",
                table: "event_partners");

            migrationBuilder.DropColumn(
                name: "directory_org_id",
                table: "sponsors");

            migrationBuilder.DropColumn(
                name: "directory_org_id",
                table: "leads");

            migrationBuilder.DropColumn(
                name: "directory_org_id",
                table: "event_partners");
        }
    }
}
