using Htn.Api.Tests.Infrastructure;
using Htn.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using static Htn.Api.Tests.Suppliers.SuppliersTestData;

namespace Htn.Api.Tests.Suppliers;

/// <summary>
/// The database half of the suppliers rules, exercised as if the API had been bypassed entirely — a hand-written
/// <c>UPDATE</c>/<c>DELETE</c> at a psql prompt. Mirrors <c>InvitationsIntegrityGuardsTests</c>/<c>DirectoryIntegrityGuardsTests</c>.
/// </summary>
public sealed class SuppliersIntegrityGuardsTests(HtnApiFactory factory) : IClassFixture<HtnApiFactory>
{
    private Task<int> SqlAsync(FormattableString sql) => factory.DbAsync(db => db.Database.ExecuteSqlInterpolatedAsync(sql));

    private async Task AssertRejectedAsync(FormattableString sql, string? message = null)
    {
        var ex = await Assert.ThrowsAnyAsync<Exception>(() => SqlAsync(sql));
        if (message is not null) Assert.Contains(message, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_suppliers_category_and_name_key_are_frozen_at_the_database_too()
    {
        var s = await SceneAsync(factory);
        var supplierId = await CreateSupplierAsync(s.Board);
        await AssertRejectedAsync($"UPDATE suppliers SET category = 'Catering' WHERE id = {supplierId}", "frozen");
        await AssertRejectedAsync($"UPDATE suppliers SET name_key = 'changed' WHERE id = {supplierId}", "frozen");
    }

    [Fact]
    public async Task A_supplier_can_never_be_hard_deleted_at_the_database()
    {
        var s = await SceneAsync(factory);
        var supplierId = await CreateSupplierAsync(s.Board);
        await AssertRejectedAsync($"DELETE FROM suppliers WHERE id = {supplierId}", "never deleted");
    }

    [Fact]
    public async Task A_hire_past_quotation_can_never_be_hard_deleted_at_the_database()
    {
        var s = await SceneAsync(factory);
        var supplierId = await CreateSupplierAsync(s.Board);
        var hireId = await HireAsync(s.Board, s.EventId, supplierId, s.CommitteeId);
        await PatchHireAsync(s.Board, s.EventId, hireId, new { status = "Booked" });
        await AssertRejectedAsync($"DELETE FROM event_suppliers WHERE id = {hireId}", "never deleted");
    }

    [Fact]
    public async Task A_quotation_hire_can_be_hard_deleted_directly_at_the_database()
    {
        var s = await SceneAsync(factory);
        var supplierId = await CreateSupplierAsync(s.Board);
        var hireId = await HireAsync(s.Board, s.EventId, supplierId, s.CommitteeId);
        await SqlAsync($"DELETE FROM event_suppliers WHERE id = {hireId}");
        var remaining = await factory.DbAsync(db => db.EventSuppliers.AsNoTracking().CountAsync(h => h.Id == hireId));
        Assert.Equal(0, remaining);
    }

    [Fact]
    public async Task The_hire_status_whitelist_is_enforced_at_the_database_too()
    {
        var s = await SceneAsync(factory);
        var supplierId = await CreateSupplierAsync(s.Board);
        var hireId = await HireAsync(s.Board, s.EventId, supplierId, s.CommitteeId);
        await AssertRejectedAsync($"UPDATE event_suppliers SET status = 'DepositPaid' WHERE id = {hireId}", "cannot move");
        await SqlAsync($"UPDATE event_suppliers SET status = 'Booked' WHERE id = {hireId}");
        await AssertRejectedAsync($"UPDATE event_suppliers SET status = 'Quotation' WHERE id = {hireId}", "cannot move");
    }

    [Fact]
    public async Task A_hires_owning_committee_is_frozen_at_the_database_too()
    {
        var s = await SceneAsync(factory);
        var supplierId = await CreateSupplierAsync(s.Board);
        var hireId = await HireAsync(s.Board, s.EventId, supplierId, s.CommitteeId);
        await AssertRejectedAsync($"UPDATE event_suppliers SET owner_committee_id = {s.OtherCommitteeId} WHERE id = {hireId}", "frozen");
    }

    [Fact]
    public async Task A_supplier_rating_is_never_updated_or_deleted_at_the_database()
    {
        var s = await SceneAsync(factory);
        var supplierId = await CreateSupplierAsync(s.Board);
        var hireId = await HireAsync(s.Board, s.EventId, supplierId, s.CommitteeId);
        await EndEventAsync(factory, s.EventId);
        var rated = await RateAsync(s.CommitteeLead, s.EventId, hireId);
        rated.EnsureSuccessStatusCode();
        var ratingId = await factory.DbAsync(db => db.SupplierRatings.AsNoTracking()
            .Where(r => r.EventSupplierId == hireId).Select(r => r.Id).FirstAsync());

        await AssertRejectedAsync($"UPDATE supplier_ratings SET stars = 1 WHERE id = {ratingId}", "append-only");
        await AssertRejectedAsync($"DELETE FROM supplier_ratings WHERE id = {ratingId}", "append-only");
    }
}
