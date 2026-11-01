using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Htn.Infrastructure.Data.Migrations
{
    /// <summary>
    /// Phase 5, the database half of the finance rules. The endpoints already enforce all of this; these triggers mean
    /// it holds even for a hand-written <c>UPDATE</c> at a psql prompt, which is the point for money.
    /// <list type="bullet">
    /// <item><c>ledger_entries_guard</c>: no deletes, ever. The money facts (fund, account, category, date, direction,
    /// amount, description, source, who entered it) are frozen at insert; the only updates allowed are
    /// Pending → Posted stamping an approver, → Voided stamping a reason, and the close/reopen stamp.</item>
    /// <item><c>ledger_entries_closed_period</c>: nothing is inserted into, or voided in, a closed month — a
    /// correction is a contra entry dated today. Deferred, because the close itself stamps the rows it closes.</item>
    /// <item><c>ledger_transfer_pair</c>: a transfer group is always exactly two opposite legs, same amount, same date,
    /// two different funds — and voiding one leg forces the other. Deferred, so EF may write the legs in any order
    /// inside one <c>SaveChangesAsync</c>.</item>
    /// <item><c>month_closes_guard</c>: a close is never deleted or edited; it is only reopened, and only when it is
    /// the most recent one, so periods can never be reopened out of order.</item>
    /// <item><c>account_reconciliations_guard</c>: superseded, never edited or deleted.</item>
    /// <item>Budget versions and their lines are fully immutable: the next save is a new version.</item>
    /// <item><c>funds_guard</c> / <c>ledger_categories_guard</c>: no deletes; a fund's kind and event and a category's
    /// code and system flag never change.</item>
    /// </list>
    /// Functions use <c>CREATE OR REPLACE</c> and every trigger is dropped first, so a half-rolled-back <c>Up</c> can
    /// be re-run. <c>htn_reject_change()</c> comes from the initial migration.
    /// </summary>
    public partial class Finance_Guards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                -- A ledger entry is append-only: it is approved, voided, closed or reopened, and nothing else.
                CREATE OR REPLACE FUNCTION htn_ledger_entries_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP = 'DELETE' THEN
                        RAISE EXCEPTION 'ledger_entries: entries are voided with a reason, never deleted'
                            USING ERRCODE = 'insufficient_privilege';
                    END IF;

                    IF TG_OP = 'INSERT' THEN
                        IF NEW.status = 'Voided' THEN
                            RAISE EXCEPTION 'ledger_entries: an entry is never created already voided'
                                USING ERRCODE = 'check_violation';
                        END IF;
                        IF NEW.status = 'Posted' AND (NEW.approved_at IS NULL) THEN
                            RAISE EXCEPTION 'ledger_entries: a posted entry carries its approval'
                                USING ERRCODE = 'check_violation';
                        END IF;
                        IF NEW.month_close_id IS NOT NULL THEN
                            RAISE EXCEPTION 'ledger_entries: a new entry is never born into a closed month'
                                USING ERRCODE = 'check_violation';
                        END IF;
                        RETURN NEW;
                    END IF;

                    -- What the money was: frozen for the life of the row.
                    IF ROW(NEW.fund_id, NEW.payment_account_id, NEW.category_id, NEW.entry_date, NEW.direction,
                           NEW.amount, NEW.reference, NEW.description, NEW.receipt_file_id, NEW.source,
                           NEW.source_id, NEW.transfer_group_id, NEW.contra_of_entry_id, NEW.entered_by_id,
                           NEW.entered_at)
                       IS DISTINCT FROM
                       ROW(OLD.fund_id, OLD.payment_account_id, OLD.category_id, OLD.entry_date, OLD.direction,
                           OLD.amount, OLD.reference, OLD.description, OLD.receipt_file_id, OLD.source,
                           OLD.source_id, OLD.transfer_group_id, OLD.contra_of_entry_id, OLD.entered_by_id,
                           OLD.entered_at) THEN
                        RAISE EXCEPTION 'ledger_entries: an entry is never edited; void it and post a new one'
                            USING ERRCODE = 'insufficient_privilege';
                    END IF;

                    IF OLD.status = 'Voided' THEN
                        RAISE EXCEPTION 'ledger_entries: a voided entry is final' USING ERRCODE = 'check_violation';
                    END IF;

                    -- An approval, once given, is part of the record.
                    IF OLD.status = 'Posted'
                       AND ROW(NEW.approved_by_id, NEW.approved_at) IS DISTINCT FROM ROW(OLD.approved_by_id, OLD.approved_at) THEN
                        RAISE EXCEPTION 'ledger_entries: an approval is never changed' USING ERRCODE = 'insufficient_privilege';
                    END IF;

                    IF NEW.status = OLD.status THEN
                        -- The only standing change is the close stamp going on (close) or off (reopen).
                        IF NEW.month_close_id IS NOT DISTINCT FROM OLD.month_close_id THEN
                            RAISE EXCEPTION 'ledger_entries: nothing on an entry is editable'
                                USING ERRCODE = 'insufficient_privilege';
                        END IF;
                        IF OLD.status <> 'Posted' THEN
                            RAISE EXCEPTION 'ledger_entries: only a posted entry belongs to a closed month'
                                USING ERRCODE = 'check_violation';
                        END IF;
                        RETURN NEW;
                    END IF;

                    IF OLD.status = 'Pending' AND NEW.status = 'Posted' THEN
                        IF NEW.approved_at IS NULL OR NEW.approved_by_id IS NULL THEN
                            RAISE EXCEPTION 'ledger_entries: approving stamps who approved it and when'
                                USING ERRCODE = 'check_violation';
                        END IF;
                        RETURN NEW;
                    END IF;

                    IF NEW.status = 'Voided' THEN
                        IF NEW.voided_at IS NULL OR NEW.void_reason IS NULL THEN
                            RAISE EXCEPTION 'ledger_entries: voiding needs a reason' USING ERRCODE = 'check_violation';
                        END IF;
                        IF OLD.month_close_id IS NOT NULL THEN
                            RAISE EXCEPTION 'ledger_entries: this month is closed; post a correcting entry dated today'
                                USING ERRCODE = 'check_violation';
                        END IF;
                        RETURN NEW;
                    END IF;

                    RAISE EXCEPTION 'ledger_entries: % is not a status an entry may move to from %', NEW.status, OLD.status
                        USING ERRCODE = 'check_violation';
                END;
                $$;
                DROP TRIGGER IF EXISTS ledger_entries_guard ON ledger_entries;
                CREATE TRIGGER ledger_entries_guard BEFORE INSERT OR UPDATE OR DELETE ON ledger_entries
                    FOR EACH ROW EXECUTE FUNCTION htn_ledger_entries_guard();
                DROP TRIGGER IF EXISTS ledger_entries_no_truncate ON ledger_entries;
                CREATE TRIGGER ledger_entries_no_truncate BEFORE TRUNCATE ON ledger_entries
                    FOR EACH STATEMENT EXECUTE FUNCTION htn_reject_change();

                -- A closed month is settled. Deferred, because closing the month stamps the very rows it closes, and
                -- reopening clears them: at commit the month_closes row already tells us which it was.
                CREATE OR REPLACE FUNCTION htn_ledger_closed_period() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE
                    v_closed uuid;
                BEGIN
                    SELECT mc.id INTO v_closed FROM month_closes mc
                    WHERE mc.reopened_at IS NULL
                      AND make_date(mc.year, mc.month, 1) = date_trunc('month', NEW.entry_date)::date
                    LIMIT 1;
                    IF v_closed IS NULL THEN
                        RETURN NULL;
                    END IF;
                    IF TG_OP = 'UPDATE' AND NEW.month_close_id IS NOT NULL AND OLD.month_close_id IS NULL THEN
                        RETURN NULL;  -- the close itself
                    END IF;
                    RAISE EXCEPTION 'ledger_entries: % falls in a closed month; post a correcting entry dated today', NEW.entry_date
                        USING ERRCODE = 'check_violation';
                END;
                $$;
                DROP TRIGGER IF EXISTS ledger_entries_closed_period ON ledger_entries;
                CREATE CONSTRAINT TRIGGER ledger_entries_closed_period
                    AFTER INSERT OR UPDATE ON ledger_entries
                    DEFERRABLE INITIALLY DEFERRED
                    FOR EACH ROW EXECUTE FUNCTION htn_ledger_closed_period();

                -- Both sides of a transfer, or neither. Deferred so the two legs may be written in either order.
                CREATE OR REPLACE FUNCTION htn_ledger_transfer_pair() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE
                    v_group uuid := COALESCE(NEW.transfer_group_id, OLD.transfer_group_id);
                    v_count int;
                    v_in int;
                    v_out int;
                    v_amounts int;
                    v_funds int;
                    v_dates int;
                BEGIN
                    IF v_group IS NULL THEN
                        RETURN NULL;
                    END IF;
                    SELECT count(*),
                           count(*) FILTER (WHERE direction = 'In'),
                           count(*) FILTER (WHERE direction = 'Out'),
                           count(DISTINCT amount),
                           count(DISTINCT fund_id),
                           count(DISTINCT entry_date)
                      INTO v_count, v_in, v_out, v_amounts, v_funds, v_dates
                      FROM ledger_entries
                     WHERE transfer_group_id = v_group AND status <> 'Voided';
                    IF v_count = 0 THEN
                        RETURN NULL;  -- the whole transfer was voided, both legs together
                    END IF;
                    IF v_count <> 2 OR v_in <> 1 OR v_out <> 1 OR v_amounts <> 1 OR v_funds <> 2 OR v_dates <> 1 THEN
                        RAISE EXCEPTION 'ledger_entries: a transfer is two opposite legs of one amount and date, in two different funds'
                            USING ERRCODE = 'check_violation';
                    END IF;
                    RETURN NULL;
                END;
                $$;
                DROP TRIGGER IF EXISTS ledger_transfer_pair ON ledger_entries;
                CREATE CONSTRAINT TRIGGER ledger_transfer_pair
                    AFTER INSERT OR UPDATE OR DELETE ON ledger_entries
                    DEFERRABLE INITIALLY DEFERRED
                    FOR EACH ROW EXECUTE FUNCTION htn_ledger_transfer_pair();

                -- A month close is reopened, never rewritten, and only ever the latest one.
                CREATE OR REPLACE FUNCTION htn_month_closes_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP = 'DELETE' THEN
                        RAISE EXCEPTION 'month_closes: a close is reopened with a reason, never deleted'
                            USING ERRCODE = 'insufficient_privilege';
                    END IF;
                    IF ROW(NEW.year, NEW.month, NEW.closed_by_id, NEW.closed_at, NEW.note)
                       IS DISTINCT FROM ROW(OLD.year, OLD.month, OLD.closed_by_id, OLD.closed_at, OLD.note) THEN
                        RAISE EXCEPTION 'month_closes: a close is never edited' USING ERRCODE = 'insufficient_privilege';
                    END IF;
                    IF OLD.reopened_at IS NOT NULL THEN
                        RAISE EXCEPTION 'month_closes: this month was already reopened' USING ERRCODE = 'check_violation';
                    END IF;
                    IF NEW.reopened_at IS NULL THEN
                        RAISE EXCEPTION 'month_closes: the only change to a close is reopening it'
                            USING ERRCODE = 'check_violation';
                    END IF;
                    IF EXISTS (SELECT 1 FROM month_closes mc
                               WHERE mc.id <> NEW.id AND mc.reopened_at IS NULL
                                 AND make_date(mc.year, mc.month, 1) > make_date(NEW.year, NEW.month, 1)) THEN
                        RAISE EXCEPTION 'month_closes: only the most recently closed month can be reopened'
                            USING ERRCODE = 'check_violation';
                    END IF;
                    RETURN NEW;
                END;
                $$;
                DROP TRIGGER IF EXISTS month_closes_guard ON month_closes;
                CREATE TRIGGER month_closes_guard BEFORE UPDATE OR DELETE ON month_closes
                    FOR EACH ROW EXECUTE FUNCTION htn_month_closes_guard();
                DROP TRIGGER IF EXISTS month_closes_no_truncate ON month_closes;
                CREATE TRIGGER month_closes_no_truncate BEFORE TRUNCATE ON month_closes
                    FOR EACH STATEMENT EXECUTE FUNCTION htn_reject_change();

                -- A reconciliation is superseded by a newer one; what the CFO believed, and when, is kept.
                CREATE OR REPLACE FUNCTION htn_account_reconciliations_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP = 'DELETE' THEN
                        RAISE EXCEPTION 'account_reconciliations: a reconciliation is superseded, never deleted'
                            USING ERRCODE = 'insufficient_privilege';
                    END IF;
                    IF ROW(NEW.payment_account_id, NEW.year, NEW.month, NEW.statement_balance, NEW.book_balance,
                           NEW.difference, NEW.note, NEW.reconciled_by_id, NEW.reconciled_at)
                       IS DISTINCT FROM
                       ROW(OLD.payment_account_id, OLD.year, OLD.month, OLD.statement_balance, OLD.book_balance,
                           OLD.difference, OLD.note, OLD.reconciled_by_id, OLD.reconciled_at) THEN
                        RAISE EXCEPTION 'account_reconciliations: reconcile again instead of editing this row'
                            USING ERRCODE = 'insufficient_privilege';
                    END IF;
                    IF OLD.superseded_at IS NOT NULL OR NEW.superseded_at IS NULL THEN
                        RAISE EXCEPTION 'account_reconciliations: the only change is superseding it'
                            USING ERRCODE = 'check_violation';
                    END IF;
                    RETURN NEW;
                END;
                $$;
                DROP TRIGGER IF EXISTS account_reconciliations_guard ON account_reconciliations;
                CREATE TRIGGER account_reconciliations_guard BEFORE UPDATE OR DELETE ON account_reconciliations
                    FOR EACH ROW EXECUTE FUNCTION htn_account_reconciliations_guard();

                -- A saved budget version is the audit trail: the next change is the next version.
                DROP TRIGGER IF EXISTS event_budget_versions_frozen ON event_budget_versions;
                CREATE TRIGGER event_budget_versions_frozen BEFORE UPDATE OR DELETE ON event_budget_versions
                    FOR EACH ROW EXECUTE FUNCTION htn_reject_change();
                DROP TRIGGER IF EXISTS event_budget_versions_no_truncate ON event_budget_versions;
                CREATE TRIGGER event_budget_versions_no_truncate BEFORE TRUNCATE ON event_budget_versions
                    FOR EACH STATEMENT EXECUTE FUNCTION htn_reject_change();
                DROP TRIGGER IF EXISTS event_budget_lines_frozen ON event_budget_lines;
                CREATE TRIGGER event_budget_lines_frozen BEFORE UPDATE OR DELETE ON event_budget_lines
                    FOR EACH ROW EXECUTE FUNCTION htn_reject_change();
                DROP TRIGGER IF EXISTS event_budget_lines_no_truncate ON event_budget_lines;
                CREATE TRIGGER event_budget_lines_no_truncate BEFORE TRUNCATE ON event_budget_lines
                    FOR EACH STATEMENT EXECUTE FUNCTION htn_reject_change();

                -- A fund never changes what it is for, and is never deleted.
                CREATE OR REPLACE FUNCTION htn_funds_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP = 'DELETE' THEN
                        RAISE EXCEPTION 'funds: a fund is closed, never deleted' USING ERRCODE = 'insufficient_privilege';
                    END IF;
                    IF ROW(NEW.kind, NEW.event_id, NEW.created_at) IS DISTINCT FROM ROW(OLD.kind, OLD.event_id, OLD.created_at) THEN
                        RAISE EXCEPTION 'funds: what a fund belongs to never changes' USING ERRCODE = 'insufficient_privilege';
                    END IF;
                    IF OLD.swept_at IS NOT NULL AND NEW.swept_at IS DISTINCT FROM OLD.swept_at THEN
                        RAISE EXCEPTION 'funds: a surplus is transferred once' USING ERRCODE = 'check_violation';
                    END IF;
                    RETURN NEW;
                END;
                $$;
                DROP TRIGGER IF EXISTS funds_guard ON funds;
                CREATE TRIGGER funds_guard BEFORE UPDATE OR DELETE ON funds
                    FOR EACH ROW EXECUTE FUNCTION htn_funds_guard();

                -- A category keeps its code and its system flag, because posting resolves categories by them.
                CREATE OR REPLACE FUNCTION htn_ledger_categories_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP = 'DELETE' THEN
                        RAISE EXCEPTION 'ledger_categories: a category is deactivated, never deleted'
                            USING ERRCODE = 'insufficient_privilege';
                    END IF;
                    IF ROW(NEW.code, NEW.is_system, NEW.kind, NEW.created_at)
                       IS DISTINCT FROM ROW(OLD.code, OLD.is_system, OLD.kind, OLD.created_at) THEN
                        RAISE EXCEPTION 'ledger_categories: a category keeps its code, kind and system flag'
                            USING ERRCODE = 'insufficient_privilege';
                    END IF;
                    RETURN NEW;
                END;
                $$;
                DROP TRIGGER IF EXISTS ledger_categories_guard ON ledger_categories;
                CREATE TRIGGER ledger_categories_guard BEFORE UPDATE OR DELETE ON ledger_categories
                    FOR EACH ROW EXECUTE FUNCTION htn_ledger_categories_guard();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS ledger_entries_guard ON ledger_entries;
                DROP TRIGGER IF EXISTS ledger_entries_no_truncate ON ledger_entries;
                DROP TRIGGER IF EXISTS ledger_entries_closed_period ON ledger_entries;
                DROP TRIGGER IF EXISTS ledger_transfer_pair ON ledger_entries;
                DROP TRIGGER IF EXISTS month_closes_guard ON month_closes;
                DROP TRIGGER IF EXISTS month_closes_no_truncate ON month_closes;
                DROP TRIGGER IF EXISTS account_reconciliations_guard ON account_reconciliations;
                DROP TRIGGER IF EXISTS event_budget_versions_frozen ON event_budget_versions;
                DROP TRIGGER IF EXISTS event_budget_versions_no_truncate ON event_budget_versions;
                DROP TRIGGER IF EXISTS event_budget_lines_frozen ON event_budget_lines;
                DROP TRIGGER IF EXISTS event_budget_lines_no_truncate ON event_budget_lines;
                DROP TRIGGER IF EXISTS funds_guard ON funds;
                DROP TRIGGER IF EXISTS ledger_categories_guard ON ledger_categories;
                DROP FUNCTION IF EXISTS htn_ledger_entries_guard();
                DROP FUNCTION IF EXISTS htn_ledger_closed_period();
                DROP FUNCTION IF EXISTS htn_ledger_transfer_pair();
                DROP FUNCTION IF EXISTS htn_month_closes_guard();
                DROP FUNCTION IF EXISTS htn_account_reconciliations_guard();
                DROP FUNCTION IF EXISTS htn_funds_guard();
                DROP FUNCTION IF EXISTS htn_ledger_categories_guard();
                """);
        }
    }
}
