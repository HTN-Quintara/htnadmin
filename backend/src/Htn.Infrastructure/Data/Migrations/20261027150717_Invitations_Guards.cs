using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Htn.Infrastructure.Data.Migrations
{
    /// <summary>
    /// Phase 7B, the database half of the invitation rules, mirroring <c>Directory_Guards</c>/<c>Leads_Guards</c>.
    /// <list type="bullet">
    /// <item><c>event_invitations_guard</c>: a row may only be hard-deleted while still <c>NotSent</c>; once sent,
    /// the snapshot columns (<c>contact_id</c>, <c>sent_at</c>, <c>sent_by_id</c>, <c>letter_subject_snapshot</c>,
    /// <c>letter_body_snapshot</c>, <c>recipient_email</c>, <c>recipient_name</c>) are frozen — none of them, nor who
    /// sent it, can ever be rewritten, so "every send is logged" stays a durable fact even if the contact row behind
    /// it is later edited or voided.</item>
    /// <item><c>event_invitations_transitions</c>: enforces the same status whitelist as
    /// <see cref="Htn.Domain.Invitations.EventInvitation"/> at the database layer too — <c>Confirmed</c>/<c>Declined</c>
    /// are terminal, <c>Bounced → Sent</c> is the one backward edge, nothing may move back to <c>NotSent</c>.</item>
    /// <item><c>invitation_events_guard</c>: fully append-only — no updates, no deletes, ever, regardless of the
    /// parent invitation's status.</item>
    /// </list>
    /// Functions use <c>CREATE OR REPLACE</c> and every trigger is dropped first, so a half-rolled-back <c>Up</c> can
    /// be re-run. <c>htn_reject_change()</c> comes from the initial migration.
    /// </summary>
    public partial class Invitations_Guards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION htn_event_invitations_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP = 'DELETE' THEN
                        IF OLD.status <> 'NotSent' THEN
                            RAISE EXCEPTION 'event_invitations: once sent, every send is logged and the row is never deleted'
                                USING ERRCODE = 'insufficient_privilege';
                        END IF;
                        RETURN OLD;
                    END IF;
                    IF OLD.status <> 'NotSent' AND ROW(
                        NEW.contact_id, NEW.sent_at, NEW.sent_by_id,
                        NEW.letter_subject_snapshot, NEW.letter_body_snapshot, NEW.recipient_email, NEW.recipient_name
                    ) IS DISTINCT FROM ROW(
                        OLD.contact_id, OLD.sent_at, OLD.sent_by_id,
                        OLD.letter_subject_snapshot, OLD.letter_body_snapshot, OLD.recipient_email, OLD.recipient_name
                    ) THEN
                        RAISE EXCEPTION 'event_invitations: the send snapshot is frozen once set'
                            USING ERRCODE = 'insufficient_privilege';
                    END IF;
                    RETURN NEW;
                END;
                $$;
                DROP TRIGGER IF EXISTS event_invitations_guard ON event_invitations;
                CREATE TRIGGER event_invitations_guard BEFORE UPDATE OR DELETE ON event_invitations
                    FOR EACH ROW EXECUTE FUNCTION htn_event_invitations_guard();
                DROP TRIGGER IF EXISTS event_invitations_no_truncate ON event_invitations;
                CREATE TRIGGER event_invitations_no_truncate BEFORE TRUNCATE ON event_invitations
                    FOR EACH STATEMENT EXECUTE FUNCTION htn_reject_change();

                CREATE OR REPLACE FUNCTION htn_event_invitations_transitions() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF NEW.status = OLD.status THEN
                        RETURN NEW;
                    END IF;
                    IF NOT (
                        (OLD.status = 'Sent' AND NEW.status IN ('Opened', 'Replied', 'Confirmed', 'Declined', 'Bounced'))
                        OR (OLD.status = 'Opened' AND NEW.status IN ('Replied', 'Confirmed', 'Declined', 'Bounced'))
                        OR (OLD.status = 'Replied' AND NEW.status IN ('Confirmed', 'Declined', 'Bounced'))
                        OR (OLD.status = 'Bounced' AND NEW.status = 'Sent')
                        OR (OLD.status = 'NotSent' AND NEW.status = 'Sent')
                    ) THEN
                        RAISE EXCEPTION 'event_invitations: cannot move from % to %', OLD.status, NEW.status
                            USING ERRCODE = 'check_violation';
                    END IF;
                    RETURN NEW;
                END;
                $$;
                DROP TRIGGER IF EXISTS event_invitations_transitions ON event_invitations;
                CREATE TRIGGER event_invitations_transitions BEFORE UPDATE ON event_invitations
                    FOR EACH ROW EXECUTE FUNCTION htn_event_invitations_transitions();

                CREATE OR REPLACE FUNCTION htn_invitation_events_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'invitation_events: append-only, never updated or deleted'
                        USING ERRCODE = 'insufficient_privilege';
                END;
                $$;
                DROP TRIGGER IF EXISTS invitation_events_guard ON invitation_events;
                CREATE TRIGGER invitation_events_guard BEFORE UPDATE OR DELETE ON invitation_events
                    FOR EACH ROW EXECUTE FUNCTION htn_invitation_events_guard();
                DROP TRIGGER IF EXISTS invitation_events_no_truncate ON invitation_events;
                CREATE TRIGGER invitation_events_no_truncate BEFORE TRUNCATE ON invitation_events
                    FOR EACH STATEMENT EXECUTE FUNCTION htn_reject_change();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS event_invitations_guard ON event_invitations;
                DROP TRIGGER IF EXISTS event_invitations_no_truncate ON event_invitations;
                DROP TRIGGER IF EXISTS event_invitations_transitions ON event_invitations;
                DROP TRIGGER IF EXISTS invitation_events_guard ON invitation_events;
                DROP TRIGGER IF EXISTS invitation_events_no_truncate ON invitation_events;
                DROP FUNCTION IF EXISTS htn_event_invitations_guard();
                DROP FUNCTION IF EXISTS htn_event_invitations_transitions();
                DROP FUNCTION IF EXISTS htn_invitation_events_guard();
                """);
        }
    }
}
