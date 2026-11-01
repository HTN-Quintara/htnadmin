using System.Net;
using System.Net.Http.Headers;
using Htn.Api.Tests.Infrastructure;
using Htn.Domain.Finance;
using Microsoft.EntityFrameworkCore;
using static Htn.Api.Tests.Finance.FinanceTestData;

namespace Htn.Api.Tests.Finance;

/// <summary>Adding, approving, voiding and reading entries, and the receipt every hand-entered one must carry.</summary>
public sealed class LedgerEndpointsTests(HtnApiFactory factory) : IClassFixture<HtnApiFactory>
{
    [Fact]
    public async Task An_entry_a_person_adds_needs_a_receipt_and_the_receipt_must_be_what_it_claims()
    {
        var s = await SceneAsync(factory);

        // No file at all.
        var none = await s.CfoClient.PostAsync("/api/finance/ledger", new { fundId = s.OrgFundId });
        Assert.Equal(HttpStatusCode.BadRequest, none.StatusCode);
        Assert.Equal("file_required", (await ApiClient.JsonAsync(none)).GetProperty("code").GetString());

        // A text file renamed to .png: the bytes are what count.
        var wrongType = await s.CfoClient.PostFormFileAsync("/api/finance/ledger", Fields(s), [1, 2, 3, 4, 5],
            "receipt.png", "image/png", field: "receipt");
        Assert.Equal(HttpStatusCode.BadRequest, wrongType.StatusCode);
        Assert.Equal("unsupported_type", (await ApiClient.JsonAsync(wrongType)).GetProperty("code").GetString());

        // Over the 5 MB ceiling.
        var tooBig = new byte[6 * 1024 * 1024];
        Png.CopyTo(tooBig, 0);
        var big = await s.CfoClient.PostFormFileAsync("/api/finance/ledger", Fields(s), tooBig,
            "receipt.png", "image/png", field: "receipt");
        Assert.Equal(HttpStatusCode.BadRequest, big.StatusCode);
        Assert.Equal("file_too_large", (await ApiClient.JsonAsync(big)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task The_amount_date_and_description_are_checked_the_same_way_as_a_json_form()
    {
        var s = await SceneAsync(factory);

        await AssertFieldAsync(s, f => f["amount"] = "0", "amount");
        await AssertFieldAsync(s, f => f["amount"] = "10.005", "amount");
        await AssertFieldAsync(s, f => f["amount"] = "not money", "amount");
        await AssertFieldAsync(s, f => f["description"] = "   ", "description");
        await AssertFieldAsync(s, f => f["entryDate"] = "1999-01-01", "entryDate");
        await AssertFieldAsync(s, f => f["direction"] = "sideways", "direction");
        await AssertFieldAsync(s, f => f["fundId"] = Guid.NewGuid().ToString(), "fundId");
        await AssertFieldAsync(s, f => f["categoryId"] = Guid.NewGuid().ToString(), "categoryId");
        await AssertFieldAsync(s, f => f["paymentAccountId"] = Guid.NewGuid().ToString(), "paymentAccountId");
    }

    [Fact]
    public async Task An_entry_records_what_it_is_and_shows_up_in_the_list_with_its_totals()
    {
        var s = await SceneAsync(factory);
        await AddEntryOkAsync(s.CfoClient, s.OrgFundId, s.AccountId, s.TicketsCategoryId, "2026-10-05", "In", "2500.00", "Ticket sales");
        await AddEntryOkAsync(s.CfoClient, s.OrgFundId, s.AccountId, s.TicketsCategoryId, "2026-10-06", "Out", "400.00", "Venue deposit");
        await AddEntryOkAsync(s.AssistantClient, s.OrgFundId, s.AccountId, s.TicketsCategoryId, "2026-10-07", "Out", "100.00", "Snacks");

        // Scoped to this scene's own account: the class shares one database and the Organization fund is global.
        var res = await s.CfoClient.GetAsync($"/api/finance/ledger?accountId={s.AccountId}");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var page = await ApiClient.JsonAsync(res);

        Assert.Equal(3, page.GetProperty("total").GetInt32());
        var totals = page.GetProperty("totals");
        // The pending entry is not money yet, so it counts in pendingCount and in neither sum.
        Assert.Equal(2500.00m, totals.GetProperty("moneyIn").GetDecimal());
        Assert.Equal(400.00m, totals.GetProperty("moneyOut").GetDecimal());
        Assert.Equal(2100.00m, totals.GetProperty("net").GetDecimal());
        Assert.Equal(1, totals.GetProperty("pendingCount").GetInt32());

        // Newest first, names attached, and the signed amount carries the direction.
        var first = page.GetProperty("items").EnumerateArray().First();
        Assert.Equal("Snacks", first.GetProperty("description").GetString());
        Assert.Equal("Organization", first.GetProperty("fundName").GetString());
        Assert.Equal("Ticket sales", first.GetProperty("categoryName").GetString());
        Assert.Equal(-100.00m, first.GetProperty("signedAmount").GetDecimal());
        Assert.True(first.GetProperty("hasReceipt").GetBoolean());
        Assert.NotNull(first.GetProperty("enteredBy").GetString());
    }

    [Fact]
    public async Task The_list_filters_by_status_direction_date_and_text()
    {
        var s = await SceneAsync(factory);
        await AddEntryOkAsync(s.CfoClient, s.OrgFundId, s.AccountId, s.TicketsCategoryId, "2026-10-05", "In", "2500.00", "Ticket sales");
        await AddEntryOkAsync(s.CfoClient, s.OrgFundId, s.AccountId, s.TicketsCategoryId, "2026-11-06", "Out", "400.00", "Venue deposit");
        await AddEntryOkAsync(s.AssistantClient, s.OrgFundId, s.AccountId, s.TicketsCategoryId, "2026-11-07", "Out", "100.00", "Snacks for the team");

        Assert.Equal(1, await CountAsync(s, "status=Pending"));
        Assert.Equal(2, await CountAsync(s, "status=Posted"));
        Assert.Equal(2, await CountAsync(s, "direction=Out"));
        Assert.Equal(1, await CountAsync(s, "from=2026-11-01&to=2026-11-06"));
        Assert.Equal(1, await CountAsync(s, "search=venue"));          // case-insensitive
        Assert.Equal(1, await CountAsync(s, "search=SNACKS"));
        Assert.Equal(0, await CountAsync(s, "search=nothing matches this"));
    }

    [Fact]
    public async Task Voiding_needs_a_reason_takes_the_money_back_out_and_happens_once()
    {
        var s = await SceneAsync(factory);
        var id = await AddEntryOkAsync(s.CfoClient, s.OrgFundId, s.AccountId, s.TicketsCategoryId, "2026-10-05", "In", "2500.00");

        Assert.Equal(HttpStatusCode.BadRequest,
            (await s.CfoClient.PostAsync($"/api/finance/ledger/{id}/void", new { reason = "" })).StatusCode);

        var res = await s.CfoClient.PostAsync($"/api/finance/ledger/{id}/void", new { reason = "Wrong amount" });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var entry = await ApiClient.JsonAsync(res);
        Assert.Equal("Voided", entry.GetProperty("status").GetString());
        Assert.Equal("Wrong amount", entry.GetProperty("voidReason").GetString());
        Assert.NotNull(entry.GetProperty("voidedBy").GetString());
        // It was approved before it was voided, and that stays in the record.
        Assert.NotNull(entry.GetProperty("approvedAt").GetString());

        // The balance no longer counts it.
        var totals = (await ApiClient.JsonAsync(
            await s.CfoClient.GetAsync($"/api/finance/ledger?accountId={s.AccountId}"))).GetProperty("totals");
        Assert.Equal(0m, totals.GetProperty("moneyIn").GetDecimal());

        var again = await s.CfoClient.PostAsync($"/api/finance/ledger/{id}/void", new { reason = "Again" });
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("entry_voided", (await ApiClient.JsonAsync(again)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_voided_pending_entry_was_never_approved()
    {
        var s = await SceneAsync(factory);
        var id = await AddEntryOkAsync(s.AssistantClient, s.OrgFundId, s.AccountId, s.TicketsCategoryId, "2026-10-05", "Out", "100.00");

        var res = await s.CfoClient.PostAsync($"/api/finance/ledger/{id}/void", new { reason = "Duplicate of the other one" });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var entry = await ApiClient.JsonAsync(res);
        Assert.Equal("Voided", entry.GetProperty("status").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, entry.GetProperty("approvedAt").ValueKind);

        Assert.Equal(HttpStatusCode.Conflict, (await s.CfoClient.PostAsync($"/api/finance/ledger/{id}/approve")).StatusCode);
    }

    [Fact]
    public async Task The_receipt_is_only_ever_handed_over_as_a_download()
    {
        var s = await SceneAsync(factory);
        var id = await AddEntryOkAsync(s.CfoClient, s.OrgFundId, s.AccountId, s.TicketsCategoryId, "2026-10-05", "Out", "400.00");

        var res = await s.CfoClient.GetAsync($"/api/finance/ledger/{id}/receipt");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("image/png", res.Content.Headers.ContentType?.MediaType);
        Assert.Equal("nosniff", res.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("attachment", res.Content.Headers.ContentDisposition?.DispositionType);
        Assert.True(res.Headers.CacheControl?.NoStore);

        // A receipt is never in the general file list, even for the board.
        var fileId = await factory.DbAsync(db => db.LedgerEntries.AsNoTracking()
            .Where(e => e.Id == id).Select(e => e.ReceiptFileId!.Value).SingleAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await s.CfoClient.GetAsync($"/api/files/{fileId}")).StatusCode);
    }

    [Fact]
    public async Task Exporting_the_ledger_is_itself_recorded_in_the_audit_log()
    {
        var s = await SceneAsync(factory);
        await AddEntryOkAsync(s.CfoClient, s.OrgFundId, s.AccountId, s.TicketsCategoryId, "2026-10-05", "In", "2500.00", "Ticket sales");

        var res = await s.CfoClient.GetAsync("/api/finance/ledger/export.csv");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("text/csv", res.Content.Headers.ContentType?.MediaType);
        var csv = await res.Content.ReadAsStringAsync();
        Assert.Contains("Date,Fund,Account,Category", csv, StringComparison.Ordinal);
        Assert.Contains("Ticket sales", csv, StringComparison.Ordinal);
        Assert.Contains("2500.00", csv, StringComparison.Ordinal);

        var audit = await factory.DbAsync(db => db.AuditEntries.AsNoTracking()
            .SingleAsync(a => a.Action == "finance.export_ledger"));
        Assert.Equal(s.Cfo.UserId, audit.ActorId);
    }

    // ---------- Helpers ----------

    private static Dictionary<string, string?> Fields(FinanceScene s) => new()
    {
        ["fundId"] = s.OrgFundId.ToString(),
        ["paymentAccountId"] = s.AccountId.ToString(),
        ["categoryId"] = s.TicketsCategoryId.ToString(),
        ["entryDate"] = "2026-10-05",
        ["direction"] = "Out",
        ["amount"] = "1500.00",
        ["description"] = "Venue deposit",
    };

    private static async Task AssertFieldAsync(FinanceScene s, Action<Dictionary<string, string?>> tweak, string field)
    {
        var fields = Fields(s);
        tweak(fields);
        var res = await s.CfoClient.PostFormFileAsync("/api/finance/ledger", fields, Png, "receipt.png", "image/png", field: "receipt");
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var problem = await ApiClient.JsonAsync(res);
        Assert.True(problem.GetProperty("errors").TryGetProperty(field, out _),
            $"Expected a validation error on '{field}', got: {problem}");
    }

    /// <summary>Counts entries on this scene's own account, so other tests in the class cannot be counted too.</summary>
    private static async Task<int> CountAsync(FinanceScene s, string query)
    {
        var res = await s.CfoClient.GetAsync($"/api/finance/ledger?accountId={s.AccountId}&{query}");
        res.EnsureSuccessStatusCode();
        return (await ApiClient.JsonAsync(res)).GetProperty("total").GetInt32();
    }
}
