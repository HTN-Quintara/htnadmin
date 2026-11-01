using Htn.Api.Tests.Infrastructure;
using Htn.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using static Htn.Api.Tests.OrgDirectory.DirectoryTestData;

namespace Htn.Api.Tests.OrgDirectory;

/// <summary>
/// The database half of the directory rules, exercised as if the API had been bypassed entirely — a hand-written
/// <c>UPDATE</c>/<c>DELETE</c> at a psql prompt. Mirrors <c>FinanceIntegrityGuardsTests</c>.
/// </summary>
public sealed class DirectoryIntegrityGuardsTests(HtnApiFactory factory) : IClassFixture<HtnApiFactory>
{
    private Task<int> SqlAsync(FormattableString sql) => factory.DbAsync(db => db.Database.ExecuteSqlInterpolatedAsync(sql));

    private async Task AssertRejectedAsync(FormattableString sql, string? message = null)
    {
        var ex = await Assert.ThrowsAnyAsync<Exception>(() => SqlAsync(sql));
        if (message is not null) Assert.Contains(message, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_directory_contact_history_row_is_never_updated_or_deleted()
    {
        var s = await SceneAsync(factory);
        var orgId = await CreateOrgAsync(s.Board);
        var contactId = await CreateContactAsync(s.Board, orgId);
        await s.Board.PostAsync($"/api/directory/{orgId}/contacts/{contactId}/still-correct");

        var historyId = await factory.DbAsync(db => db.DirectoryContactHistory.AsNoTracking()
            .Where(h => h.ContactId == contactId).Select(h => h.Id).SingleAsync());

        await AssertRejectedAsync($"UPDATE directory_contact_history SET note = 'x' WHERE id = {historyId}", "append-only");
        await AssertRejectedAsync($"DELETE FROM directory_contact_history WHERE id = {historyId}", "append-only");
    }

    [Fact]
    public async Task An_organizations_name_and_kind_are_frozen()
    {
        var s = await SceneAsync(factory);
        var orgId = await CreateOrgAsync(s.Board, NewOrg("Frozen Name Co", "Company"));

        await AssertRejectedAsync($"UPDATE directory_orgs SET name = 'Changed' WHERE id = {orgId}", "frozen");
        await AssertRejectedAsync($"UPDATE directory_orgs SET kind = 'Government' WHERE id = {orgId}", "frozen");
        await AssertRejectedAsync($"DELETE FROM directory_orgs WHERE id = {orgId}", "never deleted");
    }

    [Fact]
    public async Task A_holder_or_position_change_needs_a_matching_history_row()
    {
        var s = await SceneAsync(factory);
        var orgId = await CreateOrgAsync(s.Board);
        var contactId = await CreateContactAsync(s.Board, orgId, NewContact(position: "President", holder: "Alice Cruz"));

        await AssertRejectedAsync(
            $"UPDATE directory_contacts SET holder_name = 'Someone Else' WHERE id = {contactId}",
            "HolderChanged history row");
    }

    [Fact]
    public async Task A_voided_contact_is_final()
    {
        var s = await SceneAsync(factory);
        var orgId = await CreateOrgAsync(s.Board);
        var contactId = await CreateContactAsync(s.Board, orgId);

        var voidedById = Guid.Empty;
        await SqlAsync($"UPDATE directory_contacts SET voided_at = now(), voided_by_id = {voidedById}, void_reason = 'test' WHERE id = {contactId}");
        await AssertRejectedAsync($"UPDATE directory_contacts SET phone = '0912' WHERE id = {contactId}", "removed contact is final");
        await AssertRejectedAsync($"DELETE FROM directory_contacts WHERE id = {contactId}", "never deleted");
    }
}
