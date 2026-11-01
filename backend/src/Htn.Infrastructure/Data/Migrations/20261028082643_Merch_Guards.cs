using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Htn.Infrastructure.Data.Migrations
{
    /// <summary>
    /// Phase 8A, the database half of the merch/POS rules, mirroring <c>Suppliers_Guards</c>/<c>Invitations_Guards</c>.
    /// <list type="bullet">
    /// <item><c>merch_items_guard</c>/<c>merch_variants_guard</c>: no hard deletes (deactivate with a reason instead);
    /// once deactivated the row is final.</item>
    /// <item><c>merch_sales_guard</c>/<c>merch_sale_lines_guard</c>: fully append-only, like <c>supplier_ratings_guard</c>
    /// — no update, no delete, ever. A sale and its lines (including <c>client_request_id</c>, the POS idempotency
    /// key baked into the row at insert time) are permanent the moment they are written; this unconditional
    /// reject-all is a stronger guarantee than a column-by-column freeze comparison, since it never permits any
    /// update at all, so there is nothing further to add to it when a new snapshot column (such as
    /// <c>client_request_id</c>) is introduced.</item>
    /// <item><c>merch_sale_lines_stock_decrement</c>: the actual guarantee against a race between two concurrent
    /// sales for the last unit of a variant. Fires <c>AFTER INSERT</c> on <c>merch_sale_lines</c>, and does the
    /// decrement itself (an <c>UPDATE ... RETURNING</c> against <c>merch_variants</c>), rather than trusting a
    /// value the application read earlier: that <c>UPDATE</c> takes a row lock on the variant, so a second
    /// concurrent sale for the same variant blocks until the first either commits or rolls back, and then sees the
    /// post-commit stock. If the decrement would take <c>stock_on_hand</c> below zero, it raises
    /// <c>check_violation</c> and the whole sale is rolled back — there is no window between "the app checked
    /// stock" and "the app wrote the sale" for two sales to both believe they got the last unit. The application
    /// layer still checks stock before building the sale (<c>MerchEndpoints.Sell</c>), purely so the common case
    /// gets a friendly message without a failed round trip; this trigger is what actually holds the floor at zero.
    /// </item>
    /// </list>
    /// Functions use <c>CREATE OR REPLACE</c> and every trigger is dropped first, so a half-rolled-back <c>Up</c> can
    /// be re-run. <c>htn_reject_change()</c> comes from the initial migration.
    /// </summary>
    public partial class Merch_Guards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION htn_merch_items_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP = 'DELETE' THEN
                        RAISE EXCEPTION 'merch_items: an item is deactivated with a reason, never deleted'
                            USING ERRCODE = 'insufficient_privilege';
                    END IF;
                    IF OLD.deactivated_at IS NOT NULL THEN
                        RAISE EXCEPTION 'merch_items: a deactivated item is final' USING ERRCODE = 'check_violation';
                    END IF;
                    RETURN NEW;
                END;
                $$;
                DROP TRIGGER IF EXISTS merch_items_guard ON merch_items;
                CREATE TRIGGER merch_items_guard BEFORE UPDATE OR DELETE ON merch_items
                    FOR EACH ROW EXECUTE FUNCTION htn_merch_items_guard();
                DROP TRIGGER IF EXISTS merch_items_no_truncate ON merch_items;
                CREATE TRIGGER merch_items_no_truncate BEFORE TRUNCATE ON merch_items
                    FOR EACH STATEMENT EXECUTE FUNCTION htn_reject_change();

                CREATE OR REPLACE FUNCTION htn_merch_variants_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP = 'DELETE' THEN
                        RAISE EXCEPTION 'merch_variants: a variant is deactivated with a reason, never deleted'
                            USING ERRCODE = 'insufficient_privilege';
                    END IF;
                    IF OLD.deactivated_at IS NOT NULL THEN
                        RAISE EXCEPTION 'merch_variants: a deactivated variant is final' USING ERRCODE = 'check_violation';
                    END IF;
                    RETURN NEW;
                END;
                $$;
                DROP TRIGGER IF EXISTS merch_variants_guard ON merch_variants;
                CREATE TRIGGER merch_variants_guard BEFORE UPDATE OR DELETE ON merch_variants
                    FOR EACH ROW EXECUTE FUNCTION htn_merch_variants_guard();
                DROP TRIGGER IF EXISTS merch_variants_no_truncate ON merch_variants;
                CREATE TRIGGER merch_variants_no_truncate BEFORE TRUNCATE ON merch_variants
                    FOR EACH STATEMENT EXECUTE FUNCTION htn_reject_change();

                CREATE OR REPLACE FUNCTION htn_merch_sales_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'merch_sales: append-only, never updated or deleted'
                        USING ERRCODE = 'insufficient_privilege';
                END;
                $$;
                DROP TRIGGER IF EXISTS merch_sales_guard ON merch_sales;
                CREATE TRIGGER merch_sales_guard BEFORE UPDATE OR DELETE ON merch_sales
                    FOR EACH ROW EXECUTE FUNCTION htn_merch_sales_guard();
                DROP TRIGGER IF EXISTS merch_sales_no_truncate ON merch_sales;
                CREATE TRIGGER merch_sales_no_truncate BEFORE TRUNCATE ON merch_sales
                    FOR EACH STATEMENT EXECUTE FUNCTION htn_reject_change();

                CREATE OR REPLACE FUNCTION htn_merch_sale_lines_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'merch_sale_lines: append-only, never updated or deleted'
                        USING ERRCODE = 'insufficient_privilege';
                END;
                $$;
                DROP TRIGGER IF EXISTS merch_sale_lines_guard ON merch_sale_lines;
                CREATE TRIGGER merch_sale_lines_guard BEFORE UPDATE OR DELETE ON merch_sale_lines
                    FOR EACH ROW EXECUTE FUNCTION htn_merch_sale_lines_guard();
                DROP TRIGGER IF EXISTS merch_sale_lines_no_truncate ON merch_sale_lines;
                CREATE TRIGGER merch_sale_lines_no_truncate BEFORE TRUNCATE ON merch_sale_lines
                    FOR EACH STATEMENT EXECUTE FUNCTION htn_reject_change();

                CREATE OR REPLACE FUNCTION htn_merch_sale_lines_stock_decrement() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE
                    remaining integer;
                BEGIN
                    UPDATE merch_variants SET stock_on_hand = stock_on_hand - NEW.qty
                        WHERE id = NEW.variant_id
                        RETURNING stock_on_hand INTO remaining;
                    IF remaining IS NULL THEN
                        RAISE EXCEPTION 'merch_sale_lines: unknown variant %', NEW.variant_id USING ERRCODE = 'check_violation';
                    END IF;
                    IF remaining < 0 THEN
                        RAISE EXCEPTION 'merch_variants: insufficient stock for variant %', NEW.variant_id USING ERRCODE = 'check_violation';
                    END IF;
                    RETURN NEW;
                END;
                $$;
                DROP TRIGGER IF EXISTS merch_sale_lines_stock_decrement ON merch_sale_lines;
                CREATE TRIGGER merch_sale_lines_stock_decrement AFTER INSERT ON merch_sale_lines
                    FOR EACH ROW EXECUTE FUNCTION htn_merch_sale_lines_stock_decrement();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS merch_items_guard ON merch_items;
                DROP TRIGGER IF EXISTS merch_items_no_truncate ON merch_items;
                DROP TRIGGER IF EXISTS merch_variants_guard ON merch_variants;
                DROP TRIGGER IF EXISTS merch_variants_no_truncate ON merch_variants;
                DROP TRIGGER IF EXISTS merch_sales_guard ON merch_sales;
                DROP TRIGGER IF EXISTS merch_sales_no_truncate ON merch_sales;
                DROP TRIGGER IF EXISTS merch_sale_lines_guard ON merch_sale_lines;
                DROP TRIGGER IF EXISTS merch_sale_lines_no_truncate ON merch_sale_lines;
                DROP TRIGGER IF EXISTS merch_sale_lines_stock_decrement ON merch_sale_lines;
                DROP FUNCTION IF EXISTS htn_merch_items_guard();
                DROP FUNCTION IF EXISTS htn_merch_variants_guard();
                DROP FUNCTION IF EXISTS htn_merch_sales_guard();
                DROP FUNCTION IF EXISTS htn_merch_sale_lines_guard();
                DROP FUNCTION IF EXISTS htn_merch_sale_lines_stock_decrement();
                """);
        }
    }
}
