using System.Net;
using System.Net.Http.Json;
using Htn.Api.Tests.Infrastructure;
using Htn.Domain.Finance;
using Microsoft.EntityFrameworkCore;
using static Htn.Api.Tests.Merch.MerchTestData;

namespace Htn.Api.Tests.Merch;

/// <summary>The denial matrix: anonymous 401, a goon/apprentice with no committee lead role 403 on both policies,
/// a committee lead of this event can Sell but not Manage, the board can both.</summary>
public sealed class MerchAuthorizationTests(HtnApiFactory factory) : IClassFixture<HtnApiFactory>
{
    [Fact]
    public async Task Anonymous_is_unauthorized_on_every_route()
    {
        var s = await SceneAsync(factory);
        var itemId = await CreateItemAsync(s.Board, s.EventId);
        var anon = factory.NewClient();
        (HttpMethod Method, string Url)[] routes =
        [
            (HttpMethod.Get, ItemsUrl(s.EventId)),
            (HttpMethod.Post, ItemsUrl(s.EventId)),
            (HttpMethod.Get, DiscountsUrl(s.EventId)),
            (HttpMethod.Get, DashboardUrl(s.EventId)),
            (HttpMethod.Get, SalesExportUrl(s.EventId)),
            (HttpMethod.Get, BuyerSearchUrl(s.EventId, "ana")),
            (HttpMethod.Post, ScanUrl(s.EventId)),
            (HttpMethod.Post, SaleUrl(s.EventId)),
        ];
        foreach (var (method, url) in routes)
        {
            var res = await anon.SendAsync(method, url, null, csrf: true);
            Assert.True(HttpStatusCode.Unauthorized == res.StatusCode, $"{method} {url} => {res.StatusCode}");
        }
    }

    [Fact]
    public async Task A_goon_who_leads_nothing_is_denied_manage_and_sell()
    {
        var s = await SceneAsync(factory);
        var itemId = await CreateItemAsync(s.Board, s.EventId);
        var variantId = await CreateVariantAsync(s.Board, s.EventId, itemId);

        Assert.Equal(HttpStatusCode.Forbidden, (await s.Goon.GetAsync(ItemsUrl(s.EventId))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await s.Goon.PostAsync(ItemsUrl(s.EventId), NewItem())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await s.Goon.GetAsync(DiscountsUrl(s.EventId))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await s.Goon.GetAsync(DashboardUrl(s.EventId))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await s.Goon.GetAsync(SalesExportUrl(s.EventId))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await s.Goon.GetAsync(BuyerSearchUrl(s.EventId, "ana"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SellAsync(s.Goon, s.EventId, variantId)).StatusCode);
    }

    [Fact]
    public async Task An_apprentice_is_denied_manage_and_sell()
    {
        var s = await SceneAsync(factory);
        var itemId = await CreateItemAsync(s.Board, s.EventId);
        var variantId = await CreateVariantAsync(s.Board, s.EventId, itemId);
        Assert.Equal(HttpStatusCode.Forbidden, (await s.Apprentice.GetAsync(ItemsUrl(s.EventId))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SellAsync(s.Apprentice, s.EventId, variantId)).StatusCode);
    }

    [Fact]
    public async Task A_committee_lead_can_sell_but_not_manage()
    {
        var s = await SceneAsync(factory);
        var itemId = await CreateItemAsync(s.Board, s.EventId);
        var variantId = await CreateVariantAsync(s.Board, s.EventId, itemId);

        // Can sell / read the dashboard.
        Assert.Equal(HttpStatusCode.OK, (await SellAsync(s.CommitteeLead, s.EventId, variantId)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await s.CommitteeLead.GetAsync(DashboardUrl(s.EventId))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await s.CommitteeLead.GetAsync(BuyerSearchUrl(s.EventId, "ana"))).StatusCode);

        // Cannot manage the catalog, discounts or export.
        Assert.Equal(HttpStatusCode.Forbidden, (await s.CommitteeLead.PostAsync(ItemsUrl(s.EventId), NewItem())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await s.CommitteeLead.GetAsync(ItemsUrl(s.EventId))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await s.CommitteeLead.GetAsync(DiscountsUrl(s.EventId))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await s.CommitteeLead.GetAsync(SalesExportUrl(s.EventId))).StatusCode);
    }

    [Fact]
    public async Task The_board_can_manage_and_sell()
    {
        var s = await SceneAsync(factory);
        var itemId = await CreateItemAsync(s.Board, s.EventId);
        var variantId = await CreateVariantAsync(s.Board, s.EventId, itemId);
        Assert.Equal(HttpStatusCode.OK, (await s.Board.GetAsync(ItemsUrl(s.EventId))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await s.Board.GetAsync(DiscountsUrl(s.EventId))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await s.Board.GetAsync(SalesExportUrl(s.EventId))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SellAsync(s.Board, s.EventId, variantId)).StatusCode);
    }

    [Fact]
    public async Task A_lead_of_a_different_event_is_denied_sell_on_this_one()
    {
        var s = await SceneAsync(factory);
        var itemId = await CreateItemAsync(s.Board, s.EventId);
        var variantId = await CreateVariantAsync(s.Board, s.EventId, itemId);

        // A second event whose lead is not s.CommitteeLead.
        var other = await SceneAsync(factory);
        Assert.Equal(HttpStatusCode.Forbidden, (await SellAsync(other.CommitteeLead, s.EventId, variantId)).StatusCode);
    }
}

public sealed class MerchItemAndVariantTests(HtnApiFactory factory) : IClassFixture<HtnApiFactory>
{
    [Fact]
    public async Task Creating_an_item_and_a_variant_round_trips()
    {
        var s = await SceneAsync(factory);
        var itemId = await CreateItemAsync(s.Board, s.EventId, NewItem("Con Shirt", 350m, "#B71C1C"));
        var variantId = await CreateVariantAsync(s.Board, s.EventId, itemId, "M", 10);

        var list = await ApiClient.JsonAsync(await s.Board.GetAsync(ItemsUrl(s.EventId)));
        var row = list.EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == itemId);
        Assert.Equal("Con Shirt", row.GetProperty("name").GetString());
        Assert.Equal(350m, row.GetProperty("price").GetDecimal());
        var variantRow = row.GetProperty("variants").EnumerateArray().Single(v => v.GetProperty("id").GetGuid() == variantId);
        Assert.Equal(10, variantRow.GetProperty("stockOnHand").GetInt32());
    }

    [Fact]
    public async Task Updating_an_item_changes_price_but_future_sales_use_the_new_snapshot()
    {
        var s = await SceneAsync(factory);
        var itemId = await CreateItemAsync(s.Board, s.EventId, NewItem("Shirt", 100m));
        var variantId = await CreateVariantAsync(s.Board, s.EventId, itemId);

        var patch = await s.Board.SendAsync(HttpMethod.Patch, $"{ItemsUrl(s.EventId)}/{itemId}", NewItem("Shirt v2", 200m), true);
        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);
        Assert.Equal(200m, (await ApiClient.JsonAsync(patch)).GetProperty("price").GetDecimal());

        var sale = await SellAsync(s.Board, s.EventId, variantId, qty: 1);
        var body = await ApiClient.JsonAsync(sale);
        Assert.Equal(200m, body.GetProperty("lines")[0].GetProperty("unitPriceSnapshot").GetDecimal());
    }

