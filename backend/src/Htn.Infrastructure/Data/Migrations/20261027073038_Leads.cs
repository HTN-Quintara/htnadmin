using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Htn.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Leads : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "leads",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    subject = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    name_key = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    contact_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    contact_role = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    contact_email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    contact_mobile = table.Column<string>(type: "character varying(13)", maxLength: 13, nullable: true),
                    ask_tier_id = table.Column<Guid>(type: "uuid", nullable: true),
                    ask_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    ask_note = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    owner_person_id = table.Column<Guid>(type: "uuid", nullable: true),
                    next_step = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    next_step_due = table.Column<DateOnly>(type: "date", nullable: true),
                    source = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    sponsor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    speaker_id = table.Column<Guid>(type: "uuid", nullable: true),
                    confirmed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    confirmed_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    declined_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    declined_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    decline_reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_leads", x => x.id);
                    table.CheckConstraint("ck_leads_amount", "ask_amount IS NULL OR (ask_amount > 0 AND round(ask_amount, 2) = ask_amount)");
                    table.CheckConstraint("ck_leads_ask", "kind = 'Sponsor' OR (ask_tier_id IS NULL AND ask_amount IS NULL)");
                    table.CheckConstraint("ck_leads_confirmed", "(status = 'Confirmed') = (confirmed_at IS NOT NULL) AND (confirmed_at IS NULL) = (confirmed_by_id IS NULL)");
                    table.CheckConstraint("ck_leads_declined", "(status = 'Declined') = (declined_at IS NOT NULL) AND (declined_at IS NULL) = (decline_reason IS NULL) AND (declined_at IS NULL) = (declined_by_id IS NULL)");
                    table.CheckConstraint("ck_leads_kind", "kind IN ('Sponsor', 'Speaker')");
                    table.CheckConstraint("ck_leads_link", "(sponsor_id IS NULL OR (kind = 'Sponsor' AND status = 'Confirmed')) AND (speaker_id IS NULL OR (kind = 'Speaker' AND status = 'Confirmed'))");
                    table.CheckConstraint("ck_leads_source", "source IN ('Manual', 'Directory')");
                    table.CheckConstraint("ck_leads_status", "status IN ('Prospect', 'Contacted', 'InTalks', 'Confirmed', 'Declined')");
                    table.ForeignKey(
                        name: "fk_leads_events_event_id",
                        column: x => x.event_id,
                        principalTable: "events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_leads_people_owner_person_id",
                        column: x => x.owner_person_id,
                        principalTable: "people",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_leads_sponsor_tiers_ask_tier_id",
                        column: x => x.ask_tier_id,
                        principalTable: "sponsor_tiers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_leads_sponsors_sponsor_id",
                        column: x => x.sponsor_id,
                        principalTable: "sponsors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "speakers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lead_id = table.Column<Guid>(type: "uuid", nullable: true),
                    person_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    affiliation = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    photo_file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    photo_approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    photo_approved_by_id = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("pk_speakers", x => x.id);
                    table.CheckConstraint("ck_speakers_photo", "(photo_approved_at IS NULL) = (photo_approved_by_id IS NULL) AND (photo_approved_at IS NULL OR photo_file_id IS NOT NULL)");
                    table.CheckConstraint("ck_speakers_status", "status IN ('Invited', 'Confirmed', 'Declined')");
                    table.CheckConstraint("ck_speakers_void", "(voided_at IS NULL) = (void_reason IS NULL) AND (voided_at IS NULL) = (voided_by_id IS NULL)");
                    table.ForeignKey(
                        name: "fk_speakers_events_event_id",
                        column: x => x.event_id,
                        principalTable: "events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_speakers_leads_lead_id",
                        column: x => x.lead_id,
                        principalTable: "leads",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_speakers_stored_files_photo_file_id",
                        column: x => x.photo_file_id,
                        principalTable: "stored_files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_leads_ask_tier_id",
                table: "leads",
                column: "ask_tier_id");

            migrationBuilder.CreateIndex(
                name: "ix_leads_board",
                table: "leads",
                columns: new[] { "event_id", "kind", "status", "next_step_due" });

            migrationBuilder.CreateIndex(
                name: "ix_leads_owner",
                table: "leads",
                column: "owner_person_id");

            migrationBuilder.CreateIndex(
                name: "ux_leads_speaker",
                table: "leads",
                column: "speaker_id",
                unique: true,
                filter: "speaker_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_leads_sponsor",
                table: "leads",
                column: "sponsor_id",
                unique: true,
                filter: "sponsor_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_speakers_event_status",
                table: "speakers",
                columns: new[] { "event_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_speakers_lead_id",
                table: "speakers",
                column: "lead_id");

            migrationBuilder.CreateIndex(
                name: "ix_speakers_photo_file_id",
                table: "speakers",
                column: "photo_file_id");

            migrationBuilder.CreateIndex(
                name: "ux_speakers_event_email",
                table: "speakers",
                columns: new[] { "event_id", "email" },
                unique: true,
                filter: "voided_at IS NULL");

            migrationBuilder.AddForeignKey(
                name: "fk_leads_speakers_speaker_id",
                table: "leads",
                column: "speaker_id",
                principalTable: "speakers",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_leads_speakers_speaker_id",
                table: "leads");

            migrationBuilder.DropTable(
                name: "speakers");

            migrationBuilder.DropTable(
                name: "leads");
        }
    }
}
