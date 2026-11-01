using System.Net;
using Htn.Api.Tests.Infrastructure;
using Htn.Domain.Finance;
using Microsoft.EntityFrameworkCore;
using static Htn.Api.Tests.Finance.FinanceTestData;
using static Htn.Api.Tests.Registrations.RegistrationTestData;

namespace Htn.Api.Tests.Finance;

/// <summary>
/// Frame 26: non-event spending. A board member requests, the CFO approves or rejects with a reason, then marks it
/// paid — which is the only way a request ever touches a balance.
/// </summary>
public sealed class ExpenseRequestTests(HtnApiFactory factory) : IClassFixture<HtnApiFactory>
{
    [Fact]
    public async Task A_request_is_approved_then_paid_and_the_payment_posts_to_the_organization_fund()
    {
        var s = await SceneAsync(factory);
        var request = await RequestAsync(s.BoardClient, 12_500.50m);
        var id = request.GetProperty("id").GetGuid();

        Assert.Equal("Pending", request.GetProperty("status").GetString());
        Assert.Equal("Year-end party", request.GetProperty("title").GetString());
        Assert.NotNull(request.GetProperty("requestedBy").GetString());
        Assert.True(request.GetProperty("canCancel").GetBoolean());       // their own, still pending
        Assert.False(request.GetProperty("canApprove").GetBoolean());     // not the CFO

        var before = await OrgBalanceAsync(s);

        // Paying before approval is refused.
        var early = await PayAsync(s, id);
        Assert.Equal(HttpStatusCode.Conflict, early.StatusCode);
        Assert.Equal("request_not_approved", (await ApiClient.JsonAsync(early)).GetProperty("code").GetString());

        var approved = await s.CfoClient.PostAsync($"/api/finance/requests/{id}/approve", new { reason = "Booked at the usual place" });
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        var dto = await ApiClient.JsonAsync(approved);
        Assert.Equal("Approved", dto.GetProperty("status").GetString());
        Assert.Equal("Booked at the usual place", dto.GetProperty("decisionReason").GetString());
        Assert.True(dto.GetProperty("canPay").GetBoolean());

        var paid = await PayAsync(s, id);
        Assert.Equal(HttpStatusCode.OK, paid.StatusCode);
        var paidDto = await ApiClient.JsonAsync(paid);
        Assert.Equal("Paid", paidDto.GetProperty("status").GetString());
        var entryId = paidDto.GetProperty("paidEntryId").GetGuid();

        // One posted entry out of the Organization fund, with the receipt attached.
        var entry = await factory.DbAsync(db => db.LedgerEntries.AsNoTracking().SingleAsync(e => e.Id == entryId));
        Assert.Equal(Funds.OrganizationId, entry.FundId);
        Assert.Equal(LedgerDirection.Out, entry.Direction);
        Assert.Equal(LedgerStatus.Posted, entry.Status);
        Assert.Equal(LedgerSource.ExpenseRequest, entry.Source);
        Assert.Equal(id, entry.SourceId);
        Assert.Equal(12_500.50m, entry.Amount);
        Assert.NotNull(entry.ReceiptFileId);

        Assert.Equal(before - 12_500.50m, await OrgBalanceAsync(s));

        // And never twice.
        var again = await PayAsync(s, id);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("request_already_paid", (await ApiClient.JsonAsync(again)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_rejection_needs_a_reason_and_the_requester_is_emailed_the_decision()
    {
        var s = await SceneAsync(factory);
        var id = (await RequestAsync(s.BoardClient, 3_000m)).GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.BadRequest,
            (await s.CfoClient.PostAsync($"/api/finance/requests/{id}/reject", new { reason = "" })).StatusCode);

        var rejected = await s.CfoClient.PostAsync($"/api/finance/requests/{id}/reject",
            new { reason = "No budget for it this quarter" });
        Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);
        var dto = await ApiClient.JsonAsync(rejected);
        Assert.Equal("Rejected", dto.GetProperty("status").GetString());
        Assert.Equal("No budget for it this quarter", dto.GetProperty("decisionReason").GetString());
        Assert.False(dto.GetProperty("canPay").GetBoolean());

        await factory.DrainOutboxAsync();
        var mail = Assert.Single(factory.Emails.Sent,
            m => m.To == s.BoardMember.Email && m.Subject.Contains("expense request", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("rejected", mail.Subject, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("No budget for it this quarter", mail.Html, StringComparison.Ordinal);
        // The amount travels with the decision, as the email rules ask.
        Assert.Contains("3,000.00", mail.Html, StringComparison.Ordinal);

        // A decided request is not decided again.
        var twice = await s.CfoClient.PostAsync($"/api/finance/requests/{id}/approve");
        Assert.Equal(HttpStatusCode.Conflict, twice.StatusCode);
        Assert.Equal("request_decided", (await ApiClient.JsonAsync(twice)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task The_cfo_does_not_decide_their_own_request()
    {
        var s = await SceneAsync(factory);
        // The CFO can file one, but nobody can decide it: they hold Finance.Post and simply post the expense instead.
        var id = (await RequestAsync(s.CfoClient, 500m)).GetProperty("id").GetGuid();

        var res = await s.CfoClient.PostAsync($"/api/finance/requests/{id}/approve");
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("self_approval", (await ApiClient.JsonAsync(res)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Only_the_requester_or_the_cfo_can_withdraw_a_request()
    {
        var s = await SceneAsync(factory);
        var (other, otherClient) = await Committees.CommitteeTestData.SignedInUserAsync(factory, Htn.Domain.Organization.PersonLevel.Board);
        var id = (await RequestAsync(s.BoardClient, 800m)).GetProperty("id").GetGuid();
        Assert.NotEqual(Guid.Empty, other.PersonId);

        // Another board member cannot withdraw someone else's request.
        var refused = await otherClient.PostAsync($"/api/finance/requests/{id}/cancel", new { reason = "Not needed" });
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);

        var cancelled = await s.BoardClient.PostAsync($"/api/finance/requests/{id}/cancel",
            new { reason = "We used the sponsor's venue" });
        Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
        Assert.Equal("Cancelled", (await ApiClient.JsonAsync(cancelled)).GetProperty("status").GetString());

        // A withdrawn request is out of play.
        var decided = await s.CfoClient.PostAsync($"/api/finance/requests/{id}/approve");
        Assert.Equal(HttpStatusCode.Conflict, decided.StatusCode);
    }

    [Fact]
    public async Task Paying_a_request_needs_a_real_receipt()
    {
        var s = await SceneAsync(factory);
        var id = (await RequestAsync(s.BoardClient, 1_000m)).GetProperty("id").GetGuid();
        await s.CfoClient.PostAsync($"/api/finance/requests/{id}/approve");

        var noFile = await s.CfoClient.PostAsync($"/api/finance/requests/{id}/pay", new { paymentAccountId = s.AccountId });
        Assert.Equal(HttpStatusCode.BadRequest, noFile.StatusCode);
        Assert.Equal("file_required", (await ApiClient.JsonAsync(noFile)).GetProperty("code").GetString());

        var notAnImage = await s.CfoClient.PostFormFileAsync($"/api/finance/requests/{id}/pay",
            new Dictionary<string, string?> { ["paymentAccountId"] = s.AccountId.ToString() },
            [9, 9, 9], "receipt.png", "image/png", field: "receipt");
        Assert.Equal(HttpStatusCode.BadRequest, notAnImage.StatusCode);
        Assert.Equal("unsupported_type", (await ApiClient.JsonAsync(notAnImage)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task The_list_filters_by_status()
    {
        var s = await SceneAsync(factory);
        var pending = (await RequestAsync(s.BoardClient, 100m)).GetProperty("id").GetGuid();
        var toReject = (await RequestAsync(s.BoardClient, 200m)).GetProperty("id").GetGuid();
        await s.CfoClient.PostAsync($"/api/finance/requests/{toReject}/reject", new { reason = "Not this quarter" });

        var rejected = await ApiClient.JsonAsync(await s.CfoClient.GetAsync("/api/finance/requests?status=Rejected"));
        Assert.Contains(rejected.GetProperty("items").EnumerateArray(), r => r.GetProperty("id").GetGuid() == toReject);
        Assert.DoesNotContain(rejected.GetProperty("items").EnumerateArray(), r => r.GetProperty("id").GetGuid() == pending);

        var detail = await s.CfoClient.GetAsync($"/api/finance/requests/{pending}");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await s.CfoClient.GetAsync($"/api/finance/requests/{Guid.NewGuid()}")).StatusCode);
    }

    // ---------- Helpers ----------

    private static Task<HttpResponseMessage> PayAsync(FinanceScene s, Guid id) =>
        s.CfoClient.PostFormFileAsync($"/api/finance/requests/{id}/pay",
            new Dictionary<string, string?>
            {
                ["paymentAccountId"] = s.AccountId.ToString(),
                ["entryDate"] = "2026-10-20",
                ["reference"] = "OR-5500",
            }, FinanceTestData.Png, "receipt.png", "image/png", field: "receipt");

    private static async Task<decimal> OrgBalanceAsync(FinanceScene s)
    {
        var res = await s.CfoClient.GetAsync("/api/finance/funds");
        res.EnsureSuccessStatusCode();
        return (await ApiClient.JsonAsync(res)).EnumerateArray()
            .Single(f => f.GetProperty("id").GetGuid() == Funds.OrganizationId).GetProperty("balance").GetDecimal();
    }
}
