using System.Net;
using System.Text.Json;
using Htn.Api.Tests.Infrastructure;
using Htn.Domain.Organization;
using static Htn.Api.Tests.Committees.CommitteeTestData;
using static Htn.Api.Tests.Events.EventTestData;
using static Htn.Api.Tests.Merch.MerchTestData;

namespace Htn.Api.Tests.PopUps;

/// <summary>
/// The cast for the Phase 8C pop-up slice: a board member (manage/sell everywhere), the lead of committee A
/// (<c>PopUpsAllowed</c>, owns the pop-up under test), the lead of committee B (also <c>PopUpsAllowed</c>, but a
/// *different* committee — proves the precise per-pop-up check, not just "leads something"), a team member with no
/// lead role at all, a plain goon who leads nothing and is on no team, and a separate CFO for the write-off tests.
/// </summary>
internal sealed record PopUpScene(
    ApiClient Board, HtnApiFactory.TestUser OwnerLead, ApiClient OwnerLeadClient, HtnApiFactory.TestUser OtherLead,
    ApiClient OtherLeadClient, HtnApiFactory.TestUser TeamMember, ApiClient TeamMemberClient, ApiClient PlainGoon,
    HtnApiFactory.TestUser Cfo, ApiClient CfoClient, Guid EventId, Guid OwnerCommitteeId, Guid OtherCommitteeId);

internal static class PopUpTestData
{
    public static async Task<PopUpScene> SceneAsync(HtnApiFactory factory)
    {
        var (_, board) = await SignedInUserAsync(factory, PersonLevel.Board);
        var (otherBoard, otherBoardClient) = await SignedInUserAsync(factory, PersonLevel.Board);
        var (cfo, cfoClient) = await SignedInUserAsync(factory, PersonLevel.Board);
        var (ownerLeadUser, ownerLead) = await SignedInUserAsync(factory, PersonLevel.Goon);
        var (otherLeadUser, otherLead) = await SignedInUserAsync(factory, PersonLevel.Goon);
        var (teamUser, teamClient) = await SignedInUserAsync(factory, PersonLevel.Apprentice);
        var (_, plainGoon) = await SignedInUserAsync(factory, PersonLevel.Goon);

        var seated = await otherBoardClient.PutAsync("/api/finance-roles", new { cfoPersonId = cfo.PersonId, assistantPersonId = (Guid?)null });
        if (seated.StatusCode != HttpStatusCode.OK)
            throw new InvalidOperationException($"Could not seat CFO: {seated.StatusCode} {await seated.Content.ReadAsStringAsync()}");

        var eventId = await OnSaleAsync(board);
        var committeeA = await AddCommitteeAsync(board, eventId, "Marketing");
        var committeeB = await AddCommitteeAsync(board, eventId, "Logistics");
        await SetLeadAsync(board, eventId, committeeA, ownerLeadUser.PersonId);
        await SetLeadAsync(board, eventId, committeeB, otherLeadUser.PersonId);
        await AllowPopUpsAsync(board, eventId, committeeA, true);
        await AllowPopUpsAsync(board, eventId, committeeB, true);

        return new PopUpScene(board, ownerLeadUser, ownerLead, otherLeadUser, otherLead, teamUser, teamClient,
            plainGoon, cfo, cfoClient, eventId, committeeA, committeeB);
    }

    public static Task<HttpResponseMessage> AllowPopUpsAsync(ApiClient board, Guid eventId, Guid committeeId, bool allowed) =>
        board.PutAsync($"/api/events/{eventId}/committees/{committeeId}/pop-ups-allowed", new { allowed });