    [Fact]
    public async Task Deactivating_an_item_twice_is_refused_with_409()
    {
        var s = await SceneAsync(factory);
        var itemId = await CreateItemAsync(s.Board, s.EventId);
        await s.Board.PostAsync($"{ItemsUrl(s.EventId)}/{itemId}/deactivate", new { reason = "discontinued" });
        var again = await s.Board.PostAsync($"{ItemsUrl(s.EventId)}/{itemId}/deactivate", new { reason = "again" });
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("already_deactivated", (await ApiClient.JsonAsync(again)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Adjusting_stock_sets_the_counted_value()
    {
        var s = await SceneAsync(factory);
        var itemId = await CreateItemAsync(s.Board, s.EventId);
        var variantId = await CreateVariantAsync(s.Board, s.EventId, itemId, stock: 10);

        var patch = await s.Board.SendAsync(HttpMethod.Patch, $"{ItemsUrl(s.EventId)}/{itemId}/variants/{variantId}", new { stockOnHand = 3 }, true);
        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);
        Assert.Equal(3, (await ApiClient.JsonAsync(patch)).GetProperty("stockOnHand").GetInt32());
    }

    [Fact]
    public async Task A_duplicate_active_label_on_the_same_item_is_refused_with_409()
    {
        var s = await SceneAsync(factory);
        var itemId = await CreateItemAsync(s.Board, s.EventId);
        await CreateVariantAsync(s.Board, s.EventId, itemId, "M");
        var again = await s.Board.PostAsync($"{ItemsUrl(s.EventId)}/{itemId}/variants", new { label = "M", stockOnHand = 5 });
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("duplicate_label", (await ApiClient.JsonAsync(again)).GetProperty("code").GetString());
    }
}

public sealed class MerchDiscountSettingsTests(HtnApiFactory factory) : IClassFixture<HtnApiFactory>
{
    [Fact]
    public async Task Discounts_are_lazily_created_with_org_defaults_then_persist()
    {
        var s = await SceneAsync(factory);
        var first = await ApiClient.JsonAsync(await s.Board.GetAsync(DiscountsUrl(s.EventId)));
        Assert.Equal(20, first.GetProperty("goonPct").GetInt32());
        Assert.Equal(15, first.GetProperty("apprenticePct").GetInt32());
        Assert.Equal(10, first.GetProperty("memberPct").GetInt32());
        Assert.Equal(0, first.GetProperty("boardPct").GetInt32());

        var rowCount = await factory.DbAsync(db => db.EventMerchSettings.CountAsync(x => x.EventId == s.EventId));
        Assert.Equal(1, rowCount);

        var put = await s.Board.PutAsync(DiscountsUrl(s.EventId), new { goonPct = 25, apprenticePct = 18, memberPct = 12, boardPct = 5 });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);

        var after = await ApiClient.JsonAsync(await s.Board.GetAsync(DiscountsUrl(s.EventId)));
        Assert.Equal(25, after.GetProperty("goonPct").GetInt32());

        var rowCountAfter = await factory.DbAsync(db => db.EventMerchSettings.CountAsync(x => x.EventId == s.EventId));
        Assert.Equal(1, rowCountAfter);
    }

    [Fact]
    public async Task Updating_with_an_out_of_range_percent_is_refused()
    {
        var s = await SceneAsync(factory);
        var put = await s.Board.PutAsync(DiscountsUrl(s.EventId), new { goonPct = 101, apprenticePct = 15, memberPct = 10, boardPct = 0 });
        Assert.Equal(HttpStatusCode.BadRequest, put.StatusCode);
    }
}

public sealed class MerchSaleTests(HtnApiFactory factory) : IClassFixture<HtnApiFactory>
{
    [Fact]
    public async Task A_sale_decrements_stock_posts_one_ledger_entry_and_writes_an_audit_entry()
    {
        var s = await SceneAsync(factory);
        var itemId = await CreateItemAsync(s.Board, s.EventId, NewItem("Shirt", 300m));
        var variantId = await CreateVariantAsync(s.Board, s.EventId, itemId, stock: 5);

        var res = await SellAsync(s.Board, s.EventId, variantId, qty: 2);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await ApiClient.JsonAsync(res);
        Assert.Equal(600m, body.GetProperty("subtotal").GetDecimal());
        Assert.Equal(600m, body.GetProperty("total").GetDecimal());
        var saleId = body.GetProperty("id").GetGuid();

        var stock = await factory.DbAsync(db => db.MerchVariants.AsNoTracking().Where(v => v.Id == variantId).Select(v => v.StockOnHand).SingleAsync());
        Assert.Equal(3, stock);

        var entries = await factory.DbAsync(db => db.LedgerEntries.AsNoTracking()
            .Where(e => e.Source == LedgerSource.MerchSale && e.SourceId == saleId).ToListAsync());
        Assert.Single(entries);
        Assert.Equal(600m, entries[0].Amount);

        var audited = await factory.DbAsync(db => db.AuditEntries.AsNoTracking()
            .AnyAsync(a => a.Action == "merch_sale.create" && a.EntityId == saleId.ToString()));
        Assert.True(audited);
    }

