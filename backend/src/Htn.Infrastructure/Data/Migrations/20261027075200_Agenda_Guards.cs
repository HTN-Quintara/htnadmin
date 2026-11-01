using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Htn.Infrastructure.Data.Migrations
{
    /// <summary>
    /// Phase 6C, the database half of the agenda rules.
    /// <list type="bullet">
    /// <item><c>event_rooms_guard</c>: a room is deactivated, never deleted; its event never changes.</item>
    /// <item><c>sessions_guard</c>: a session's event and created-by are frozen; it may be edited and, uniquely in
    /// Phase 6, hard-deleted (its room/speaker link rows cascade). Only the event id is immutable.</item>
    /// <item><c>session_room_overlap</c>: a room never holds two sessions whose times overlap. Deferred, so EF may
    /// write a session and its rooms in any order within one SaveChanges; the endpoint also checks this first and
    /// returns 409 <c>room_busy</c>, with this trigger as the backstop.</item>
    /// </list>
    /// </summary>
    public partial class Agenda_Guards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION htn_event_rooms_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP = 'DELETE' THEN
                        RAISE EXCEPTION 'event_rooms: a room is deactivated, never deleted' USING ERRCODE = 'insufficient_privilege';
                    END IF;
                    IF TG_OP = 'UPDATE' AND ROW(NEW.event_id, NEW.created_at) IS DISTINCT FROM ROW(OLD.event_id, OLD.created_at) THEN
                        RAISE EXCEPTION 'event_rooms: a room keeps its event' USING ERRCODE = 'insufficient_privilege';
                    END IF;
                    RETURN NEW;
                END;
                $$;
                DROP TRIGGER IF EXISTS event_rooms_guard ON event_rooms;
                CREATE TRIGGER event_rooms_guard BEFORE UPDATE OR DELETE ON event_rooms
                    FOR EACH ROW EXECUTE FUNCTION htn_event_rooms_guard();

                CREATE OR REPLACE FUNCTION htn_sessions_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP = 'UPDATE' AND ROW(NEW.event_id, NEW.created_at, NEW.created_by_id)
                       IS DISTINCT FROM ROW(OLD.event_id, OLD.created_at, OLD.created_by_id) THEN
                        RAISE EXCEPTION 'sessions: a session keeps its event' USING ERRCODE = 'insufficient_privilege';
                    END IF;
                    RETURN NEW;
                END;
                $$;
                DROP TRIGGER IF EXISTS sessions_guard ON sessions;
                CREATE TRIGGER sessions_guard BEFORE UPDATE ON sessions
                    FOR EACH ROW EXECUTE FUNCTION htn_sessions_guard();

                CREATE OR REPLACE FUNCTION htn_session_room_overlap() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE
                    v_start timestamptz;
                    v_end timestamptz;
                BEGIN
                    SELECT s.starts_at, s.starts_at + make_interval(mins => s.duration_minutes)
                      INTO v_start, v_end FROM sessions s WHERE s.id = NEW.session_id;
                    IF v_start IS NULL THEN
                        RETURN NULL;  -- the session was removed in the same transaction
                    END IF;
                    IF EXISTS (
                        SELECT 1 FROM session_rooms sr
                        JOIN sessions s2 ON s2.id = sr.session_id
                        WHERE sr.room_id = NEW.room_id AND sr.session_id <> NEW.session_id
                          AND s2.starts_at < v_end
                          AND (s2.starts_at + make_interval(mins => s2.duration_minutes)) > v_start
                    ) THEN
                        RAISE EXCEPTION 'session_rooms: that room is already booked for an overlapping time'
                            USING ERRCODE = 'check_violation';
                    END IF;
                    RETURN NULL;
                END;
                $$;
                DROP TRIGGER IF EXISTS session_room_overlap ON session_rooms;
                CREATE CONSTRAINT TRIGGER session_room_overlap AFTER INSERT OR UPDATE ON session_rooms
                    DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION htn_session_room_overlap();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS event_rooms_guard ON event_rooms;
                DROP TRIGGER IF EXISTS sessions_guard ON sessions;
                DROP TRIGGER IF EXISTS session_room_overlap ON session_rooms;
                DROP FUNCTION IF EXISTS htn_event_rooms_guard();
                DROP FUNCTION IF EXISTS htn_sessions_guard();
                DROP FUNCTION IF EXISTS htn_session_room_overlap();
                """);
        }
    }
}
