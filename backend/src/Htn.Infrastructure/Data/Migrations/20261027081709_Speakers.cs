using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Htn.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Speakers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "travel_mode",
                table: "events",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "None");

            migrationBuilder.CreateTable(
                name: "speaker_links",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    speaker_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    send_count = table.Column<int>(type: "integer", nullable: false),
                    first_opened_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_opened_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    revoke_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    otp_hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: true),
                    otp_sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    otp_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    otp_attempts = table.Column<int>(type: "integer", nullable: false),
                    otp_locked_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    otp_window_started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    otp_send_count = table.Column<int>(type: "integer", nullable: false),
                    session_token_hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: true),
                    session_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_speaker_links", x => x.id);
                    table.CheckConstraint("ck_speaker_links_expiry", "expires_at > created_at");
                    table.CheckConstraint("ck_speaker_links_otp", "(otp_hash IS NULL) = (otp_expires_at IS NULL) AND otp_attempts BETWEEN 0 AND 5");
                    table.CheckConstraint("ck_speaker_links_revoke", "(revoked_at IS NULL) = (revoke_reason IS NULL) AND (revoked_at IS NULL) = (revoked_by_id IS NULL)");
                    table.CheckConstraint("ck_speaker_links_sent", "send_count >= 0 AND (sent_at IS NULL) = (send_count = 0)");
                    table.CheckConstraint("ck_speaker_links_session", "(session_token_hash IS NULL) = (session_expires_at IS NULL)");
                    table.CheckConstraint("ck_speaker_links_submitted", "last_submitted_at IS NULL OR submitted_at IS NOT NULL");
                    table.CheckConstraint("ck_speaker_links_token", "token_hash ~ '^[0-9a-f]{64}$'");
                    table.ForeignKey(
                        name: "fk_speaker_links_events_event_id",
                        column: x => x.event_id,
                        principalTable: "events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_speaker_links_speakers_speaker_id",
                        column: x => x.speaker_id,
                        principalTable: "speakers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "speaker_plus_ones",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    speaker_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    name_segment = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    shares_room = table.Column<bool>(type: "boolean", nullable: false),
                    voucher_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    removed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    removed_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    remove_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_speaker_plus_ones", x => x.id);
                    table.CheckConstraint("ck_speaker_plus_ones_removed", "(removed_at IS NULL) = (remove_reason IS NULL)");
                    table.ForeignKey(
                        name: "fk_speaker_plus_ones_speakers_speaker_id",
                        column: x => x.speaker_id,
                        principalTable: "speakers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "speaker_travel",
                columns: table => new
                {
                    speaker_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    arrival_date = table.Column<DateOnly>(type: "date", nullable: true),
                    departure_date = table.Column<DateOnly>(type: "date", nullable: true),
                    flying_from = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    preferred_arrival_time = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    id_full_name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    bus_from = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    bus_eta = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    needs_terminal_pickup = table.Column<bool>(type: "boolean", nullable: false),
                    room_note = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    needs_notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    consent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    consent_version = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    purged_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_speaker_travel", x => x.speaker_id);
                    table.CheckConstraint("ck_speaker_travel_consent", "(submitted_at IS NULL) OR (consent_at IS NOT NULL AND consent_version IS NOT NULL)");
                    table.CheckConstraint("ck_speaker_travel_dates", "arrival_date IS NULL OR departure_date IS NULL OR departure_date >= arrival_date");
                    table.CheckConstraint("ck_speaker_travel_purged", "purged_at IS NULL OR (flying_from IS NULL AND preferred_arrival_time IS NULL AND id_full_name IS NULL)");
                    table.ForeignKey(
                        name: "fk_speaker_travel_events_event_id",
                        column: x => x.event_id,
                        principalTable: "events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_speaker_travel_speakers_speaker_id",
                        column: x => x.speaker_id,
                        principalTable: "speakers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "speaker_link_access",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    link_id = table.Column<Guid>(type: "uuid", nullable: false),
                    speaker_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    client_hint = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    ip_hint = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    note = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_speaker_link_access", x => x.id);
                    table.CheckConstraint("ck_speaker_link_access_kind", "kind IN ('LinkSent', 'Opened', 'CodeSent', 'CodeVerified', 'CodeFailed', 'CodeLocked', 'Submitted', 'Revoked')");
                    table.ForeignKey(
                        name: "fk_speaker_link_access_speaker_links_link_id",
                        column: x => x.link_id,
                        principalTable: "speaker_links",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_events_travel_mode",
                table: "events",
                sql: "travel_mode IN ('None', 'Plane', 'Bus', 'PlaneOrBus')");

            migrationBuilder.CreateIndex(
                name: "ix_speaker_link_access_link",
                table: "speaker_link_access",
                columns: new[] { "link_id", "at" });

            migrationBuilder.CreateIndex(
                name: "ix_speaker_links_event",
                table: "speaker_links",
                columns: new[] { "event_id", "speaker_id" });

            migrationBuilder.CreateIndex(
                name: "ux_speaker_links_open",
                table: "speaker_links",
                column: "speaker_id",
                unique: true,
                filter: "revoked_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_speaker_links_token",
                table: "speaker_links",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_speaker_plus_ones_active",
                table: "speaker_plus_ones",
                column: "speaker_id",
                unique: true,
                filter: "removed_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_speaker_travel_event_id",
                table: "speaker_travel",
                column: "event_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "speaker_link_access");

            migrationBuilder.DropTable(
                name: "speaker_plus_ones");

            migrationBuilder.DropTable(
                name: "speaker_travel");

            migrationBuilder.DropTable(
                name: "speaker_links");

            migrationBuilder.DropCheckConstraint(
                name: "ck_events_travel_mode",
                table: "events");

            migrationBuilder.DropColumn(
                name: "travel_mode",
                table: "events");
        }
    }
}