    public static object NewPopUp(
        Guid ownerCommitteeId, IReadOnlyList<Guid>? teamPersonIds = null, string? name = null,
        DateTimeOffset? startsAt = null, DateTimeOffset? endsAt = null, string discountMode = "None") => new
    {
        name = name ?? "Pop-up " + Guid.NewGuid().ToString("N")[..8],
        venueDirectoryOrgId = (Guid?)null,
        venueText = "Mall activity area",
        startsAt = startsAt ?? DateTimeOffset.UtcNow.AddDays(1),
        endsAt = endsAt ?? DateTimeOffset.UtcNow.AddDays(2),
        feeAmount = 500m,
        discountMode,
        goonPct = (int?)null,
        apprenticePct = (int?)null,
        memberPct = (int?)null,
        boardPct = (int?)null,
        ownerCommitteeId,
        teamPersonIds = teamPersonIds ?? [],
    };

    public static async Task<Guid> CreatePopUpAsync(
        ApiClient caller, Guid ownerCommitteeId, IReadOnlyList<Guid>? teamPersonIds = null, string? name = null)
    {
        var res = await caller.PostAsync("/api/pop-ups", NewPopUp(ownerCommitteeId, teamPersonIds, name));
        if (res.StatusCode != HttpStatusCode.OK)
            throw new InvalidOperationException($"Create pop-up failed: {(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
        return (await ApiClient.JsonAsync(res)).GetProperty("id").GetGuid();
    }

    /// <summary>Puts a variant in the leftover pool: an item at the event, a variant with stock, then moved.</summary>
    public static async Task<(Guid ItemId, Guid VariantId)> LeftoverVariantAsync(
        ApiClient board, Guid eventId, int stock = 20, string label = "One size")
    {
        var itemId = await CreateItemAsync(board, eventId);
        var variantId = await CreateVariantAsync(board, eventId, itemId, label, stock);
        var moved = await board.PostAsync($"/api/events/{eventId}/merch/move-to-store", new { variantIds = new[] { variantId } });
        if (moved.StatusCode != HttpStatusCode.NoContent)
            throw new InvalidOperationException($"Move to store failed: {(int)moved.StatusCode} {await moved.Content.ReadAsStringAsync()}");
        return (itemId, variantId);
    }

    public static Task<HttpResponseMessage> AllocateAsync(ApiClient caller, Guid popUpId, Guid variantId, int qty) =>
        caller.PostAsync($"/api/pop-ups/{popUpId}/allocations", new { lines = new[] { new { variantId, qty } } });

    public static Task<HttpResponseMessage> OpenAsync(ApiClient caller, Guid popUpId) =>
        caller.PostAsync($"/api/pop-ups/{popUpId}/open", new { });

    public static Task<HttpResponseMessage> CloseAsync(ApiClient caller, Guid popUpId) =>
        caller.PostAsync($"/api/pop-ups/{popUpId}/close", new { });

    public static Task<HttpResponseMessage> CountAsync(ApiClient caller, Guid popUpId, Guid variantId, int qtyReturned, decimal cashCounted) =>
        caller.PostAsync($"/api/pop-ups/{popUpId}/count", new { returned = new[] { new { variantId, qtyReturned } }, cashCounted });

    public static Task<HttpResponseMessage> WriteOffAsync(ApiClient caller, Guid popUpId, string reason = "Shrinkage") =>
        caller.PostAsync($"/api/pop-ups/{popUpId}/write-off", new { reason });

    public static Task<HttpResponseMessage> SellAsync(
        ApiClient caller, Guid popUpId, Guid variantId, int qty = 1, string method = "Cash", string buyerName = "Walk-in", Guid? clientRequestId = null) =>
        caller.PostAsync($"/api/pop-ups/{popUpId}/pos/sale", new
        {
            clientRequestId = clientRequestId ?? Guid.NewGuid(), buyerPersonId = (Guid?)null, buyerAttendeeId = (Guid?)null, buyerName,
            lines = new[] { new { variantId, qty } }, method, station = "Pop-up booth",
        });

    public static async Task<JsonElement> DetailAsync(ApiClient caller, Guid popUpId)
    {
        var res = await caller.GetAsync($"/api/pop-ups/{popUpId}");
        res.EnsureSuccessStatusCode();
        return await ApiClient.JsonAsync(res);
    }
}
