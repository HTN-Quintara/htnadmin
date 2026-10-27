using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Htn.Infrastructure.Data.Migrations
{
    /// <summary>
    /// Phase 6B, the database half of the leads/speakers rules.
    /// <list type="bullet">
    /// <item><c>leads_guard</c>: no deletes; event, kind and created_at frozen; the sponsor/speaker link is write-once;
    /// once confirmed and linked the lead is final.</item>
    /// <item><c>speakers_guard</c>: no deletes; event frozen; lead_id write-once; a voided speaker is final.</item>
    /// </list>
    /// Functions use <c>CREATE OR REPLACE</c> and every trigger is dropped first, so a half-rolled-back <c>Up</c> can
    /// be re-run. <c>htn_reject_change()</c> comes from the initial migration.
    /// </summary>
    public partial class Leads_Guards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION htn_leads_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP = 'DELETE' THEN
                        RAISE EXCEPTION 'leads: a lead is reopened or declined with a reason, never deleted'
                            USING ERRCODE = 'insufficient_privilege';
                    END IF;
                    IF TG_OP = 'INSERT' THEN RETURN NEW; END IF;

                    IF ROW(NEW.event_id, NEW.kind, NEW.created_at, NEW.created_by_id)
                       IS DISTINCT FROM ROW(OLD.event_id, OLD.kind, OLD.created_at, OLD.created_by_id) THEN
                        RAISE EXCEPTION 'leads: a lead''s event and kind never change'
                            USING ERRCODE = 'insufficient_privilege';
                    END IF;

                    -- The link to the sponsor/speaker it became is set once and never cleared or changed.
                    IF OLD.sponsor_id IS NOT NULL AND NEW.sponsor_id IS DISTINCT FROM OLD.sponsor_id THEN
                        RAISE EXCEPTION 'leads: a confirmed sponsor link is permanent' USING ERRCODE = 'insufficient_privilege';
                    END IF;
                    IF OLD.speaker_id IS NOT NULL AND NEW.speaker_id IS DISTINCT FROM OLD.speaker_id THEN
                        RAISE EXCEPTION 'leads: a confirmed speaker link is permanent' USING ERRCODE = 'insufficient_privilege';
                    END IF;

                    -- Once confirmed AND linked, the lead is final; an unlinked confirmed lead may still be reopened.
                    IF (OLD.sponsor_id IS NOT NULL OR OLD.speaker_id IS NOT NULL) AND OLD.status = 'Confirmed'
                       AND NEW.status <> 'Confirmed' THEN
                        RAISE EXCEPTION 'leads: a confirmed, linked lead cannot be reopened' USING ERRCODE = 'check_violation';
                    END IF;

                    RETURN NEW;
                END;
                $$;
                DROP TRIGGER IF EXISTS leads_guard ON leads;
                CREATE TRIGGER leads_guard BEFORE INSERT OR UPDATE OR DELETE ON leads
                    FOR EACH ROW EXECUTE FUNCTION htn_leads_guard();
                DROP TRIGGER IF EXISTS leads_no_truncate ON leads;
                CREATE TRIGGER leads_no_truncate BEFORE TRUNCATE ON leads
                    FOR EACH STATEMENT EXECUTE FUNCTION htn_reject_change();

                CREATE OR REPLACE FUNCTION htn_speakers_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP = 'DELETE' THEN
                        RAISE EXCEPTION 'speakers: a speaker is voided with a reason, never deleted'
                            USING ERRCODE = 'insufficient_privilege';
                    END IF;
                    IF TG_OP = 'INSERT' THEN RETURN NEW; END IF;

                    IF ROW(NEW.event_id, NEW.created_at, NEW.created_by_id)
                       IS DISTINCT FROM ROW(OLD.event_id, OLD.created_at, OLD.created_by_id) THEN
                        RAISE EXCEPTION 'speakers: a speaker''s event never changes' USING ERRCODE = 'insufficient_privilege';
                    END IF;
                    IF OLD.lead_id IS NOT NULL AND NEW.lead_id IS DISTINCT FROM OLD.lead_id THEN
                        RAISE EXCEPTION 'speakers: the lead a speaker came from never changes' USING ERRCODE = 'insufficient_privilege';
                    END IF;
                    IF OLD.voided_at IS NOT NULL THEN
                        RAISE EXCEPTION 'speakers: a removed speaker is final' USING ERRCODE = 'check_violation';
                    END IF;
                    RETURN NEW;
                END;
                $$;
                DROP TRIGGER IF EXISTS speakers_guard ON speakers;
                CREATE TRIGGER speakers_guard BEFORE INSERT OR UPDATE OR DELETE ON speakers
                    FOR EACH ROW EXECUTE FUNCTION htn_speakers_guard();
                DROP TRIGGER IF EXISTS speakers_no_truncate ON speakers;
                CREATE TRIGGER speakers_no_truncate BEFORE TRUNCATE ON speakers
                    FOR EACH STATEMENT EXECUTE FUNCTION htn_reject_change();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS leads_guard ON leads;
                DROP TRIGGER IF EXISTS leads_no_truncate ON leads;
                DROP TRIGGER IF EXISTS speakers_guard ON speakers;
                DROP TRIGGER IF EXISTS speakers_no_truncate ON speakers;
                DROP FUNCTION IF EXISTS htn_leads_guard();
                DROP FUNCTION IF EXISTS htn_speakers_guard();
                """);
        }
    }
}
