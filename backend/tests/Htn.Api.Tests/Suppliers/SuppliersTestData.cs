using System.Net;
using System.Net.Http;
using System.Text.Json;
using Htn.Api.Tests.Infrastructure;
using Htn.Domain.Finance;
using Htn.Domain.Organization;
using Microsoft.EntityFrameworkCore;
using static Htn.Api.Tests.Committees.CommitteeTestData;
using static Htn.Api.Tests.Events.EventTestData;

namespace Htn.Api.Tests.Suppliers;

/// <summary>
/// The cast for the Phase 7C suppliers slice: a board member (manage + view + rate anything), a committee lead who
/// leads the hire's owning committee (view + rate that hire), another committee lead who leads a sibling committee
/// (view, but denied rating the first lead's hire), a plain goon who leads nothing (denied everything) and an
/// apprentice.
/// </summary>
internal sealed record SuppliersScene(
    ApiClient Board, ApiClient CommitteeLead, ApiClient OtherLead, ApiClient Goon, ApiClient Apprentice,
    Guid EventId, Guid CommitteeId, Guid OtherCommitteeId);

internal static class SuppliersTestData
{
    public static async Task<SuppliersScene> SceneAsync(HtnApiFactory factory)
    {
        var (_, board) = await SignedInUserAsync(factory, PersonLevel.Board);
        var (leadUser, lead) = await SignedInUserAsync(factory, PersonLevel.Goon);
        var (otherLeadUser, otherLead) = await SignedInUserAsync(factory, PersonLevel.Goon);
        var (_, goon) = await SignedInUserAsync(factory, PersonLevel.Goon);
        var (_, apprentice) = await SignedInUserAsync(factory, PersonLevel.Apprentice);
        var eventId = await OnSaleAsync(board);
        var committeeId = await AddCommitteeAsync(board, eventId, "Logistics");
        await SetLeadAsync(board, eventId, committeeId, leadUser.PersonId);
        var otherCommitteeId = await AddCommitteeAsync(board, eventId, "Marketing");
        await SetLeadAsync(board, eventId, otherCommitteeId, otherLeadUser.PersonId);
        return new SuppliersScene(board, lead, otherLead, goon, apprentice, eventId, committeeId, otherCommitteeId);
    }

    public static object NewSupplier(string? name = null, string category = "Venue") => new
    {
        category,
        name = name ?? "Supplier " + Guid.NewGuid().ToString("N")[..8],
        contactName = "Contact Person",
        contactEmail = "contact@example.ph",
        contactPhone = "+639171234567",
        issuesOr = true,
        tin = "123-456-789",
        bankDetails = "BDO 001-234-567",
        notes = "Some notes.",
    };

    public static async Task<Guid> CreateSupplierAsync(ApiClient board, object? body = null)
    {
        var res = await board.PostAsync("/api/suppliers", body ?? NewSupplier());
        if (res.StatusCode != HttpStatusCode.OK)
            throw new InvalidOperationException($"Create supplier failed: {(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
        return (await ApiClient.JsonAsync(res)).GetProperty("id").GetGuid();
    }

    public static string EventSuppliersUrl(Guid eventId) => $"/api/events/{eventId}/suppliers";

    public static async Task<Guid> HireAsync(ApiClient board, Guid eventId, Guid supplierId, Guid committeeId, string? note = null)
    {
        var res = await board.PostAsync(EventSuppliersUrl(eventId),
            new { supplierId, categoryId = LedgerCategories.WriteOffId, ownerCommitteeId = committeeId, note });
        if (res.StatusCode != HttpStatusCode.OK)
            throw new InvalidOperationException($"Hire failed: {(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
        return (await ApiClient.JsonAsync(res)).GetProperty("id").GetGuid();
    }

    public static Task<HttpResponseMessage> PatchHireAsync(ApiClient client, Guid eventId, Guid hireId, object body) =>
        client.SendAsync(HttpMethod.Patch, $"{EventSuppliersUrl(eventId)}/{hireId}", body, true);

    public static Task<HttpResponseMessage> RateAsync(ApiClient client, Guid eventId, Guid hireId, object? body = null) =>
        client.PostAsync($"{EventSuppliersUrl(eventId)}/{hireId}/ratings",
            body ?? new { stars = 5, tags = new[] { "OnTime" }, hireAgain = "Yes", note = "Great to work with." });

    /// <summary>Pushes the event's end date into the past, so post-event-only flows (rating) are reachable.</summary>
    public static Task EndEventAsync(HtnApiFactory factory, Guid eventId) => factory.DbAsync(async db =>
    {
        var ev = await db.Events.FirstAsync(e => e.Id == eventId);
        var type = typeof(Htn.Domain.Events.Event);
        type.GetProperty("StartsAt")!.SetValue(ev, DateTimeOffset.UtcNow.AddDays(-2));
        type.GetProperty("EndsAt")!.SetValue(ev, DateTimeOffset.UtcNow.AddDays(-1));
        await db.SaveChangesAsync();
        return 0;
    });
}