    [Fact]
    public async Task Reselling_does_not_double_post_the_ledger()
    {
        var s = await SceneAsync(factory);
        var itemId = await CreateItemAsync(s.Board, s.EventId, NewItem("Pin", 50m));
        var variantId = await CreateVariantAsync(s.Board, s.EventId, itemId, stock: 20);

        await SellAsync(s.Board, s.EventId, variantId, qty: 1);
        await SellAsync(s.Board, s.EventId, variantId, qty: 1);

        var entryCount = await factory.DbAsync(db => db.LedgerEntries.AsNoTracking().CountAsync(e => e.Source == LedgerSource.MerchSale));
        Assert.Equal(2, entryCount);
    }

    [Fact]
    public async Task Selling_more_than_in_stock_is_refused_with_409_and_stock_is_unchanged()
    {
        var s = await SceneAsync(factory);
        var itemId = await CreateItemAsync(s.Board, s.EventId);
        var variantId = await CreateVariantAsync(s.Board, s.EventId, itemId, stock: 2);

        var res = await SellAsync(s.Board, s.EventId, variantId, qty: 3);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("insufficient_stock", (await ApiClient.JsonAsync(res)).GetProperty("code").GetString());

        var stock = await factory.DbAsync(db => db.MerchVariants.AsNoTracking().Where(v => v.Id == variantId).Select(v => v.StockOnHand).SingleAsync());
        Assert.Equal(2, stock);
    }

