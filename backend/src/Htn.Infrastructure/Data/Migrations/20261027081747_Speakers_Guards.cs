using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Htn.Infrastructure.Data.Migrations
{
    /// <summary>
    /// Phase 6D, the database half of the speaker confirmation link, travel and plus-one rules.
    /// <list type="bullet">
    /// <item><c>speaker_travel_guard</c>: no deletes; speaker/event frozen; consent is write-once; once
    /// <c>purged_at</c> is set the three sensitive columns can never be written again, even by a direct UPDATE —
    /// this is the one place the database enforces the system's only hard-delete rule.</item>
    /// <item><c>speaker_links_guard</c>: no deletes; token/expiry/speaker/event frozen (extending a link is revoke
    /// + reissue, never an edit); revoke and the first submission are write-once; a revoked link is final.</item>
    /// <item><c>speaker_link_access_guard</c>: insert only.</item>
    /// <item><c>speaker_plus_ones_guard</c>: no deletes; speaker/voucher frozen; a removed row is final.</item>
    /// </list>
    /// </summary>
    public partial class Speakers_Guards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION htn_speaker_travel_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP = 'DELETE' THEN
                        RAISE EXCEPTION 'speaker_travel: travel data is purged, never deleted'
                            USING ERRCODE = 'insufficient_privilege';
                    END IF;
                    IF TG_OP = 'UPDATE' THEN
                        IF ROW(NEW.speaker_id, NEW.event_id) IS DISTINCT FROM ROW(OLD.speaker_id, OLD.event_id) THEN
                            RAISE EXCEPTION 'speaker_travel: a travel row keeps its speaker and event'
                                USING ERRCODE = 'insufficient_privilege';
                        END IF;
                        IF OLD.consent_at IS NOT NULL
                           AND ROW(NEW.consent_at, NEW.consent_version) IS DISTINCT FROM ROW(OLD.consent_at, OLD.consent_version) THEN
                            RAISE EXCEPTION 'speaker_travel: consent is recorded once' USING ERRCODE = 'insufficient_privilege';
                        END IF;
                        IF OLD.purged_at IS NOT NULL AND (
                            NEW.flying_from IS NOT NULL OR NEW.preferred_arrival_time IS NOT NULL OR NEW.id_full_name IS NOT NULL
                        ) THEN
                            RAISE EXCEPTION 'speaker_travel: purged fields can never be written again'
                                USING ERRCODE = 'insufficient_privilege';
                        END IF;
                    END IF;
                    RETURN NEW;
                END;
                $$;
                DROP TRIGGER IF EXISTS speaker_travel_guard ON speaker_travel;
                CREATE TRIGGER speaker_travel_guard BEFORE UPDATE OR DELETE ON speaker_travel
                    FOR EACH ROW EXECUTE FUNCTION htn_speaker_travel_guard();

                CREATE OR REPLACE FUNCTION htn_speaker_links_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP = 'DELETE' THEN
                        RAISE EXCEPTION 'speaker_links: a link is revoked, never deleted' USING ERRCODE = 'insufficient_privilege';
                    END IF;
                    IF TG_OP = 'UPDATE' THEN
                        IF ROW(NEW.speaker_id, NEW.event_id, NEW.token_hash, NEW.created_at, NEW.expires_at)
                           IS DISTINCT FROM ROW(OLD.speaker_id, OLD.event_id, OLD.token_hash, OLD.created_at, OLD.expires_at) THEN
                            RAISE EXCEPTION 'speaker_links: a link keeps its token, owner and expiry; reissue instead'
                                USING ERRCODE = 'insufficient_privilege';
                        END IF;
                        IF OLD.revoked_at IS NOT NULL THEN
                            RAISE EXCEPTION 'speaker_links: this link is already revoked' USING ERRCODE = 'check_violation';
                        END IF;
                        IF OLD.submitted_at IS NOT NULL AND NEW.submitted_at IS DISTINCT FROM OLD.submitted_at THEN
                            RAISE EXCEPTION 'speaker_links: the first submission timestamp is permanent'
                                USING ERRCODE = 'insufficient_privilege';
                        END IF;
                    END IF;
                    RETURN NEW;
                END;
                $$;
                DROP TRIGGER IF EXISTS speaker_links_guard ON speaker_links;
                CREATE TRIGGER speaker_links_guard BEFORE UPDATE OR DELETE ON speaker_links
                    FOR EACH ROW EXECUTE FUNCTION htn_speaker_links_guard();

                CREATE OR REPLACE FUNCTION htn_speaker_link_access_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'speaker_link_access: the access log is never changed or deleted'
                        USING ERRCODE = 'insufficient_privilege';
                END;
                $$;
                DROP TRIGGER IF EXISTS speaker_link_access_guard ON speaker_link_access;
                CREATE TRIGGER speaker_link_access_guard BEFORE UPDATE OR DELETE ON speaker_link_access
                    FOR EACH ROW EXECUTE FUNCTION htn_speaker_link_access_guard();
                DROP TRIGGER IF EXISTS speaker_link_access_no_truncate ON speaker_link_access;
                CREATE TRIGGER speaker_link_access_no_truncate BEFORE TRUNCATE ON speaker_link_access
                    FOR EACH STATEMENT EXECUTE FUNCTION htn_reject_change();

                CREATE OR REPLACE FUNCTION htn_speaker_plus_ones_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP = 'DELETE' THEN
                        RAISE EXCEPTION 'speaker_plus_ones: a plus-one is removed with a reason, never deleted'
                            USING ERRCODE = 'insufficient_privilege';
                    END IF;
                    IF TG_OP = 'UPDATE' THEN
                        IF ROW(NEW.speaker_id, NEW.event_id, NEW.name, NEW.name_segment, NEW.email, NEW.created_at)
                           IS DISTINCT FROM ROW(OLD.speaker_id, OLD.event_id, OLD.name, OLD.name_segment, OLD.email, OLD.created_at) THEN
                            RAISE EXCEPTION 'speaker_plus_ones: a plus-one''s identity never changes; removing and adding a new row does'
                                USING ERRCODE = 'insufficient_privilege';
                        END IF;
                        IF OLD.voucher_id IS NOT NULL AND NEW.voucher_id IS DISTINCT FROM OLD.voucher_id THEN
                            RAISE EXCEPTION 'speaker_plus_ones: the voucher link is permanent' USING ERRCODE = 'insufficient_privilege';
                        END IF;
                        IF OLD.removed_at IS NOT NULL THEN
                            RAISE EXCEPTION 'speaker_plus_ones: a removed plus-one is final' USING ERRCODE = 'check_violation';
                        END IF;
                    END IF;
                    RETURN NEW;
                END;
                $$;
                DROP TRIGGER IF EXISTS speaker_plus_ones_guard ON speaker_plus_ones;
                CREATE TRIGGER speaker_plus_ones_guard BEFORE UPDATE OR DELETE ON speaker_plus_ones
                    FOR EACH ROW EXECUTE FUNCTION htn_speaker_plus_ones_guard();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS speaker_travel_guard ON speaker_travel;
                DROP TRIGGER IF EXISTS speaker_links_guard ON speaker_links;
                DROP TRIGGER IF EXISTS speaker_link_access_guard ON speaker_link_access;
                DROP TRIGGER IF EXISTS speaker_link_access_no_truncate ON speaker_link_access;
                DROP TRIGGER IF EXISTS speaker_plus_ones_guard ON speaker_plus_ones;
                DROP FUNCTION IF EXISTS htn_speaker_travel_guard();
                DROP FUNCTION IF EXISTS htn_speaker_links_guard();
                DROP FUNCTION IF EXISTS htn_speaker_link_access_guard();
                DROP FUNCTION IF EXISTS htn_speaker_plus_ones_guard();
                """);
        }
    }
}
