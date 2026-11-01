using System.Net;
using Htn.Api.Tests.Infrastructure;
using Htn.Domain.Finance;
using static Htn.Api.Tests.Finance.FinanceTestData;

namespace Htn.Api.Tests.Finance;

/// <summary>
/// Who may reach finance at all, and who may do what inside it. This is the most important class in the slice: the
/// rule is not merely "forbidden to non-board" but <b>invisible</b> — <c>permissions.md</c> requires a 404 so the
/// module's existence is not revealed — with honest 403s only once a caller is inside.
/// </summary>
public sealed class FinanceAccessTests(HtnApiFactory factory) : IClassFixture<HtnApiFactory>
{
    /// <summary>Every finance read, as (method, route).</summary>
    private static IEnumerable<(string Method, string Route)> Reads(FinanceScene s) =>
    [
        ("GET", "/api/finance/overview"),
        ("GET", "/api/finance/funds"),
        ("GET", "/api/finance/categories"),
        ("GET", "/api/finance/audit"),
        ("GET", "/api/finance/audit/export.csv"),
        ("GET", "/api/finance/periods"),
        ("GET", "/api/finance/ledger"),
        ("GET", "/api/finance/ledger/export.csv"),
        ("GET", "/api/finance/accounts"),
        ("GET", $"/api/finance/budgets/{s.EventId}"),
        ("GET", "/api/finance/requests"),
    ];

    /// <summary>Every finance write, as (method, route, body).</summary>
    private static IEnumerable<(string Method, string Route, object? Body)> Writes(FinanceScene s) =>
    [
        ("POST", "/api/finance/periods/2026/10/close", new { note = "done" }),
        ("POST", "/api/finance/periods/2026/10/reopen", new { reason = "A late invoice" }),
        ("POST", $"/api/finance/funds/{s.OrgFundId}/sweep", new { entryDate = "2026-10-31" }),
        ("POST", "/api/finance/ledger/transfers", new
        {
            fromFundId = s.OrgFundId, toFundId = s.OrgFundId, entryDate = "2026-10-05", amount = 100, description = "x",
        }),
        ("POST", $"/api/finance/accounts/{s.AccountId}/reconcile", new { year = 2026, month = 10, statementBalance = 0 }),
        ("PUT", $"/api/finance/budgets/{s.EventId}", new
        {
            reason = "First draft",
            lines = new[] { new { categoryId = LedgerCategories.WriteOffId, kind = "Expense", label = "Venue", amountBudgeted = 1000 } },
        }),
        ("POST", "/api/finance/requests", new
        {
            title = "Party", amount = 1000, categoryId = LedgerCategories.WriteOffId,
        }),
    ];

    // ---------- 404 matrix: finance is invisible outside finance ----------

    [Fact]
    public async Task A_plain_goon_and_an_apprentice_cannot_tell_that_finance_exists()
    {
        var s = await SceneAsync(factory);
        var entryId = await AddEntryOkAsync(s.CfoClient, s.OrgFundId, s.AccountId, s.TicketsCategoryId,
            "2026-10-05", "Out", "1500.00");

        foreach (var client in new[] { s.OutsiderClient, s.ApprenticeClient })
        {
            foreach (var (_, route) in Reads(s))
                Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(route)).StatusCode);

            // The per-record routes too, including one that really exists: knowing an id must not leak the module.
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/finance/ledger/{entryId}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/finance/ledger/{entryId}/receipt")).StatusCode);

            foreach (var (method, route, body) in Writes(s))
                Assert.Equal(HttpStatusCode.NotFound,
                    (await client.SendAsync(new HttpMethod(method), route, body, csrf: true)).StatusCode);

