using System.Net;
using Htn.Api.Tests.Infrastructure;
using static Htn.Api.Tests.Finance.FinanceTestData;

namespace Htn.Api.Tests.Finance;

/// <summary>
/// The balancing screen's account rows. Its own class, so its own database: the close precondition reaches across
/// months, and an entry another test leaves in an open month would otherwise show up here as a blocker.
/// </summary>
public sealed class FinanceAccountsTests(HtnApiFactory factory) : IClassFixture<HtnApiFactory>
{
    [Fact]
    public async Task A_deactivated_account_can_never_block_a_close()
    {
        var s = await SceneAsync(factory);
        var retired = await AccountAsync(s.BoardClient, "Old BDO");
        await s.BoardClient.PostAsync($"/api/settings/payment-accounts/{retired}/deactivate",
            new { reason = "Account closed at the bank" });

        var accounts = await AccountsAsync(s.CfoClient, 2027, 3);
        var row = accounts.GetProperty("accounts").EnumerateArray().Single(a => a.GetProperty("id").GetGuid() == retired);
        Assert.False(row.GetProperty("isActive").GetBoolean());

        // Reconciling only the live accounts is enough: March 2027 has no entries and no earlier open month of its own.
        await ReconcileAllAsync(s.CfoClient, 2027, 3);
        var blockers = (await ApiClient.JsonAsync(await s.CfoClient.GetAsync("/api/finance/accounts?year=2027&month=3")))
            .GetProperty("period").GetProperty("blockers");
        Assert.Empty(blockers.EnumerateArray());

        // And it refuses to be reconciled at all once deactivated.
        var refused = await s.CfoClient.PostAsync($"/api/finance/accounts/{retired}/reconcile",
            new { year = 2027, month = 3, statementBalance = 0 });
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal("account_inactive", (await ApiClient.JsonAsync(refused)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task An_entry_posted_after_reconciling_makes_the_month_unbalanced_again()
    {
        var s = await SceneAsync(factory);
        // March 2028: a month with nothing before it in this database, so only this test's own state matters.
        await ReconcileAllAsync(s.CfoClient, 2028, 3);
        var ready = (await ApiClient.JsonAsync(await s.CfoClient.GetAsync("/api/finance/accounts?year=2028&month=3")))
            .GetProperty("period");
        Assert.Empty(ready.GetProperty("blockers").EnumerateArray());
        Assert.True(ready.GetProperty("canClose").GetBoolean());

        // A late entry in that month moves the books, so the signed-off reconciliation no longer matches them.
        await AddEntryOkAsync(s.CfoClient, s.OrgFundId, s.AccountId, s.TicketsCategoryId, "2028-03-15", "In", "750.00", "Late entry");

        var after = (await ApiClient.JsonAsync(await s.CfoClient.GetAsync("/api/finance/accounts?year=2028&month=3")))
            .GetProperty("period");
        var blockers = after.GetProperty("blockers").EnumerateArray().ToList();
        Assert.Contains(blockers, b => b.GetProperty("code").GetString() == "month_unbalanced");
        Assert.False(after.GetProperty("canClose").GetBoolean());

        var refused = await s.CfoClient.PostAsync("/api/finance/periods/2028/3/close");
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal("month_unbalanced", (await ApiClient.JsonAsync(refused)).GetProperty("code").GetString());

        // Reconciling again against the new books clears it.
        await ReconcileAllAsync(s.CfoClient, 2028, 3);
        Assert.Equal(HttpStatusCode.OK, (await s.CfoClient.PostAsync("/api/finance/periods/2028/3/close")).StatusCode);
    }

    [Fact]
    public async Task A_deactivated_account_that_still_holds_posted_money_blocks_the_close()
    {
        var s = await SceneAsync(factory);
        var retiring = await AccountAsync(s.BoardClient, "BDO closing");

        // Money went through it before it was retired, so it still has to be accounted for.
        await AddEntryOkAsync(s.CfoClient, s.OrgFundId, retiring, s.TicketsCategoryId, "2029-05-04", "In", "3000.00", "Last deposit");
        await s.BoardClient.PostAsync($"/api/settings/payment-accounts/{retiring}/deactivate",
            new { reason = "Account closed at the bank" });

        var blockers = (await ApiClient.JsonAsync(await s.CfoClient.GetAsync("/api/finance/accounts?year=2029&month=5")))
            .GetProperty("period").GetProperty("blockers").EnumerateArray().ToList();
        Assert.Contains(blockers, b => b.GetProperty("code").GetString() == "month_unbalanced"
                                       && b.GetProperty("message").GetString()!.Contains("BDO closing", StringComparison.Ordinal));
    }
}
