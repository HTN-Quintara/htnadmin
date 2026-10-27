using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Htn.Infrastructure.Data.Migrations
{
    /// <summary>
    /// Phase 7A: links sponsors, leads and event partners to the directory organization with the same
    /// <c>name_key</c>, wherever one hasn't been linked yet. On a fresh install <c>directory_orgs</c> is empty, so
    /// this runs as a structurally correct no-op; once organizations are added by hand (or via the API) it is safe
    /// to run again — each <c>UPDATE</c> only ever touches rows that are still unlinked, so a repeat run (or this
    /// migration replayed) changes nothing that was already matched.
    /// <para>
    /// The same join is also exposed as <see cref="Htn.Infrastructure.OrgDirectory.DirectoryOps.BackfillAsync"/>, a
    /// reusable, testable method an admin action can call later to re-run the match after more organizations are
    /// added — a C# migration cannot resolve the scoped <c>HtnDbContext</c> it needs, so the two are kept in step by
    /// hand rather than one calling the other.
    /// </para>
    /// </summary>
    public partial class DirectoryOrgBackfill : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE sponsors t SET directory_org_id = o.id
                FROM directory_orgs o
                WHERE t.name_key = o.name_key AND o.deactivated_at IS NULL
                  AND t.name_key IS NOT NULL AND t.directory_org_id IS NULL;

                UPDATE leads t SET directory_org_id = o.id
                FROM directory_orgs o
                WHERE t.name_key = o.name_key AND o.deactivated_at IS NULL
                  AND t.name_key IS NOT NULL AND t.directory_org_id IS NULL;

                UPDATE event_partners t SET directory_org_id = o.id
                FROM directory_orgs o
                WHERE t.name_key = o.name_key AND o.deactivated_at IS NULL
                  AND t.name_key IS NOT NULL AND t.directory_org_id IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Nothing to undo structurally: this migration only ever fills a nullable FK that the Directory
            // migration's Down() already drops. Leaving the links in place on a partial rollback is harmless.
        }
    }
}
