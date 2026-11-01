using System.Net;
using Htn.Api.Tests.Infrastructure;
using Htn.Domain.Finance;
using Microsoft.EntityFrameworkCore;
using static Htn.Api.Tests.Events.EventTestData;
using static Htn.Api.Tests.Finance.FinanceTestData;
using static Htn.Api.Tests.Registrations.RegistrationTestData;

namespace Htn.Api.Tests.Finance;

/// <summary>
/// An event's budget against its actuals (frame 25). "Only the CFO edits them; there is no board approval", so the
/// safeguard is history: every save is a new immutable version with a reason, and the board is emailed.
/// </summary>
public sealed class BudgetTests(HtnApiFactory factory) : IClassFixture<HtnApiFactory>
{
    [Fact]
    public async Task An_event_with_no_budget_yet_reports_an_empty_one()
    {
        var s = await SceneAsync(factory);
        var eventId = await OnSaleAsync(s.BoardClient);

        var res = await s.CfoClient.GetAsync($"/api/finance/budgets/{eventId}");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var budget = await ApiClient.JsonAsync(res);

        Assert.Equal(System.Text.Json.JsonValueKind.Null, budget.GetProperty("id").ValueKind);
        Assert.Empty(budget.GetProperty("lines").EnumerateArray());
        Assert.Empty(budget.GetProperty("history").EnumerateArray());
        Assert.True(budget.GetProperty("canEdit").GetBoolean());

        Assert.Equal(HttpStatusCode.NotFound, (await s.CfoClient.GetAsync($"/api/finance/budgets/{Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task Every_save_is_a_new_version_with_a_reason_and_the_board_is_emailed()
    {
        var s = await SceneAsync(factory);
        var eventId = await OnSaleAsync(s.BoardClient);

        Assert.Equal(HttpStatusCode.BadRequest, (await s.CfoClient.PutAsync($"/api/finance/budgets/{eventId}", new
        {
            reason = "",
            lines = new[] { Line(LedgerCategories.WriteOffId, "Expense", "Venue", 25_000) },
        })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await s.CfoClient.PutAsync($"/api/finance/budgets/{eventId}",
            new { reason = "No lines", lines = Array.Empty<object>() })).StatusCode);

        var first = await SaveAsync(s, eventId, "First draft", 25_000);
        Assert.Equal(1, first.GetProperty("version").GetProperty("versionNumber").GetInt32());
        Assert.Equal("First draft", first.GetProperty("version").GetProperty("reason").GetString());
        Assert.Single(first.GetProperty("history").EnumerateArray());

        var second = await SaveAsync(s, eventId, "The venue quote went up", 30_000);
        Assert.Equal(2, second.GetProperty("version").GetProperty("versionNumber").GetInt32());
        Assert.Equal(2, second.GetProperty("history").EnumerateArray().Count());
        Assert.Equal(30_000m, second.GetProperty("lines")[0].GetProperty("amountBudgeted").GetDecimal());

        // Version 1 is still readable, with its own numbers: that is the whole point of versioning.
        var oldId = second.GetProperty("history").EnumerateArray()
            .Single(v => v.GetProperty("versionNumber").GetInt32() == 1).GetProperty("id").GetGuid();
        var old = await ApiClient.JsonAsync(await s.CfoClient.GetAsync($"/api/finance/budgets/{eventId}/versions/{oldId}"));
        Assert.Equal(25_000m, old.GetProperty("lines")[0].GetProperty("amountBudgeted").GetDecimal());

        // The audit trail carries before and after.
        // Scoped to this event: the class shares one database with the other budget tests.
        var budgetId = second.GetProperty("id").GetGuid().ToString();
        var audit = await factory.DbAsync(db => db.AuditEntries.AsNoTracking()
            .Where(a => a.Action == "finance.budget_save" && a.EntityId == budgetId)
            .OrderBy(a => a.At).ToListAsync());
        Assert.Equal(2, audit.Count);
        Assert.Null(audit[0].BeforeJson);
        Assert.NotNull(audit[1].BeforeJson);
        Assert.Equal("The venue quote went up", audit[1].Reason);

        // "The board is notified": one message per other active board member.
        await factory.DrainOutboxAsync();
        // This scene's recipients only: the fake sender collects every mail the class sends.
        var mails = factory.Emails.Sent
            .Where(m => m.Subject.Contains("budget updated", StringComparison.OrdinalIgnoreCase))
            .Where(m => m.To == s.BoardMember.Email || m.To == s.Cfo.Email)
            .ToList();
        Assert.Contains(mails, m => m.To == s.BoardMember.Email);
        // Not to the CFO who saved it.
        Assert.DoesNotContain(mails, m => m.To == s.Cfo.Email);
        Assert.Contains(mails, m => m.Html.Contains("The venue quote went up", StringComparison.Ordinal));
        Assert.Contains(mails, m => m.Html.Contains("First draft", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_same_category_cannot_appear_twice_on_one_side()
    {
        var s = await SceneAsync(factory);
        var eventId = await OnSaleAsync(s.BoardClient);

        var duplicate = await s.CfoClient.PutAsync($"/api/finance/budgets/{eventId}", new
        {
            reason = "Duplicate",
            lines = new[]
            {
                Line(LedgerCategories.WriteOffId, "Expense", "Venue", 1_000),
                Line(LedgerCategories.WriteOffId, "Expense", "Venue again", 2_000),
            },
        });
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);

        // The same category on the other side is a different line, and is fine.
        var bothSides = await s.CfoClient.PutAsync($"/api/finance/budgets/{eventId}", new
        {
            reason = "Both sides",
            lines = new[]
            {
                Line(LedgerCategories.WriteOffId, "Expense", "Write-offs", 1_000),
                Line(LedgerCategories.WriteOffId, "Income", "Recovered", 500),
            },
        });
        Assert.Equal(HttpStatusCode.OK, bothSides.StatusCode);
    }

    [Fact]
    public async Task A_category_that_is_gone_cannot_be_budgeted_against()
    {
        var s = await SceneAsync(factory);
        var eventId = await OnSaleAsync(s.BoardClient);

        var res = await s.CfoClient.PutAsync($"/api/finance/budgets/{eventId}", new
        {
            reason = "Unknown category",
            lines = new[] { Line(Guid.NewGuid(), "Expense", "Mystery", 1_000) },
        });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("unknown_category", (await ApiClient.JsonAsync(res)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Actuals_come_from_the_posted_entries_of_the_events_fund()
    {
        var s = await SceneAsync(factory);
        var eventId = await OnSaleAsync(s.BoardClient);

        // A fund with income and spending against two categories.
        await factory.DbAsync(async db =>
        {
            var ev = await db.Events.AsNoTracking().SingleAsync(e => e.Id == eventId);
            db.Funds.Add(Fund.ForEvent(ev, DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
            return 0;
        });
        var fundId = await factory.DbAsync(db => db.Funds.AsNoTracking()
            .Where(f => f.EventId == eventId).Select(f => f.Id).SingleAsync());

        await AddEntryOkAsync(s.CfoClient, fundId, s.AccountId, LedgerCategories.TicketsId, "2026-10-05", "In", "12000.00", "Ticket sales");
        await AddEntryOkAsync(s.CfoClient, fundId, s.AccountId, LedgerCategories.WriteOffId, "2026-10-06", "Out", "8000.00", "Venue");
        // Neither of these is money, so neither may show up as an actual.
        await AddEntryOkAsync(s.AssistantClient, fundId, s.AccountId, LedgerCategories.WriteOffId, "2026-10-07", "Out", "5000.00", "Pending");
        var voided = await AddEntryOkAsync(s.CfoClient, fundId, s.AccountId, LedgerCategories.WriteOffId, "2026-10-08", "Out", "900.00", "Mistake");
        await s.CfoClient.PostAsync($"/api/finance/ledger/{voided}/void", new { reason = "Wrong supplier" });

        var budget = await ApiClient.JsonAsync(await s.CfoClient.PutAsync($"/api/finance/budgets/{eventId}", new
        {
            reason = "Plan",
            lines = new[]
            {
                Line(LedgerCategories.TicketsId, "Income", "Ticket sales", 10_000),
                Line(LedgerCategories.WriteOffId, "Expense", "Venue", 9_000),
            },
        }));

        var lines = budget.GetProperty("lines").EnumerateArray().ToList();
        var income = lines.Single(l => l.GetProperty("kind").GetString() == "Income");
        var expense = lines.Single(l => l.GetProperty("kind").GetString() == "Expense");

        // Income: earned 12,000 against a plan of 10,000 — 2,000 better than planned.
        Assert.Equal(12_000m, income.GetProperty("actual").GetDecimal());
        Assert.Equal(2_000m, income.GetProperty("variance").GetDecimal());
        // Expense: spent 8,000 of a planned 9,000 — 1,000 under. The pending and voided rows are excluded.
        Assert.Equal(8_000m, expense.GetProperty("actual").GetDecimal());
        Assert.Equal(1_000m, expense.GetProperty("variance").GetDecimal());

        var totals = budget.GetProperty("totals");
        Assert.Equal(10_000m, totals.GetProperty("incomeBudgeted").GetDecimal());
        Assert.Equal(12_000m, totals.GetProperty("incomeActual").GetDecimal());
        Assert.Equal(9_000m, totals.GetProperty("expenseBudgeted").GetDecimal());
        Assert.Equal(8_000m, totals.GetProperty("expenseActual").GetDecimal());
        Assert.Equal(1_000m, totals.GetProperty("netBudgeted").GetDecimal());
        Assert.Equal(4_000m, totals.GetProperty("netActual").GetDecimal());
        Assert.Equal(4_000m, budget.GetProperty("fundBalance").GetDecimal());
    }

    // ---------- Helpers ----------

    private static object Line(Guid categoryId, string kind, string label, decimal amount) =>
        new { categoryId, kind, label, amountBudgeted = amount, sortOrder = 10 };

    private async Task<System.Text.Json.JsonElement> SaveAsync(FinanceScene s, Guid eventId, string reason, decimal amount)
    {
        var res = await s.CfoClient.PutAsync($"/api/finance/budgets/{eventId}", new
        {
            reason,
            lines = new[] { Line(LedgerCategories.WriteOffId, "Expense", "Venue", amount) },
        });
        if (res.StatusCode != HttpStatusCode.OK)
            throw new InvalidOperationException($"Save failed: {res.StatusCode} {await res.Content.ReadAsStringAsync()}");
        return await ApiClient.JsonAsync(res);
    }
}
