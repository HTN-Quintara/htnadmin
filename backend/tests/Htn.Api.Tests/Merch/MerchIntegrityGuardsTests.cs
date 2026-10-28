using Htn.Api.Tests.Infrastructure;
using Htn.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using static Htn.Api.Tests.Merch.MerchTestData;

namespace Htn.Api.Tests.Merch;

/// <summary>
/// The database half of the merch rules, exercised as if the API had been bypassed entirely — mirrors
/// <c>SuppliersIntegrityGuardsTests</c>.
/// </summary>
public sealed class MerchIntegrityGuardsTests(HtnApiFactory factory) : IClassFixture<HtnApiFactory>
{
    private Task<int> SqlAsync(FormattableString sql) => factory.DbAsync(db => db.Database.ExecuteSqlInterpolatedAsync(sql));

    private async Task AssertRejectedAsync(FormattableString sql, string? message = null)
    {
        var ex = await Assert.ThrowsAnyAsync<Exception>(() => SqlAsync(sql));
        if (message is not null) Assert.Contains(message, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_merch_item_can_never_be_hard_deleted_at_the_database()
    {
        var s = await SceneAsync(factory);
        var itemId = await CreateItemAsync(s.Board, s.EventId);
        await AssertRejectedAsync($"DELETE FROM merch_items WHERE id = {itemId}", "never deleted");
    }

    [Fact]
    public async Task A_deactivated_item_is_final_at_the_database_too()
    {
        var s = await SceneAsync(factory);
        var itemId = await CreateItemAsync(s.Board, s.EventId);
        await s.Board.PostAsync($"{ItemsUrl(s.EventId)}/{itemId}/deactivate", new { reason = "discontinued" });
        await AssertRejectedAsync($"UPDATE merch_items SET name = 'New name' WHERE id = {itemId}", "final");
    }

    [Fact]
    public async Task A_merch_variant_can_never_be_hard_deleted_at_the_database()
    {
        var s = await SceneAsync(factory);
        var itemId = await CreateItemAsync(s.Board, s.EventId);
        var variantId = await CreateVariantAsync(s.Board, s.EventId, itemId);
        await AssertRejectedAsync($"DELETE FROM merch_variants WHERE id = {variantId}", "never deleted");
    }

    [Fact]
    public async Task A_sale_and_its_lines_are_never_updated_or_deleted_at_the_database()
    {
        var s = await SceneAsync(factory);
        var itemId = await CreateItemAsync(s.Board, s.EventId);
        var variantId = await CreateVariantAsync(s.Board, s.EventId, itemId);
        var res = await SellAsync(s.Board, s.EventId, variantId);
        var body = await ApiClient.JsonAsync(res);
        var saleId = body.GetProperty("id").GetGuid();
        var lineId = body.GetProperty("lines")[0].GetProperty("id").GetGuid();

        await AssertRejectedAsync($"UPDATE merch_sales SET buyer_name_snapshot = 'Someone else' WHERE id = {saleId}", "append-only");
        await AssertRejectedAsync($"DELETE FROM merch_sales WHERE id = {saleId}", "append-only");
        await AssertRejectedAsync($"UPDATE merch_sale_lines SET qty = 99 WHERE id = {lineId}", "append-only");
        await AssertRejectedAsync($"DELETE FROM merch_sale_lines WHERE id = {lineId}", "append-only");
    }

    [Fact]
    public async Task Inserting_a_sale_line_directly_still_decrements_stock_and_refuses_to_go_negative()
    {
        var s = await SceneAsync(factory);
        var itemId = await CreateItemAsync(s.Board, s.EventId);
        var variantId = await CreateVariantAsync(s.Board, s.EventId, itemId, stock: 1);

        var saleId = await factory.DbAsync(async db =>
        {
            var id = Guid.CreateVersion7();
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO merch_sales (id, event_id, client_request_id, buyer_name_snapshot, subtotal, discount_pct, discount_amount, total, method, cashier_id, sold_at)
                VALUES ({id}, {s.EventId}, {Guid.CreateVersion7()}, 'Direct insert', 0, 0, 0, 0, 'Cash', {id}, now())
                """);
            return id;
        });

        // The trigger's own UPDATE would take stock_on_hand negative, which the column's check constraint refuses
        // (ck_merch_variants_stock) before the trigger's own "insufficient stock" message is even reached — either
        // way, the write is rejected and the floor at zero holds.
        await AssertRejectedAsync($"""
            INSERT INTO merch_sale_lines (id, sale_id, item_id, variant_id, qty, unit_price_snapshot, line_total)
            VALUES ({Guid.CreateVersion7()}, {saleId}, {itemId}, {variantId}, 2, 100, 200)
            """);

        var stock = await factory.DbAsync(db => db.MerchVariants.AsNoTracking().Where(v => v.Id == variantId).Select(v => v.StockOnHand).SingleAsync());
        Assert.Equal(1, stock);
    }
}
