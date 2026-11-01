using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Htn.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Invitations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "event_invitation_templates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    body = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_event_invitation_templates", x => x.id);
                    table.ForeignKey(
                        name: "fk_event_invitation_templates_events_event_id",
                        column: x => x.event_id,
                        principalTable: "events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "event_invitations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    directory_org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contact_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    sent_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    last_status_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    letter_subject_snapshot = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    letter_body_snapshot = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    recipient_email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    recipient_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_event_invitations", x => x.id);
                    table.CheckConstraint("ck_event_invitations_sent", "(status = 'NotSent') = (sent_at IS NULL) AND (status = 'NotSent') = (sent_by_id IS NULL) AND (status = 'NotSent') = (contact_id IS NULL) AND (status = 'NotSent') = (letter_subject_snapshot IS NULL) AND (status = 'NotSent') = (letter_body_snapshot IS NULL) AND (status = 'NotSent') = (recipient_email IS NULL) AND (status = 'NotSent') = (recipient_name IS NULL)");
                    table.CheckConstraint("ck_event_invitations_status", "status IN ('NotSent', 'Sent', 'Opened', 'Replied', 'Confirmed', 'Declined', 'Bounced')");
                    table.ForeignKey(
                        name: "fk_event_invitations_directory_orgs_directory_org_id",
                        column: x => x.directory_org_id,
                        principalTable: "directory_orgs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_event_invitations_events_event_id",
                        column: x => x.event_id,
                        principalTable: "events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "invitation_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    invitation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_invitation_events", x => x.id);
                    table.CheckConstraint("ck_invitation_events_kind", "kind IN ('NotSent', 'Sent', 'Opened', 'Replied', 'Confirmed', 'Declined', 'Bounced')");
                    table.ForeignKey(
                        name: "fk_invitation_events_event_invitations_invitation_id",
                        column: x => x.invitation_id,
                        principalTable: "event_invitations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_event_invitation_templates_event",
                table: "event_invitation_templates",
                column: "event_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_event_invitations_directory_org_id",
                table: "event_invitations",
                column: "directory_org_id");

            migrationBuilder.CreateIndex(
                name: "ix_event_invitations_event",
                table: "event_invitations",
                column: "event_id");

            migrationBuilder.CreateIndex(
                name: "ux_event_invitations_event_org",
                table: "event_invitations",
                columns: new[] { "event_id", "directory_org_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_invitation_events_invitation",
                table: "invitation_events",
                columns: new[] { "invitation_id", "at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "event_invitation_templates");

            migrationBuilder.DropTable(
                name: "invitation_events");

            migrationBuilder.DropTable(
                name: "event_invitations");
        }
    }
}
