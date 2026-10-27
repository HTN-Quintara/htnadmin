using System.Net;
using System.Text.Json;
using Htn.Api.Tests.Infrastructure;
using Htn.Domain.Finance;
using Htn.Domain.Organization;
using static Htn.Api.Tests.Committees.CommitteeTestData;
using static Htn.Api.Tests.Events.EventTestData;

namespace Htn.Api.Tests.Finance;

/// <summary>
/// The cast every finance test needs, because finance has four distinct kinds of caller and the difference between
/// them is the whole point of the slice:
/// <list type="bullet">
/// <item><paramref name="CfoClient"/> — the one board member with the CFO flag: posts, approves, closes.</item>
/// <item><paramref name="BoardClient"/> — a board member without it: reads, and files expense requests.</item>
/// <item><paramref name="AssistantClient"/> — the one goon with the finance-assistant flag: adds entries that wait.</item>
/// <item><paramref name="OutsiderClient"/> — a plain goon. Finance must be <b>invisible</b> to them: 404, not 403.</item>
/// </list>
/// </summary>
internal sealed record FinanceScene(
    HtnApiFactory.TestUser Cfo,
    ApiClient CfoClient,
    HtnApiFactory.TestUser BoardMember,
    ApiClient BoardClient,
    HtnApiFactory.TestUser Assistant,
    ApiClient AssistantClient,
    HtnApiFactory.TestUser Outsider,
    ApiClient OutsiderClient,
    ApiClient ApprenticeClient,
    Guid EventId,
    Guid OrgFundId,
    Guid AccountId,
    Guid TicketsCategoryId);

internal static class FinanceTestData
{
    /// <summary>A 1×1 PNG, reused as a receipt. Same fixture shape as the task attachment tests.</summary>
    public static byte[] Png { get; } = Tasks.TaskTestData.Png;

    public static async Task<FinanceScene> SceneAsync(HtnApiFactory factory)
    {
        // The board member who seats the finance roles needs People.Manage, which every board member has.
        var (cfo, cfoClient) = await SignedInUserAsync(factory, PersonLevel.Board);
        var (boardMember, boardClient) = await SignedInUserAsync(factory, PersonLevel.Board);
        var (assistant, assistantClient) = await SignedInUserAsync(factory, PersonLevel.Goon);
        var (outsider, outsiderClient) = await SignedInUserAsync(factory, PersonLevel.Goon);
        var (_, apprenticeClient) = await SignedInUserAsync(factory, PersonLevel.Apprentice);

        // A board member may not appoint themselves CFO, so the other one does it.
        var seated = await boardClient.PutAsync("/api/finance-roles",
            new { cfoPersonId = cfo.PersonId, assistantPersonId = assistant.PersonId });
        if (seated.StatusCode != HttpStatusCode.OK)
            throw new InvalidOperationException($"Could not seat the finance roles: {seated.StatusCode} {await seated.Content.ReadAsStringAsync()}");

        var eventId = await OnSaleAsync(boardClient);
        var accountId = await AccountAsync(boardClient);

        return new FinanceScene(cfo, cfoClient, boardMember, boardClient, assistant, assistantClient,
            outsider, outsiderClient, apprenticeClient, eventId, Funds.OrganizationId, accountId,
            LedgerCategories.TicketsId);
    }

    /// <summary>An active GCash account, which is both a billing account and something finance reconciles.</summary>
    public static async Task<Guid> AccountAsync(ApiClient board, string? label = null)
    {
        var res = await board.PostAsync("/api/settings/payment-accounts", new
        {
            kind = "GCash",
            label = label ?? "GCash " + Guid.NewGuid().ToString("N")[..6],
            accountName = "HackTheNorth PH",
            accountNumber = "09171234567",
        });
        res.EnsureSuccessStatusCode();
        return (await ApiClient.JsonAsync(res)).GetProperty("id").GetGuid();
    }

    /// <summary>Adds an entry the way the screen does: multipart, with a receipt.</summary>
    public static Task<HttpResponseMessage> AddEntryAsync(
        ApiClient client, Guid fundId, Guid accountId, Guid categoryId, string entryDate, string direction,
        string amount, string description = "Venue deposit", string? reference = "OR-1001", string? status = null) =>
        client.PostFormFileAsync("/api/finance/ledger", new Dictionary<string, string?>
        {
            ["fundId"] = fundId.ToString(),
            ["paymentAccountId"] = accountId.ToString(),
            ["categoryId"] = categoryId.ToString(),
            ["entryDate"] = entryDate,
            ["direction"] = direction,
            ["amount"] = amount,
            ["description"] = description,
            ["reference"] = reference,
            // Never honoured: the server decides the status from who is asking. Sent here to prove it is ignored.
            ["status"] = status,
        }, Png, "receipt.png", "image/png", field: "receipt");

    public static async Task<Guid> AddEntryOkAsync(
        ApiClient client, Guid fundId, Guid accountId, Guid categoryId, string entryDate, string direction,
        string amount, string description = "Venue deposit")
    {
        var res = await AddEntryAsync(client, fundId, accountId, categoryId, entryDate, direction, amount, description);
        if (res.StatusCode != HttpStatusCode.OK)
            throw new InvalidOperationException($"Could not add the entry: {res.StatusCode} {await res.Content.ReadAsStringAsync()}");
        return (await ApiClient.JsonAsync(res)).GetProperty("id").GetGuid();
    }

    public static async Task<JsonElement> AccountsAsync(ApiClient client, int year, int month)
    {
        var res = await client.GetAsync($"/api/finance/accounts?year={year}&month={month}");
        res.EnsureSuccessStatusCode();
        return await ApiClient.JsonAsync(res);
    }

    /// <summary>Reconciles every active, reconcilable account to its own book balance, which balances the month.</summary>
    public static async Task ReconcileAllAsync(ApiClient cfo, int year, int month)
    {
        var accounts = await AccountsAsync(cfo, year, month);
        foreach (var account in accounts.GetProperty("accounts").EnumerateArray())
        {
            if (!account.GetProperty("isReconcilable").GetBoolean()) continue;
            if (!account.GetProperty("isActive").GetBoolean()) continue;
            var id = account.GetProperty("id").GetGuid();
            var book = account.GetProperty("bookBalance").GetDecimal();
            var res = await cfo.PostAsync($"/api/finance/accounts/{id}/reconcile",
                new { year, month, statementBalance = book, note = "Matches the statement" });
            if (res.StatusCode != HttpStatusCode.OK)
                throw new InvalidOperationException($"Could not reconcile: {res.StatusCode} {await res.Content.ReadAsStringAsync()}");
        }
    }

    public static async Task<JsonElement> RequestAsync(ApiClient client, decimal amount = 40_000m)
    {
        var res = await client.PostAsync("/api/finance/requests", new
        {
            title = "Year-end party",
            description = "Venue and food",
            amount,
            categoryId = LedgerCategories.WriteOffId,
            neededBy = "2026-12-15",
        });
        res.EnsureSuccessStatusCode();
        return await ApiClient.JsonAsync(res);
    }
}
