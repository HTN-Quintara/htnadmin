using Htn.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

// Named OrgDirectory, not Directory: a sibling namespace literally named "Directory" under Htn.Infrastructure
// shadows System.IO.Directory for every other namespace nested under Htn.Infrastructure (e.g. Htn.Infrastructure.Storage's
// `Directory.CreateDirectory` call), so this avoids that collision project-wide.
namespace Htn.Infrastructure.OrgDirectory;

/// <summary>
/// The Phase 7 Directory backfill: fills <c>directory_org_id</c> on sponsors, leads and event partners whose
/// <c>name_key</c> matches a live <c>directory_orgs</c> row and that aren't linked yet. Idempotent and safe to
/// re-run — matched rows that are already linked are left alone, and a non-matching <c>name_key</c> stays null. The
/// <c>DirectoryOrgBackfill</c> migration calls this once from <c>Up()</c>; it is also exposed here so an admin action
/// can re-run it later, after new organizations are added to the directory.
/// </summary>
public static class DirectoryOps
{
    public static async Task<int> BackfillAsync(HtnDbContext db, CancellationToken ct = default)
    {
        var total = 0;

        // The WHERE clause only matches rows that are both unlinked AND have a live org with the same name_key, so
        // an unmatched row is never touched (and so never counted) on this or any later run: only genuine new links
        // contribute to the returned count, which is what makes "run it again" a meaningful no-op check.
        total += await db.Sponsors.Where(s => s.DirectoryOrgId == null
                && db.DirectoryOrgs.Any(o => o.DeactivatedAt == null && o.NameKey == s.NameKey))
            .ExecuteUpdateAsync(set => set.SetProperty(
                s => s.DirectoryOrgId,
                s => db.DirectoryOrgs.Where(o => o.DeactivatedAt == null && o.NameKey == s.NameKey)
                    .Select(o => (Guid?)o.Id).FirstOrDefault()), ct);

        total += await db.Leads.Where(l => l.DirectoryOrgId == null
                && db.DirectoryOrgs.Any(o => o.DeactivatedAt == null && o.NameKey == l.NameKey))
            .ExecuteUpdateAsync(set => set.SetProperty(
                l => l.DirectoryOrgId,
                l => db.DirectoryOrgs.Where(o => o.DeactivatedAt == null && o.NameKey == l.NameKey)
                    .Select(o => (Guid?)o.Id).FirstOrDefault()), ct);

        total += await db.EventPartners.Where(p => p.DirectoryOrgId == null
                && db.DirectoryOrgs.Any(o => o.DeactivatedAt == null && o.NameKey == p.NameKey))
            .ExecuteUpdateAsync(set => set.SetProperty(
                p => p.DirectoryOrgId,
                p => db.DirectoryOrgs.Where(o => o.DeactivatedAt == null && o.NameKey == p.NameKey)
                    .Select(o => (Guid?)o.Id).FirstOrDefault()), ct);

        return total;
    }
}
