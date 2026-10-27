using System.Net;
using System.Text.Json;
using Htn.Api.Tests.Infrastructure;
using Htn.Domain.Organization;
using static Htn.Api.Tests.Committees.CommitteeTestData;
using static Htn.Api.Tests.Events.EventTestData;

namespace Htn.Api.Tests.Sponsors;

/// <summary>
/// The cast for the sponsors slice: a board member (who may manage sponsors) and a plain goon and apprentice (who
/// may not — sponsors are board-only, so they get an honest 403). A payment account stands in for where a
/// sponsor's cash lands.
/// </summary>
internal sealed record SponsorsScene(
    ApiClient Board,
    ApiClient Goon,
    ApiClient Apprentice,
    Guid EventId,
    Guid AccountId);

internal static class SponsorsTestData
{
    public static byte[] Png { get; } = Tasks.TaskTestData.Png;

    public static async Task<SponsorsScene> SceneAsync(HtnApiFactory factory)
    {
        var (_, board) = await SignedInUserAsync(factory, PersonLevel.Board);
        var (_, goon) = await SignedInUserAsync(factory, PersonLevel.Goon);
        var (_, apprentice) = await SignedInUserAsync(factory, PersonLevel.Apprentice);
        var eventId = await OnSaleAsync(board);
        var accountId = await AccountAsync(board);
        return new SponsorsScene(board, goon, apprentice, eventId, accountId);
    }

    public static async Task<Guid> AccountAsync(ApiClient board)
    {
        var res = await board.PostAsync("/api/settings/payment-accounts", new
        {
            kind = "GCash",
            label = "GCash " + Guid.NewGuid().ToString("N")[..6],
            accountName = "HackTheNorth PH",
            accountNumber = "09171234567",
        });
        res.EnsureSuccessStatusCode();
        return (await ApiClient.JsonAsync(res)).GetProperty("id").GetGuid();
    }

    public static async Task<Guid> CreateTierAsync(ApiClient board, Guid eventId, object? body = null)
    {
        var res = await board.PostAsync($"/api/events/{eventId}/sponsor-tiers", body ?? new
        {
            @class = "Gold",
            name = "Gold " + Guid.NewGuid().ToString("N")[..6],
            cashAmount = 100000m,
            slots = 3,
            compedSeats = 3,
            sortOrder = 10,
            perks = new[] { "Logo on lanyard", "Booth (2 × 2 m)" },
        });
        res.EnsureSuccessStatusCode();
        var page = await ApiClient.JsonAsync(res);
        return page.GetProperty("tiers").EnumerateArray().Last().GetProperty("id").GetGuid();
    }

    public static async Task<Guid> CreateSponsorAsync(ApiClient board, Guid eventId, object? body = null)
    {
        var res = await board.PostAsync($"/api/events/{eventId}/sponsors", body ?? new
        {
            kind = "Organization",
            name = "CloudCorp " + Guid.NewGuid().ToString("N")[..6],
            contactName = "Kai Cruz",
            contactRole = "Partnerships",
            cashAmount = 200000m,
            compedSeats = 0,
        });
        if (res.StatusCode != HttpStatusCode.OK)
            throw new InvalidOperationException($"Create sponsor failed: {res.StatusCode} {await res.Content.ReadAsStringAsync()}");
        return (await ApiClient.JsonAsync(res)).GetProperty("sponsor").GetProperty("id").GetGuid();
    }

    public static Task<HttpResponseMessage> CashReceivedAsync(
        ApiClient board, Guid eventId, Guid sid, Guid accountId, string? reference = "OR-9001") =>
        board.PostAsync($"/api/events/{eventId}/sponsors/{sid}/cash-received",
            new { paymentAccountId = accountId, receivedOn = "2026-11-01", reference });

    public static JsonElement Sponsor(this JsonElement detail) => detail.GetProperty("sponsor");
}