    [Fact]
    public async Task A_sale_with_no_buyer_name_and_no_matched_buyer_is_refused()
    {
        var s = await SceneAsync(factory);
        var itemId = await CreateItemAsync(s.Board, s.EventId);
        var variantId = await CreateVariantAsync(s.Board, s.EventId, itemId);
        var res = await s.Board.PostAsync(SaleUrl(s.EventId), new
        {
            buyerPersonId = (Guid?)null, buyerAttendeeId = (Guid?)null, buyerName = "",
            lines = new[] { new { variantId, qty = 1 } }, method = "Cash",
        });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Two_concurrent_sales_for_the_last_unit_only_one_succeeds()
    {
        var s = await SceneAsync(factory);
        var itemId = await CreateItemAsync(s.Board, s.EventId);
        var variantId = await CreateVariantAsync(s.Board, s.EventId, itemId, stock: 1);

        // Pre-create this event's fund with an unrelated sale first, so the race below exercises only the stock
        // trigger, not the (separate, pre-existing) first-write-wins race on creating the event's Fund row.
        var warmUpVariant = await CreateVariantAsync(s.Board, s.EventId, itemId, "Warm-up", stock: 10);
        await SellAsync(s.Board, s.EventId, warmUpVariant, qty: 1);

        // Two genuinely different sale attempts (distinct ClientRequestIds), fired as real parallel HTTP
        // requests, racing for the last unit: this proves the stock_decrement trigger's row lock actually
        // serializes concurrent sales, not just that a sequential retry is rejected.
        var t1 = SellAsync(s.Board, s.EventId, variantId, qty: 1, clientRequestId: Guid.NewGuid());
        var t2 = SellAsync(s.CommitteeLead, s.EventId, variantId, qty: 1, clientRequestId: Guid.NewGuid());
        var results = await Task.WhenAll(t1, t2);

        var statuses = results.Select(r => r.StatusCode).OrderBy(c => c).ToList();
        Assert.Contains(HttpStatusCode.OK, statuses);
        Assert.Contains(HttpStatusCode.Conflict, statuses);
        Assert.Equal("insufficient_stock",
            (await ApiClient.JsonAsync(results.Single(r => r.StatusCode == HttpStatusCode.Conflict))).GetProperty("code").GetString());

        var stock = await factory.DbAsync(db => db.MerchVariants.AsNoTracking().Where(v => v.Id == variantId).Select(v => v.StockOnHand).SingleAsync());
        Assert.Equal(0, stock);
    }

    [Fact]
    public async Task A_double_submit_with_the_same_client_request_id_is_a_true_no_op()
    {
        var s = await SceneAsync(factory);
        var itemId = await CreateItemAsync(s.Board, s.EventId, NewItem("Lanyard", 75m));
        var variantId = await CreateVariantAsync(s.Board, s.EventId, itemId, stock: 10);
        var clientRequestId = Guid.NewGuid();

        var first = await SellAsync(s.Board, s.EventId, variantId, qty: 2, clientRequestId: clientRequestId);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var firstBody = await ApiClient.JsonAsync(first);
        var saleId = firstBody.GetProperty("id").GetGuid();

        // A network retry (or a double-tap) of the exact same attempt: same ClientRequestId, same everything.
        var retry = await SellAsync(s.Board, s.EventId, variantId, qty: 2, clientRequestId: clientRequestId);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        var retryBody = await ApiClient.JsonAsync(retry);

        Assert.Equal(saleId, retryBody.GetProperty("id").GetGuid());
        Assert.Equal(firstBody.GetProperty("total").GetDecimal(), retryBody.GetProperty("total").GetDecimal());

        var saleCount = await factory.DbAsync(db => db.MerchSales.AsNoTracking().CountAsync(x => x.EventId == s.EventId));
        Assert.Equal(1, saleCount);

        var stock = await factory.DbAsync(db => db.MerchVariants.AsNoTracking().Where(v => v.Id == variantId).Select(v => v.StockOnHand).SingleAsync());
        Assert.Equal(8, stock); // decremented exactly once, not twice

        var entryCount = await factory.DbAsync(db => db.LedgerEntries.AsNoTracking().CountAsync(e => e.Source == LedgerSource.MerchSale && e.SourceId == saleId));
        Assert.Equal(1, entryCount); // posted exactly once, not twice
    }
}

public sealed class MerchBuyerSearchTests(HtnApiFactory factory) : IClassFixture<HtnApiFactory>
{
    [Fact]
    public async Task Buyer_search_returns_people_by_name()
    {
        var s = await SceneAsync(factory);
        // CreateUserAsync names the test board account "Test Board" (first/last = level).
        var hits = await ApiClient.JsonAsync(await s.Board.GetAsync(BuyerSearchUrl(s.EventId, "Board")));
        Assert.Contains(hits.EnumerateArray(), h => h.GetProperty("kind").GetString() == "Person" && h.GetProperty("level").GetString() == "Board");
    }

    [Fact]
    public async Task A_short_query_returns_no_results()
    {
        var s = await SceneAsync(factory);
        var hits = await ApiClient.JsonAsync(await s.Board.GetAsync(BuyerSearchUrl(s.EventId, "ab")));
        Assert.Empty(hits.EnumerateArray());
    }
}