            Assert.Equal(HttpStatusCode.NotFound,
                (await client.PostAsync($"/api/finance/ledger/{entryId}/approve")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound,
                (await client.PostAsync($"/api/finance/ledger/{entryId}/void", new { reason = "no" })).StatusCode);
            // Adding an entry is multipart, and must be just as invisible.
            Assert.Equal(HttpStatusCode.NotFound,
                (await AddEntryAsync(client, s.OrgFundId, s.AccountId, s.TicketsCategoryId, "2026-10-06", "Out", "10.00")).StatusCode);
        }
    }

    [Fact]
    public async Task An_anonymous_caller_is_told_to_sign_in_not_that_nothing_is_there()
    {
        var s = await SceneAsync(factory);
        var anonymous = factory.NewClient();

        foreach (var (_, route) in Reads(s))
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(route)).StatusCode);
        foreach (var (method, route, body) in Writes(s))
            Assert.Equal(HttpStatusCode.Unauthorized,
                (await anonymous.SendAsync(new HttpMethod(method), route, body, csrf: true)).StatusCode);
    }

    // ---------- 403 matrix: inside finance, denials are honest ----------

    [Fact]
    public async Task The_finance_assistant_can_only_read_the_ledger_and_add_to_it()
    {
        var s = await SceneAsync(factory);
        var entryId = await AddEntryOkAsync(s.CfoClient, s.OrgFundId, s.AccountId, s.TicketsCategoryId,
            "2026-10-05", "Out", "1500.00");
        var c = s.AssistantClient;

        // The six routes that are theirs.
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/finance/ledger")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync($"/api/finance/ledger/{entryId}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync($"/api/finance/ledger/{entryId}/receipt")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/finance/categories")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/finance/funds")).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await AddEntryAsync(c, s.OrgFundId, s.AccountId, s.TicketsCategoryId, "2026-10-06", "Out", "250.00")).StatusCode);

        // Everything else is forbidden — not hidden: they are inside the module.
        foreach (var route in new[]
        {
            "/api/finance/overview", "/api/finance/audit", "/api/finance/audit/export.csv", "/api/finance/periods",
            "/api/finance/ledger/export.csv", "/api/finance/accounts", "/api/finance/requests",
            $"/api/finance/budgets/{s.EventId}",
        })
            Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync(route)).StatusCode);

        foreach (var (method, route, body) in Writes(s))
            Assert.Equal(HttpStatusCode.Forbidden,
                (await c.SendAsync(new HttpMethod(method), route, body, csrf: true)).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await c.PostAsync($"/api/finance/ledger/{entryId}/approve")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await c.PostAsync($"/api/finance/ledger/{entryId}/void", new { reason = "no" })).StatusCode);
    }

    [Fact]
    public async Task An_assistant_entry_lands_pending_whatever_the_client_sends()
    {
        var s = await SceneAsync(factory);

        var res = await AddEntryAsync(s.AssistantClient, s.OrgFundId, s.AccountId, s.TicketsCategoryId,
            "2026-10-07", "Out", "999.00", status: "Posted");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var entry = await ApiClient.JsonAsync(res);
        Assert.Equal("Pending", entry.GetProperty("status").GetString());
        // And it is not money: it cannot approve itself, and the assistant cannot approve it either.
        Assert.False(entry.GetProperty("canApprove").GetBoolean());
        Assert.False(entry.GetProperty("canVoid").GetBoolean());
    }

    [Fact]
    public async Task A_board_member_without_the_cfo_flag_reads_everything_and_changes_nothing()
    {
        var s = await SceneAsync(factory);
        var entryId = await AddEntryOkAsync(s.CfoClient, s.OrgFundId, s.AccountId, s.TicketsCategoryId,
            "2026-10-05", "Out", "1500.00");
        var c = s.BoardClient;

        foreach (var (_, route) in Reads(s))
            Assert.Equal(HttpStatusCode.OK, (await c.GetAsync(route)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync($"/api/finance/ledger/{entryId}")).StatusCode);

        // Filing an expense request is the one thing they may do.
        Assert.Equal(HttpStatusCode.OK, (await c.PostAsync("/api/finance/requests", new
        {
            title = "Year-end party", amount = 1000, categoryId = LedgerCategories.WriteOffId,
        })).StatusCode);

        // Posting, approving, reconciling, closing and editing a budget are the CFO's alone.
        foreach (var (method, route, body) in Writes(s).Where(w => !w.Route.EndsWith("/requests", StringComparison.Ordinal)))
            Assert.Equal(HttpStatusCode.Forbidden,
                (await c.SendAsync(new HttpMethod(method), route, body, csrf: true)).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await c.PostAsync($"/api/finance/ledger/{entryId}/approve")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await c.PostAsync($"/api/finance/ledger/{entryId}/void", new { reason = "no" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await AddEntryAsync(c, s.OrgFundId, s.AccountId, s.TicketsCategoryId, "2026-10-08", "Out", "10.00")).StatusCode);
    }

    [Fact]
    public async Task The_cfo_reaches_every_finance_route()
    {
        var s = await SceneAsync(factory);
        var c = s.CfoClient;

        foreach (var (_, route) in Reads(s))
            Assert.Equal(HttpStatusCode.OK, (await c.GetAsync(route)).StatusCode);

        // A CFO entry is money straight away.
        var res = await AddEntryAsync(c, s.OrgFundId, s.AccountId, s.TicketsCategoryId, "2026-10-05", "Out", "1500.00");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var entry = await ApiClient.JsonAsync(res);
        Assert.Equal("Posted", entry.GetProperty("status").GetString());
        Assert.NotNull(entry.GetProperty("approvedAt").GetString());
        Assert.True(entry.GetProperty("canVoid").GetBoolean());

        // The writes that need no particular state reach their handler rather than the gate.
        Assert.Equal(HttpStatusCode.OK, (await c.PostAsync($"/api/finance/accounts/{s.AccountId}/reconcile",
            new { year = 2026, month = 10, statementBalance = -1500.00m })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.PutAsync($"/api/finance/budgets/{s.EventId}", new
        {
            reason = "First draft",
            lines = new[] { new { categoryId = LedgerCategories.WriteOffId, kind = "Expense", label = "Venue", amountBudgeted = 1000 } },
        })).StatusCode);
    }

    [Fact]
    public async Task The_cfo_cannot_approve_their_own_entry_so_the_assistant_is_a_second_pair_of_eyes()
    {
        var s = await SceneAsync(factory);
        // Entered by the assistant: the CFO may approve it.
        var theirs = await AddEntryOkAsync(s.AssistantClient, s.OrgFundId, s.AccountId, s.TicketsCategoryId,
            "2026-10-09", "Out", "500.00");
        var approved = await s.CfoClient.PostAsync($"/api/finance/ledger/{theirs}/approve");
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        Assert.Equal("Posted", (await ApiClient.JsonAsync(approved)).GetProperty("status").GetString());

        // Approving it again is a conflict, not a silent no-op.
        var again = await s.CfoClient.PostAsync($"/api/finance/ledger/{theirs}/approve");
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("already_posted", (await ApiClient.JsonAsync(again)).GetProperty("code").GetString());
    }
}
