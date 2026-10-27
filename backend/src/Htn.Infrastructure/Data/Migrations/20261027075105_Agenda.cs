using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Htn.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Agenda : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "event_rooms",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    color_hex = table.Column<string>(type: "character(7)", fixedLength: true, maxLength: 7, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deactivated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_event_rooms", x => x.id);
                    table.CheckConstraint("ck_event_rooms_color", "color_hex ~ '^#[0-9A-F]{6}$'");
                    table.CheckConstraint("ck_event_rooms_deactivated", "is_active = (deactivated_at IS NULL)");
                    table.ForeignKey(
                        name: "fk_event_rooms_events_event_id",
                        column: x => x.event_id,
                        principalTable: "events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    starts_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    duration_minutes = table.Column<int>(type: "integer", nullable: false),
                    owner_committee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_public = table.Column<bool>(type: "boolean", nullable: false),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sessions", x => x.id);
                    table.CheckConstraint("ck_sessions_duration", "duration_minutes BETWEEN 5 AND 600");
                    table.CheckConstraint("ck_sessions_kind", "kind IN ('Keynote', 'Talk', 'Panel', 'Workshop', 'Break')");
                    table.CheckConstraint("ck_sessions_title", "char_length(title) > 0");
                    table.ForeignKey(
                        name: "fk_sessions_committees_owner_committee_id",
                        column: x => x.owner_committee_id,
                        principalTable: "committees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sessions_events_event_id",
                        column: x => x.event_id,
                        principalTable: "events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "session_rooms",
                columns: table => new
                {
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    room_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_session_rooms", x => new { x.session_id, x.room_id });
                    table.ForeignKey(
                        name: "fk_session_rooms_event_rooms_room_id",
                        column: x => x.room_id,
                        principalTable: "event_rooms",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_session_rooms_sessions_session_id",
                        column: x => x.session_id,
                        principalTable: "sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "session_speakers",
                columns: table => new
                {
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    speaker_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_session_speakers", x => new { x.session_id, x.speaker_id });
                    table.CheckConstraint("ck_session_speakers_role", "role IN ('Speaker', 'Moderator', 'Panellist')");
                    table.ForeignKey(
                        name: "fk_session_speakers_sessions_session_id",
                        column: x => x.session_id,
                        principalTable: "sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_session_speakers_speakers_speaker_id",
                        column: x => x.speaker_id,
                        principalTable: "speakers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_event_rooms_event",
                table: "event_rooms",
                columns: new[] { "event_id", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ux_event_rooms_event_name",
                table: "event_rooms",
                columns: new[] { "event_id", "name" },
                unique: true,
                filter: "deactivated_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_session_rooms_room",
                table: "session_rooms",
                columns: new[] { "room_id", "session_id" });

            migrationBuilder.CreateIndex(
                name: "ix_session_speakers_speaker",
                table: "session_speakers",
                column: "speaker_id");

            migrationBuilder.CreateIndex(
                name: "ix_sessions_event_start",
                table: "sessions",
                columns: new[] { "event_id", "starts_at" });

            migrationBuilder.CreateIndex(
                name: "ix_sessions_owner_committee_id",
                table: "sessions",
                column: "owner_committee_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "session_rooms");

            migrationBuilder.DropTable(
                name: "session_speakers");

            migrationBuilder.DropTable(
                name: "event_rooms");

            migrationBuilder.DropTable(
                name: "sessions");
        }
    }
}
