using System.Net;
using Htn.Api.Tests.Infrastructure;
using Htn.Domain.Organization;
using static Htn.Api.Tests.Committees.CommitteeTestData;
using static Htn.Api.Tests.Events.EventTestData;

namespace Htn.Api.Tests.Merch;

/// <summary>
/// The cast for the Phase 8A merch slice, mirroring <c>SuppliersTestData.SuppliersScene</c>: a board member (manage +
/// sell anywhere), a committee lead of this event (sell, but not manage), a goon who leads nothing (denied both)
/// and an apprentice.
/// </summary>
internal sealed record MerchScene(ApiClient Board, ApiClient CommitteeLead, ApiClient Goon, ApiClient Apprentice, Guid EventId, Guid CommitteeId);

internal static class MerchTestData
{
    public static async Task<MerchScene> SceneAsync(HtnApiFactory factory)
    {
        var (_, board) = await SignedInUserAsync(factory, PersonLevel.Board);
        var (leadUser, lead) = await SignedInUserAsync(factory, PersonLevel.Goon);
        var (_, goon) = await SignedInUserAsync(factory, PersonLevel.Goon);
        var (_, apprentice) = await SignedInUserAsync(factory, PersonLevel.Apprentice);
        var eventId = await OnSaleAsync(board);
        var committeeId = await AddCommitteeAsync(board, eventId, "Logistics");
        await SetLeadAsync(board, eventId, committeeId, leadUser.PersonId);
        return new MerchScene(board, lead, goon, apprentice, eventId, committeeId);
    }

    public static string ItemsUrl(Guid eventId) => $"/api/events/{eventId}/merch/items";
    public static string DiscountsUrl(Guid eventId) => $"/api/events/{eventId}/merch/discounts";
    public static string DashboardUrl(Guid eventId) => $"/api/events/{eventId}/merch";
    public static string SalesExportUrl(Guid eventId) => $"/api/events/{eventId}/merch/sales/export";
    public static string BuyerSearchUrl(Guid eventId, string q) => $"/api/events/{eventId}/pos/buyer-search?q={Uri.EscapeDataString(q)}";
    public static string ScanUrl(Guid eventId) => $"/api/events/{eventId}/pos/scan";
    public static string SaleUrl(Guid eventId) => $"/api/events/{eventId}/pos/sale";

    public static object NewItem(string? name = null, decimal price = 350m, string? swatch = "#B71C1C") => new
    {
        name = name ?? "Shirt " + Guid.NewGuid().ToString("N")[..8], price, swatchColor = swatch,
    };

    public static async Task<Guid> CreateItemAsync(ApiClient board, Guid eventId, object? body = null)
    {
        var res = await board.PostAsync(ItemsUrl(eventId), body ?? NewItem());
        if (res.StatusCode != HttpStatusCode.OK)
            throw new InvalidOperationException($"Create item failed: {(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
        return (await ApiClient.JsonAsync(res)).GetProperty("id").GetGuid();
    }

    public static async Task<Guid> CreateVariantAsync(ApiClient board, Guid eventId, Guid itemId, string label = "M", int stock = 10)
    {
        var res = await board.PostAsync($"{ItemsUrl(eventId)}/{itemId}/variants", new { label, stockOnHand = stock });
        if (res.StatusCode != HttpStatusCode.OK)
            throw new InvalidOperationException($"Create variant failed: {(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
        return (await ApiClient.JsonAsync(res)).GetProperty("id").GetGuid();
    }

    public static Task<HttpResponseMessage> SellAsync(
        ApiClient client, Guid eventId, Guid variantId, int qty = 1, string method = "Cash", string buyerName = "Walk-in", Guid? clientRequestId = null) =>
        client.PostAsync(SaleUrl(eventId), new
        {
            clientRequestId = clientRequestId ?? Guid.NewGuid(), buyerPersonId = (Guid?)null, buyerAttendeeId = (Guid?)null, buyerName,
            lines = new[] { new { variantId, qty } }, method, station = "Booth A",
        });
}
