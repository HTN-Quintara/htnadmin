using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Htn.Infrastructure.Data.Migrations
{
    /// <summary>
    /// Phase 8B: the database half of "membership is fully immutable once written" — mirrors
    /// <c>merch_sales_guard</c>/<c>merch_sale_lines_guard</c> exactly: an unconditional reject-all on
    /// <c>UPDATE</c>/<c>DELETE</c>, never a column-by-column freeze comparison, because there is no update path at
    /// all (a renewal is a brand new row for the next membership year). <c>htn_reject_change()</c> comes from the
    /// initial migration.
    /// </summary>
    public partial class Membership_Guards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION htn_memberships_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'memberships: append-only, never updated or deleted'
                        USING ERRCODE = 'insufficient_privilege';
                END;
                $$;
                DROP TRIGGER IF EXISTS memberships_guard ON memberships;
                CREATE TRIGGER memberships_guard BEFORE UPDATE OR DELETE ON memberships
                    FOR EACH ROW EXECUTE FUNCTION htn_memberships_guard();
                DROP TRIGGER IF EXISTS memberships_no_truncate ON memberships;
                CREATE TRIGGER memberships_no_truncate BEFORE TRUNCATE ON memberships
                    FOR EACH STATEMENT EXECUTE FUNCTION htn_reject_change();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS memberships_guard ON memberships;
                DROP TRIGGER IF EXISTS memberships_no_truncate ON memberships;
                DROP FUNCTION IF EXISTS htn_memberships_guard();
                """);
        }
    }
}
