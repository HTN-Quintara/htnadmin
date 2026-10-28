using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Htn.Infrastructure.Data.Migrations
{
    /// <summary>
    /// Phase 8C, the database half of the pop-up rules, mirroring <c>Suppliers_Guards</c>/<c>Merch_Guards</c>.
    /// <list type="bullet">
    /// <item><c>pop_up_stores_guard</c>: no hard delete once a pop-up leaves <c>Planned</c>, and status is
    /// forward-only along the exact sequence <c>Planned → Open → NeedsCount → ClosedBalanced/ClosedMissing</c> —
    /// no skipping ahead, no backward move at all (mirrors <c>EventSupplier</c>'s hire-status precedent, not
    /// <c>EventInvitation</c>'s one Bounced exception), and once <c>ClosedBalanced</c>/<c>ClosedMissing</c> the
    /// status never moves again (a write-off records who/when but doesn't change it).</item>
    /// <item><c>pop_up_allocations_guard</c>: deletable only while the owning pop-up is still <c>Planned</c>
    /// (looked up live, not trusted from the application); past that, the only legal update at all is the
    /// "close &amp; count" step filling in <c>qty_returned</c> exactly once, and only while the pop-up is
    /// <c>NeedsCount</c> — every other column is frozen.</item>
    /// <item><c>pop_up_team_members_guard</c>: never updated; deletable only while the owning pop-up is still
    /// <c>Planned</c>.</item>
    /// <item><c>pop_up_allocations_reserve</c>: the actual guarantee behind "can't allocate more leftover stock
    /// than exists" — the C# pre-check in the endpoint is only a friendly-message optimization, exactly like
    /// <c>merch_sale_lines_stock_decrement</c>'s division of labor for a sale. On insert it takes a row lock on
    /// the variant (<c>SELECT ... FOR UPDATE</c>) so two concurrent allocations for the same variant — to the
    /// same or different pop-ups — serialize instead of both reading a stale "available" figure, then sums every
    /// non-closed pop-up's reservation for that variant and rejects if it would exceed <c>stock_on_hand</c>.</item>
    /// </list>
    /// Functions use <c>CREATE OR REPLACE</c> and every trigger is dropped first, so a half-rolled-back <c>Up</c>
    /// can be re-run. <c>htn_reject_change()</c> comes from the initial migration.
    /// </summary>
    public partial class PopUps_Guards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION htn_pop_up_stores_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP = 'DELETE' THEN
                        IF OLD.status <> 'Planned' THEN
                            RAISE EXCEPTION 'pop_up_stores: a pop-up past Planned is never deleted'
                                USING ERRCODE = 'insufficient_privilege';
                        END IF;
                        RETURN OLD;
                    END IF;
                    IF NEW.status <> OLD.status THEN
                        IF OLD.status IN ('ClosedBalanced', 'ClosedMissing') THEN
                            RAISE EXCEPTION 'pop_up_stores: status is final once closed' USING ERRCODE = 'check_violation';
                        END IF;
                        IF NOT (
                            (OLD.status = 'Planned' AND NEW.status = 'Open') OR
                            (OLD.status = 'Open' AND NEW.status = 'NeedsCount') OR
                            (OLD.status = 'NeedsCount' AND NEW.status IN ('ClosedBalanced', 'ClosedMissing'))
                        ) THEN
                            RAISE EXCEPTION 'pop_up_stores: no backward or skipped status move (% -> %)', OLD.status, NEW.status
                                USING ERRCODE = 'check_violation';
                        END IF;
                    END IF;
                    RETURN NEW;
                END;
                $$;
                DROP TRIGGER IF EXISTS pop_up_stores_guard ON pop_up_stores;
                CREATE TRIGGER pop_up_stores_guard BEFORE UPDATE OR DELETE ON pop_up_stores
                    FOR EACH ROW EXECUTE FUNCTION htn_pop_up_stores_guard();
                DROP TRIGGER IF EXISTS pop_up_stores_no_truncate ON pop_up_stores;
                CREATE TRIGGER pop_up_stores_no_truncate BEFORE TRUNCATE ON pop_up_stores
                    FOR EACH STATEMENT EXECUTE FUNCTION htn_reject_change();

                CREATE OR REPLACE FUNCTION htn_pop_up_allocations_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE
                    pop_up_status text;
                BEGIN
                    SELECT status INTO pop_up_status FROM pop_up_stores WHERE id = OLD.pop_up_id;
                    IF TG_OP = 'DELETE' THEN
                        IF pop_up_status IS DISTINCT FROM 'Planned' THEN
                            RAISE EXCEPTION 'pop_up_allocations: deletable only while the pop-up is still Planned'
                                USING ERRCODE = 'insufficient_privilege';
                        END IF;
                        RETURN OLD;
                    END IF;
                    IF NEW.pop_up_id <> OLD.pop_up_id OR NEW.variant_id <> OLD.variant_id OR NEW.qty_brought <> OLD.qty_brought THEN
                        RAISE EXCEPTION 'pop_up_allocations: only qty_returned may ever be set, once' USING ERRCODE = 'check_violation';
                    END IF;
                    IF OLD.qty_returned IS NOT NULL THEN
                        RAISE EXCEPTION 'pop_up_allocations: this line has already been counted' USING ERRCODE = 'check_violation';
                    END IF;
                    -- The application sets every allocation's qty_returned and then moves the pop-up's own status
                    -- out of NeedsCount in the same SaveChanges transaction; EF doesn't guarantee which UPDATE
                    -- statement lands first, so this also accepts the already-closed status rather than only
                    -- NeedsCount, to avoid a statement-ordering race within that one transaction.
                    IF pop_up_status NOT IN ('NeedsCount', 'ClosedBalanced', 'ClosedMissing') THEN
                        RAISE EXCEPTION 'pop_up_allocations: can only be counted at or after the pop-up needs a count'
                            USING ERRCODE = 'check_violation';
                    END IF;
                    RETURN NEW;
                END;
                $$;
                DROP TRIGGER IF EXISTS pop_up_allocations_guard ON pop_up_allocations;
                CREATE TRIGGER pop_up_allocations_guard BEFORE UPDATE OR DELETE ON pop_up_allocations
                    FOR EACH ROW EXECUTE FUNCTION htn_pop_up_allocations_guard();
                DROP TRIGGER IF EXISTS pop_up_allocations_no_truncate ON pop_up_allocations;
                CREATE TRIGGER pop_up_allocations_no_truncate BEFORE TRUNCATE ON pop_up_allocations
                    FOR EACH STATEMENT EXECUTE FUNCTION htn_reject_change();

                CREATE OR REPLACE FUNCTION htn_pop_up_team_members_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE
                    pop_up_status text;
                BEGIN
                    IF TG_OP = 'UPDATE' THEN
                        RAISE EXCEPTION 'pop_up_team_members: never updated' USING ERRCODE = 'insufficient_privilege';
                    END IF;
                    SELECT status INTO pop_up_status FROM pop_up_stores WHERE id = OLD.pop_up_id;
                    IF pop_up_status IS DISTINCT FROM 'Planned' THEN
                        RAISE EXCEPTION 'pop_up_team_members: deletable only while the pop-up is still Planned'
                            USING ERRCODE = 'insufficient_privilege';
                    END IF;
                    RETURN OLD;
                END;
                $$;
                DROP TRIGGER IF EXISTS pop_up_team_members_guard ON pop_up_team_members;
                CREATE TRIGGER pop_up_team_members_guard BEFORE UPDATE OR DELETE ON pop_up_team_members
                    FOR EACH ROW EXECUTE FUNCTION htn_pop_up_team_members_guard();
                DROP TRIGGER IF EXISTS pop_up_team_members_no_truncate ON pop_up_team_members;
                CREATE TRIGGER pop_up_team_members_no_truncate BEFORE TRUNCATE ON pop_up_team_members
                    FOR EACH STATEMENT EXECUTE FUNCTION htn_reject_change();

                CREATE OR REPLACE FUNCTION htn_pop_up_allocations_reserve() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE
                    on_hand integer;
                    reserved integer;
                BEGIN
                    -- Lock the variant row first: two concurrent allocation inserts for the same variant then
                    -- serialize instead of both reading a stale "available" total and both succeeding.
                    SELECT stock_on_hand INTO on_hand FROM merch_variants WHERE id = NEW.variant_id FOR UPDATE;
                    IF on_hand IS NULL THEN
                        RAISE EXCEPTION 'pop_up_allocations: unknown variant %', NEW.variant_id USING ERRCODE = 'check_violation';
                    END IF;
                    SELECT COALESCE(SUM(a.qty_brought - COALESCE(a.qty_returned, 0)), 0) INTO reserved
                        FROM pop_up_allocations a
                        JOIN pop_up_stores s ON s.id = a.pop_up_id
                        WHERE a.variant_id = NEW.variant_id AND s.status NOT IN ('ClosedBalanced', 'ClosedMissing');
                    IF reserved > on_hand THEN
                        RAISE EXCEPTION 'pop_up_allocations: insufficient leftover stock for variant %', NEW.variant_id
                            USING ERRCODE = 'check_violation';
                    END IF;
                    RETURN NEW;
                END;
                $$;
                DROP TRIGGER IF EXISTS pop_up_allocations_reserve ON pop_up_allocations;
                CREATE TRIGGER pop_up_allocations_reserve AFTER INSERT ON pop_up_allocations
                    FOR EACH ROW EXECUTE FUNCTION htn_pop_up_allocations_reserve();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS pop_up_stores_guard ON pop_up_stores;
                DROP TRIGGER IF EXISTS pop_up_stores_no_truncate ON pop_up_stores;
                DROP TRIGGER IF EXISTS pop_up_allocations_guard ON pop_up_allocations;
                DROP TRIGGER IF EXISTS pop_up_allocations_no_truncate ON pop_up_allocations;
                DROP TRIGGER IF EXISTS pop_up_team_members_guard ON pop_up_team_members;
                DROP TRIGGER IF EXISTS pop_up_team_members_no_truncate ON pop_up_team_members;
                DROP TRIGGER IF EXISTS pop_up_allocations_reserve ON pop_up_allocations;
                DROP FUNCTION IF EXISTS htn_pop_up_stores_guard();
                DROP FUNCTION IF EXISTS htn_pop_up_allocations_guard();
                DROP FUNCTION IF EXISTS htn_pop_up_team_members_guard();
                DROP FUNCTION IF EXISTS htn_pop_up_allocations_reserve();
                """);
        }
    }
}
