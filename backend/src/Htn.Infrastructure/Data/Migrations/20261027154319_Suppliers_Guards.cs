using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Htn.Infrastructure.Data.Migrations
{
    /// <summary>
    /// Phase 7C, the database half of the suppliers rules, mirroring <c>Directory_Guards</c>/<c>Invitations_Guards</c>.
    /// <list type="bullet">
    /// <item><c>suppliers_guard</c>: no deletes (deactivate with a reason instead); <c>name</c>, <c>name_key</c> and
    /// <c>category</c> are frozen once set, exactly like <c>directory_orgs</c>' <c>name</c>/<c>name_key</c>/<c>kind</c>;
    /// once deactivated the row is final.</item>
    /// <item><c>supplier_documents_guard</c>: no deletes (void with a reason instead); <c>supplier_id</c>,
    /// <c>kind</c>, <c>file_id</c> and <c>uploaded_at</c> are frozen once set — a replacement document is a new row,
    /// not an edit of this one; once voided the row is final.</item>
    /// <item><c>event_suppliers_guard</c>: a hire may only be hard-deleted while still <c>Quotation</c> — the one
    /// judgment call this slice makes by analogy with <c>EventInvitation</c>'s <c>NotSent</c> delete window, since
    /// nothing irreversible (money, a booking) has happened yet at that stage; once <c>Booked</c> or later, the row
    /// is permanent and must be voided. <c>event_id</c>, <c>supplier_id</c>, <c>category_id</c> and
    /// <c>owner_committee_id</c> are frozen once set; once voided the row is final.</item>
    /// <item><c>event_suppliers_transitions</c>: enforces the forward-only whitelist at the database too — exactly
    /// Quotation → Booked → DepositPaid → FullyPaid, one step at a time, with no backward move at all (unlike
    /// <c>event_invitations</c>'s one Bounced → Sent exception). If a booking falls through, the hire is voided and
    /// a new one created instead of rewinding the status.</item>
    /// <item><c>supplier_ratings_guard</c>: fully append-only — no updates, no deletes, ever, mirroring
    /// <c>invitation_events_guard</c>.</item>
    /// </list>
    /// Functions use <c>CREATE OR REPLACE</c> and every trigger is dropped first, so a half-rolled-back <c>Up</c> can
    /// be re-run. <c>htn_reject_change()</c> comes from the initial migration.
    /// </summary>
    public partial class Suppliers_Guards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION htn_suppliers_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP = 'DELETE' THEN
                        RAISE EXCEPTION 'suppliers: a supplier is deactivated with a reason, never deleted'
                            USING ERRCODE = 'insufficient_privilege';
                    END IF;
                    IF ROW(NEW.name, NEW.name_key, NEW.category, NEW.created_at)
                       IS DISTINCT FROM ROW(OLD.name, OLD.name_key, OLD.category, OLD.created_at) THEN
                        RAISE EXCEPTION 'suppliers: name, name_key and category are frozen once set'
                            USING ERRCODE = 'insufficient_privilege';
                    END IF;
                    IF OLD.deactivated_at IS NOT NULL THEN
                        RAISE EXCEPTION 'suppliers: a deactivated supplier is final' USING ERRCODE = 'check_violation';
                    END IF;
                    RETURN NEW;
                END;
                $$;
                DROP TRIGGER IF EXISTS suppliers_guard ON suppliers;
                CREATE TRIGGER suppliers_guard BEFORE UPDATE OR DELETE ON suppliers
                    FOR EACH ROW EXECUTE FUNCTION htn_suppliers_guard();
                DROP TRIGGER IF EXISTS suppliers_no_truncate ON suppliers;
                CREATE TRIGGER suppliers_no_truncate BEFORE TRUNCATE ON suppliers
                    FOR EACH STATEMENT EXECUTE FUNCTION htn_reject_change();

                CREATE OR REPLACE FUNCTION htn_supplier_documents_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP = 'DELETE' THEN
                        RAISE EXCEPTION 'supplier_documents: a document is voided with a reason, never deleted'
                            USING ERRCODE = 'insufficient_privilege';
                    END IF;
                    IF ROW(NEW.supplier_id, NEW.kind, NEW.file_id, NEW.uploaded_at)
                       IS DISTINCT FROM ROW(OLD.supplier_id, OLD.kind, OLD.file_id, OLD.uploaded_at) THEN
                        RAISE EXCEPTION 'supplier_documents: supplier, kind, file and upload time are frozen once set'
                            USING ERRCODE = 'insufficient_privilege';
                    END IF;
                    IF OLD.voided_at IS NOT NULL THEN
                        RAISE EXCEPTION 'supplier_documents: a removed document is final' USING ERRCODE = 'check_violation';
                    END IF;
                    RETURN NEW;
                END;
                $$;
                DROP TRIGGER IF EXISTS supplier_documents_guard ON supplier_documents;
                CREATE TRIGGER supplier_documents_guard BEFORE UPDATE OR DELETE ON supplier_documents
                    FOR EACH ROW EXECUTE FUNCTION htn_supplier_documents_guard();
                DROP TRIGGER IF EXISTS supplier_documents_no_truncate ON supplier_documents;
                CREATE TRIGGER supplier_documents_no_truncate BEFORE TRUNCATE ON supplier_documents
                    FOR EACH STATEMENT EXECUTE FUNCTION htn_reject_change();

                CREATE OR REPLACE FUNCTION htn_event_suppliers_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP = 'DELETE' THEN
                        IF OLD.status <> 'Quotation' THEN
                            RAISE EXCEPTION 'event_suppliers: a hire past Quotation is voided, never deleted'
                                USING ERRCODE = 'insufficient_privilege';
                        END IF;
                        RETURN OLD;
                    END IF;
                    IF ROW(NEW.event_id, NEW.supplier_id, NEW.category_id, NEW.owner_committee_id, NEW.created_at)
                       IS DISTINCT FROM ROW(OLD.event_id, OLD.supplier_id, OLD.category_id, OLD.owner_committee_id, OLD.created_at) THEN
                        RAISE EXCEPTION 'event_suppliers: event, supplier, category and owning committee are frozen once set'
                            USING ERRCODE = 'insufficient_privilege';
                    END IF;
                    IF OLD.voided_at IS NOT NULL THEN
                        RAISE EXCEPTION 'event_suppliers: a voided hire is final' USING ERRCODE = 'check_violation';
                    END IF;
                    RETURN NEW;
                END;
                $$;
                DROP TRIGGER IF EXISTS event_suppliers_guard ON event_suppliers;
                CREATE TRIGGER event_suppliers_guard BEFORE UPDATE OR DELETE ON event_suppliers
                    FOR EACH ROW EXECUTE FUNCTION htn_event_suppliers_guard();
                DROP TRIGGER IF EXISTS event_suppliers_no_truncate ON event_suppliers;
                CREATE TRIGGER event_suppliers_no_truncate BEFORE TRUNCATE ON event_suppliers
                    FOR EACH STATEMENT EXECUTE FUNCTION htn_reject_change();

                CREATE OR REPLACE FUNCTION htn_event_suppliers_transitions() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF NEW.status = OLD.status THEN
                        RETURN NEW;
                    END IF;
                    IF NOT (
                        (OLD.status = 'Quotation' AND NEW.status = 'Booked')
                        OR (OLD.status = 'Booked' AND NEW.status = 'DepositPaid')
                        OR (OLD.status = 'DepositPaid' AND NEW.status = 'FullyPaid')
                    ) THEN
                        RAISE EXCEPTION 'event_suppliers: cannot move from % to %', OLD.status, NEW.status
                            USING ERRCODE = 'check_violation';
                    END IF;
                    RETURN NEW;
                END;
                $$;
                DROP TRIGGER IF EXISTS event_suppliers_transitions ON event_suppliers;
                CREATE TRIGGER event_suppliers_transitions BEFORE UPDATE ON event_suppliers
                    FOR EACH ROW EXECUTE FUNCTION htn_event_suppliers_transitions();

                CREATE OR REPLACE FUNCTION htn_supplier_ratings_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'supplier_ratings: append-only, never updated or deleted'
                        USING ERRCODE = 'insufficient_privilege';
                END;
                $$;
                DROP TRIGGER IF EXISTS supplier_ratings_guard ON supplier_ratings;
                CREATE TRIGGER supplier_ratings_guard BEFORE UPDATE OR DELETE ON supplier_ratings
                    FOR EACH ROW EXECUTE FUNCTION htn_supplier_ratings_guard();
                DROP TRIGGER IF EXISTS supplier_ratings_no_truncate ON supplier_ratings;
                CREATE TRIGGER supplier_ratings_no_truncate BEFORE TRUNCATE ON supplier_ratings
                    FOR EACH STATEMENT EXECUTE FUNCTION htn_reject_change();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS suppliers_guard ON suppliers;
                DROP TRIGGER IF EXISTS suppliers_no_truncate ON suppliers;
                DROP TRIGGER IF EXISTS supplier_documents_guard ON supplier_documents;
                DROP TRIGGER IF EXISTS supplier_documents_no_truncate ON supplier_documents;
                DROP TRIGGER IF EXISTS event_suppliers_guard ON event_suppliers;
                DROP TRIGGER IF EXISTS event_suppliers_no_truncate ON event_suppliers;
                DROP TRIGGER IF EXISTS event_suppliers_transitions ON event_suppliers;
                DROP TRIGGER IF EXISTS supplier_ratings_guard ON supplier_ratings;
                DROP TRIGGER IF EXISTS supplier_ratings_no_truncate ON supplier_ratings;
                DROP FUNCTION IF EXISTS htn_suppliers_guard();
                DROP FUNCTION IF EXISTS htn_supplier_documents_guard();
                DROP FUNCTION IF EXISTS htn_event_suppliers_guard();
                DROP FUNCTION IF EXISTS htn_event_suppliers_transitions();
                DROP FUNCTION IF EXISTS htn_supplier_ratings_guard();
                """);
        }
    }
}
