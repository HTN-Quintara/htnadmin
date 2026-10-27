using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Htn.Infrastructure.Data.Migrations
{
    /// <summary>
    /// Phase 6A, the database half of the sponsors/partners rules. The endpoints enforce all of this; these triggers
    /// mean it holds even for a hand-written <c>UPDATE</c> at a psql prompt, as it does for finance.
    /// <list type="bullet">
    /// <item><c>sponsors_guard</c>: no deletes; event and kind frozen; status only AwaitingContract ⇄ Confirmed, and
    /// back to AwaitingContract only while the cash is not yet received; the cash amount is frozen once received; the
    /// receipt stamp (received-at, account, reference) is write-once and may only be cleared whole by a reversal; once
    /// voided the row is final.</item>
    /// <item><c>sponsor_tiers_guard</c> / <c>event_partners_guard</c> / <c>partner_group_links_guard</c>: no deletes;
    /// the identity columns are frozen; untag and revoke are write-once.</item>
    /// <item><c>sponsor_perks_guard</c>: no deletes; a removed perk is final.</item>
    /// <item><c>sponsor_comp_registrations_guard</c>: insert only (a comp is released by cancelling its registration).</item>
    /// </list>
    /// Functions use <c>CREATE OR REPLACE</c> and every trigger is dropped first, so a half-rolled-back <c>Up</c> can
    /// be re-run. <c>htn_reject_change()</c> comes from the initial migration.
    /// </summary>
    public partial class Sponsors_Guards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION htn_sponsors_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP = 'DELETE' THEN
                        RAISE EXCEPTION 'sponsors: a sponsor is voided with a reason, never deleted'
                            USING ERRCODE = 'insufficient_privilege';
                    END IF;
                    IF TG_OP = 'INSERT' THEN
                        RETURN NEW;
                    END IF;

                    IF ROW(NEW.event_id, NEW.kind, NEW.created_at, NEW.created_by_id)
                       IS DISTINCT FROM ROW(OLD.event_id, OLD.kind, OLD.created_at, OLD.created_by_id) THEN
                        RAISE EXCEPTION 'sponsors: a sponsor''s event and kind never change'
                            USING ERRCODE = 'insufficient_privilege';
                    END IF;

                    IF OLD.voided_at IS NOT NULL THEN
                        RAISE EXCEPTION 'sponsors: a removed sponsor is final' USING ERRCODE = 'check_violation';
                    END IF;

                    IF NEW.status <> OLD.status THEN
                        IF OLD.status = 'AwaitingContract' AND NEW.status = 'Confirmed' THEN
                            NULL;
                        ELSIF OLD.status = 'Confirmed' AND NEW.status = 'AwaitingContract' THEN
                            IF OLD.cash_received_at IS NOT NULL THEN
                                RAISE EXCEPTION 'sponsors: reverse the cash before un-confirming'
                                    USING ERRCODE = 'check_violation';
                            END IF;
                        ELSE
                            RAISE EXCEPTION 'sponsors: % is not a status a sponsor may move to from %', NEW.status, OLD.status
                                USING ERRCODE = 'check_violation';
                        END IF;
                    END IF;

                    IF OLD.cash_received_at IS NOT NULL
                       AND NEW.cash_amount IS DISTINCT FROM OLD.cash_amount THEN
                        RAISE EXCEPTION 'sponsors: the cash amount is frozen once received; reverse the receipt first'
                            USING ERRCODE = 'insufficient_privilege';
                    END IF;

                    IF OLD.cash_received_at IS NOT NULL AND NEW.cash_received_at IS NOT NULL
                       AND ROW(NEW.cash_received_at, NEW.cash_payment_account_id, NEW.cash_reference)
                           IS DISTINCT FROM ROW(OLD.cash_received_at, OLD.cash_payment_account_id, OLD.cash_reference) THEN
                        RAISE EXCEPTION 'sponsors: a cash receipt is not edited; reverse it and record it again'
                            USING ERRCODE = 'insufficient_privilege';
                    END IF;

                    RETURN NEW;
                END;
                $$;
                DROP TRIGGER IF EXISTS sponsors_guard ON sponsors;
                CREATE TRIGGER sponsors_guard BEFORE INSERT OR UPDATE OR DELETE ON sponsors
                    FOR EACH ROW EXECUTE FUNCTION htn_sponsors_guard();
                DROP TRIGGER IF EXISTS sponsors_no_truncate ON sponsors;
                CREATE TRIGGER sponsors_no_truncate BEFORE TRUNCATE ON sponsors
                    FOR EACH STATEMENT EXECUTE FUNCTION htn_reject_change();

                CREATE OR REPLACE FUNCTION htn_sponsor_tiers_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP = 'DELETE' THEN
                        RAISE EXCEPTION 'sponsor_tiers: a tier is deactivated, never deleted'
                            USING ERRCODE = 'insufficient_privilege';
                    END IF;
                    IF TG_OP = 'UPDATE'
                       AND ROW(NEW.event_id, NEW.class, NEW.created_at) IS DISTINCT FROM ROW(OLD.event_id, OLD.class, OLD.created_at) THEN
                        RAISE EXCEPTION 'sponsor_tiers: a tier keeps its event and class'
                            USING ERRCODE = 'insufficient_privilege';
                    END IF;
                    RETURN NEW;
                END;
                $$;
                DROP TRIGGER IF EXISTS sponsor_tiers_guard ON sponsor_tiers;
                CREATE TRIGGER sponsor_tiers_guard BEFORE UPDATE OR DELETE ON sponsor_tiers
                    FOR EACH ROW EXECUTE FUNCTION htn_sponsor_tiers_guard();

                CREATE OR REPLACE FUNCTION htn_sponsor_perks_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP = 'DELETE' THEN
                        RAISE EXCEPTION 'sponsor_perks: a perk is removed with a reason, never deleted'
                            USING ERRCODE = 'insufficient_privilege';
                    END IF;
                    IF TG_OP = 'UPDATE' THEN
                        IF ROW(NEW.sponsor_id, NEW.event_id, NEW.text, NEW.created_at)
                           IS DISTINCT FROM ROW(OLD.sponsor_id, OLD.event_id, OLD.text, OLD.created_at) THEN
                            RAISE EXCEPTION 'sponsor_perks: a perk''s wording and owner never change'
                                USING ERRCODE = 'insufficient_privilege';
                        END IF;
                        IF OLD.removed_at IS NOT NULL THEN
                            RAISE EXCEPTION 'sponsor_perks: a removed perk is final' USING ERRCODE = 'check_violation';
                        END IF;
                    END IF;
                    RETURN NEW;
                END;
                $$;
                DROP TRIGGER IF EXISTS sponsor_perks_guard ON sponsor_perks;
                CREATE TRIGGER sponsor_perks_guard BEFORE UPDATE OR DELETE ON sponsor_perks
                    FOR EACH ROW EXECUTE FUNCTION htn_sponsor_perks_guard();

                CREATE OR REPLACE FUNCTION htn_sponsor_comp_registrations_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'sponsor_comp_registrations: a comp link is never changed or deleted; cancel the registration'
                        USING ERRCODE = 'insufficient_privilege';
                END;
                $$;
                DROP TRIGGER IF EXISTS sponsor_comp_registrations_guard ON sponsor_comp_registrations;
                CREATE TRIGGER sponsor_comp_registrations_guard BEFORE UPDATE OR DELETE ON sponsor_comp_registrations
                    FOR EACH ROW EXECUTE FUNCTION htn_sponsor_comp_registrations_guard();
                DROP TRIGGER IF EXISTS sponsor_comp_registrations_no_truncate ON sponsor_comp_registrations;
                CREATE TRIGGER sponsor_comp_registrations_no_truncate BEFORE TRUNCATE ON sponsor_comp_registrations
                    FOR EACH STATEMENT EXECUTE FUNCTION htn_reject_change();

                CREATE OR REPLACE FUNCTION htn_event_partners_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP = 'DELETE' THEN
                        RAISE EXCEPTION 'event_partners: a partner is untagged with a reason, never deleted'
                            USING ERRCODE = 'insufficient_privilege';
                    END IF;
                    IF TG_OP = 'UPDATE'
                       AND ROW(NEW.event_id, NEW.name_key, NEW.created_at) IS DISTINCT FROM ROW(OLD.event_id, OLD.name_key, OLD.created_at) THEN
                        RAISE EXCEPTION 'event_partners: a partner keeps its event and name'
                            USING ERRCODE = 'insufficient_privilege';
                    END IF;
                    RETURN NEW;
                END;
                $$;
                DROP TRIGGER IF EXISTS event_partners_guard ON event_partners;
                CREATE TRIGGER event_partners_guard BEFORE UPDATE OR DELETE ON event_partners
                    FOR EACH ROW EXECUTE FUNCTION htn_event_partners_guard();

                CREATE OR REPLACE FUNCTION htn_partner_group_links_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP = 'DELETE' THEN
                        RAISE EXCEPTION 'partner_group_links: a link is revoked, never deleted'
                            USING ERRCODE = 'insufficient_privilege';
                    END IF;
                    IF TG_OP = 'UPDATE' THEN
                        IF ROW(NEW.event_id, NEW.event_partner_id, NEW.token_hash, NEW.created_at)
                           IS DISTINCT FROM ROW(OLD.event_id, OLD.event_partner_id, OLD.token_hash, OLD.created_at) THEN
                            RAISE EXCEPTION 'partner_group_links: a link keeps its token and owner'
                                USING ERRCODE = 'insufficient_privilege';
                        END IF;
                        IF OLD.revoked_at IS NOT NULL THEN
                            RAISE EXCEPTION 'partner_group_links: this link is already revoked'
                                USING ERRCODE = 'check_violation';
                        END IF;
                    END IF;
                    RETURN NEW;
                END;
                $$;
                DROP TRIGGER IF EXISTS partner_group_links_guard ON partner_group_links;
                CREATE TRIGGER partner_group_links_guard BEFORE UPDATE OR DELETE ON partner_group_links
                    FOR EACH ROW EXECUTE FUNCTION htn_partner_group_links_guard();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS sponsors_guard ON sponsors;
                DROP TRIGGER IF EXISTS sponsors_no_truncate ON sponsors;
                DROP TRIGGER IF EXISTS sponsor_tiers_guard ON sponsor_tiers;
                DROP TRIGGER IF EXISTS sponsor_perks_guard ON sponsor_perks;
                DROP TRIGGER IF EXISTS sponsor_comp_registrations_guard ON sponsor_comp_registrations;
                DROP TRIGGER IF EXISTS sponsor_comp_registrations_no_truncate ON sponsor_comp_registrations;
                DROP TRIGGER IF EXISTS event_partners_guard ON event_partners;
                DROP TRIGGER IF EXISTS partner_group_links_guard ON partner_group_links;
                DROP FUNCTION IF EXISTS htn_sponsors_guard();
                DROP FUNCTION IF EXISTS htn_sponsor_tiers_guard();
                DROP FUNCTION IF EXISTS htn_sponsor_perks_guard();
                DROP FUNCTION IF EXISTS htn_sponsor_comp_registrations_guard();
                DROP FUNCTION IF EXISTS htn_event_partners_guard();
                DROP FUNCTION IF EXISTS htn_partner_group_links_guard();
                """);
        }
    }
}
