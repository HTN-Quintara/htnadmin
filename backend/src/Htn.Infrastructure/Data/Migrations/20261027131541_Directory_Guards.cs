using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Htn.Infrastructure.Data.Migrations
{
    /// <summary>
    /// Phase 7A, the database half of the directory rules, mirroring <c>Sponsors_Guards</c>.
    /// <list type="bullet">
    /// <item><c>directory_orgs_guard</c>: no deletes; name, name_key and kind are frozen once set; once deactivated
    /// the row is final.</item>
    /// <item><c>directory_contacts_guard</c>: no deletes; a contact keeps its organization; once voided the row is
    /// final.</item>
    /// <item><c>directory_contact_history_guard</c>: fully append-only — no updates, no deletes, ever.</item>
    /// <item><c>directory_contacts_holder_check</c>: a deferred constraint trigger (like <c>session_room_overlap</c>
    /// in <c>Agenda_Guards</c>) that, at commit, rejects any change to a contact's <c>holder_name</c> or
    /// <c>position</c> that isn't matched by a <c>HolderChanged</c> history row snapshotting the prior values —
    /// deferred so the contact update and its history insert, issued in the same transaction, both land before the
    /// check runs.</item>
    /// </list>
    /// Functions use <c>CREATE OR REPLACE</c> and every trigger is dropped first, so a half-rolled-back <c>Up</c> can
    /// be re-run. <c>htn_reject_change()</c> comes from the initial migration.
    /// </summary>
    public partial class Directory_Guards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION htn_directory_orgs_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP = 'DELETE' THEN
                        RAISE EXCEPTION 'directory_orgs: an organization is deactivated with a reason, never deleted'
                            USING ERRCODE = 'insufficient_privilege';
                    END IF;
                    IF ROW(NEW.name, NEW.name_key, NEW.kind, NEW.created_at)
                       IS DISTINCT FROM ROW(OLD.name, OLD.name_key, OLD.kind, OLD.created_at) THEN
                        RAISE EXCEPTION 'directory_orgs: name, name_key and kind are frozen once set'
                            USING ERRCODE = 'insufficient_privilege';
                    END IF;
                    IF OLD.deactivated_at IS NOT NULL THEN
                        RAISE EXCEPTION 'directory_orgs: a deactivated organization is final' USING ERRCODE = 'check_violation';
                    END IF;
                    RETURN NEW;
                END;
                $$;
                DROP TRIGGER IF EXISTS directory_orgs_guard ON directory_orgs;
                CREATE TRIGGER directory_orgs_guard BEFORE UPDATE OR DELETE ON directory_orgs
                    FOR EACH ROW EXECUTE FUNCTION htn_directory_orgs_guard();
                DROP TRIGGER IF EXISTS directory_orgs_no_truncate ON directory_orgs;
                CREATE TRIGGER directory_orgs_no_truncate BEFORE TRUNCATE ON directory_orgs
                    FOR EACH STATEMENT EXECUTE FUNCTION htn_reject_change();

                CREATE OR REPLACE FUNCTION htn_directory_contacts_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP = 'DELETE' THEN
                        RAISE EXCEPTION 'directory_contacts: a contact is voided with a reason, never deleted'
                            USING ERRCODE = 'insufficient_privilege';
                    END IF;
                    IF ROW(NEW.org_id, NEW.created_at) IS DISTINCT FROM ROW(OLD.org_id, OLD.created_at) THEN
                        RAISE EXCEPTION 'directory_contacts: a contact keeps its organization'
                            USING ERRCODE = 'insufficient_privilege';
                    END IF;
                    IF OLD.voided_at IS NOT NULL THEN
                        RAISE EXCEPTION 'directory_contacts: a removed contact is final' USING ERRCODE = 'check_violation';
                    END IF;
                    RETURN NEW;
                END;
                $$;
                DROP TRIGGER IF EXISTS directory_contacts_guard ON directory_contacts;
                CREATE TRIGGER directory_contacts_guard BEFORE UPDATE OR DELETE ON directory_contacts
                    FOR EACH ROW EXECUTE FUNCTION htn_directory_contacts_guard();
                DROP TRIGGER IF EXISTS directory_contacts_no_truncate ON directory_contacts;
                CREATE TRIGGER directory_contacts_no_truncate BEFORE TRUNCATE ON directory_contacts
                    FOR EACH STATEMENT EXECUTE FUNCTION htn_reject_change();

                CREATE OR REPLACE FUNCTION htn_directory_contact_history_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'directory_contact_history: append-only, never updated or deleted'
                        USING ERRCODE = 'insufficient_privilege';
                END;
                $$;
                DROP TRIGGER IF EXISTS directory_contact_history_guard ON directory_contact_history;
                CREATE TRIGGER directory_contact_history_guard BEFORE UPDATE OR DELETE ON directory_contact_history
                    FOR EACH ROW EXECUTE FUNCTION htn_directory_contact_history_guard();
                DROP TRIGGER IF EXISTS directory_contact_history_no_truncate ON directory_contact_history;
                CREATE TRIGGER directory_contact_history_no_truncate BEFORE TRUNCATE ON directory_contact_history
                    FOR EACH STATEMENT EXECUTE FUNCTION htn_reject_change();

                CREATE OR REPLACE FUNCTION htn_directory_contacts_holder_check() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF NEW.holder_name IS DISTINCT FROM OLD.holder_name OR NEW.position IS DISTINCT FROM OLD.position THEN
                        IF NOT EXISTS (
                            SELECT 1 FROM directory_contact_history h
                            WHERE h.contact_id = NEW.id AND h.kind = 'HolderChanged'
                              AND h.prior_position = OLD.position AND h.prior_holder_name = OLD.holder_name
                        ) THEN
                            RAISE EXCEPTION 'directory_contacts: a holder or position change needs a matching HolderChanged history row'
                                USING ERRCODE = 'check_violation';
                        END IF;
                    END IF;
                    RETURN NULL;
                END;
                $$;
                DROP TRIGGER IF EXISTS directory_contacts_holder_check ON directory_contacts;
                CREATE CONSTRAINT TRIGGER directory_contacts_holder_check AFTER UPDATE ON directory_contacts
                    DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION htn_directory_contacts_holder_check();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS directory_orgs_guard ON directory_orgs;
                DROP TRIGGER IF EXISTS directory_orgs_no_truncate ON directory_orgs;
                DROP TRIGGER IF EXISTS directory_contacts_guard ON directory_contacts;
                DROP TRIGGER IF EXISTS directory_contacts_no_truncate ON directory_contacts;
                DROP TRIGGER IF EXISTS directory_contact_history_guard ON directory_contact_history;
                DROP TRIGGER IF EXISTS directory_contact_history_no_truncate ON directory_contact_history;
                DROP TRIGGER IF EXISTS directory_contacts_holder_check ON directory_contacts;
                DROP FUNCTION IF EXISTS htn_directory_orgs_guard();
                DROP FUNCTION IF EXISTS htn_directory_contacts_guard();
                DROP FUNCTION IF EXISTS htn_directory_contact_history_guard();
                DROP FUNCTION IF EXISTS htn_directory_contacts_holder_check();
                """);
        }
    }
}
