using System.Net;
using System.Net.Http;
using System.Text.Json;
using Htn.Api.Tests.Infrastructure;
using Htn.Domain.Organization;
using static Htn.Api.Tests.Committees.CommitteeTestData;
using static Htn.Api.Tests.Events.EventTestData;

namespace Htn.Api.Tests.OrgDirectory;

/// <summary>
/// The cast for the Phase 7A directory slice: a board member (manage + view), a committee lead (view only — the
/// policy under test), a plain goon who leads nothing (denied everything) and an apprentice.
/// </summary>
internal sealed record DirectoryScene(
    ApiClient Board, ApiClient CommitteeLead, ApiClient Goon, ApiClient Apprentice, Guid EventId, Guid CommitteeId);

internal static class DirectoryTestData
{
    public static async Task<DirectoryScene> SceneAsync(HtnApiFactory factory)
    {
        var (_, board) = await SignedInUserAsync(factory, PersonLevel.Board);
        var (leadUser, lead) = await SignedInUserAsync(factory, PersonLevel.Goon);
        var (_, goon) = await SignedInUserAsync(factory, PersonLevel.Goon);
        var (_, apprentice) = await SignedInUserAsync(factory, PersonLevel.Apprentice);
        var eventId = await OnSaleAsync(board);
        var committeeId = await AddCommitteeAsync(board, eventId);
        await SetLeadAsync(board, eventId, committeeId, leadUser.PersonId);
        return new DirectoryScene(board, lead, goon, apprentice, eventId, committeeId);
    }

    public static object NewOrg(string? name = null, string? kind = "Company") => new
    {
        kind,
        name = name ?? "CloudCorp " + Guid.NewGuid().ToString("N")[..8],
        websiteUrl = "https://cloudcorp.ph",
    };

    public static async Task<Guid> CreateOrgAsync(ApiClient board, object? body = null)
    {
        var res = await board.PostAsync("/api/directory", body ?? NewOrg());
        if (res.StatusCode != HttpStatusCode.OK)
            throw new InvalidOperationException($"Create org failed: {(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
        return (await ApiClient.JsonAsync(res)).GetProperty("org").GetProperty("id").GetGuid();
    }

    public static object NewContact(string position = "President", string holder = "Alice Cruz", string? email = null, bool isPrimary = true) => new
    {
        position,
        holderName = holder,
        email = email ?? $"{holder.Replace(" ", ".").ToLowerInvariant()}@example.ph",
        isPrimary,
    };

    public static async Task<Guid> CreateContactAsync(ApiClient board, Guid orgId, object? body = null)
    {
        var request = body ?? NewContact();
        var email = (string)request.GetType().GetProperty("email")!.GetValue(request)!;
        var res = await board.PostAsync($"/api/directory/{orgId}/contacts", request);
        if (res.StatusCode != HttpStatusCode.OK)
            throw new InvalidOperationException($"Create contact failed: {(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
        var detail = await ApiClient.JsonAsync(res);
        // Contacts are ordered primary-first then by position, so "last" isn't reliably the one just created:
        // match by the email the caller used instead.
        return detail.GetProperty("contacts").EnumerateArray()
            .Single(c => c.GetProperty("email").GetString() == email).GetProperty("id").GetGuid();
    }

    public static Task<HttpResponseMessage> PatchOrgAsync(ApiClient client, Guid orgId, object body) =>
        client.SendAsync(HttpMethod.Patch, $"/api/directory/{orgId}", body, true);

    public static Task<HttpResponseMessage> PatchContactAsync(ApiClient client, Guid orgId, Guid contactId, object body) =>
        client.SendAsync(HttpMethod.Patch, $"/api/directory/{orgId}/contacts/{contactId}", body, true);

    public static JsonElement Org(this JsonElement detail) => detail.GetProperty("org");

    public static List<JsonElement> Contacts(this JsonElement detail) => [.. detail.GetProperty("contacts").EnumerateArray()];

    public static List<JsonElement> History(this JsonElement detail) => [.. detail.GetProperty("history").EnumerateArray()];
}
