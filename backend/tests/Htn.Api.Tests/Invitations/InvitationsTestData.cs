using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Htn.Api.Tests.Infrastructure;
using Htn.Domain.Organization;
using static Htn.Api.Tests.Committees.CommitteeTestData;
using static Htn.Api.Tests.Events.EventTestData;
using static Htn.Api.Tests.OrgDirectory.DirectoryTestData;

namespace Htn.Api.Tests.Invitations;

/// <summary>
/// The cast for the Phase 7B invitations slice: a board member (the only role with access), a committee lead (a
/// goon — denied, unlike Directory.View, since Invitations.Manage has no lead carve-out), a plain goon and an
/// apprentice.
/// </summary>
internal sealed record InvitationsScene(ApiClient Board, ApiClient CommitteeLead, ApiClient Goon, ApiClient Apprentice, Guid EventId);

internal static class InvitationsTestData
{
    public static async Task<InvitationsScene> SceneAsync(HtnApiFactory factory)
    {
        var (_, board) = await SignedInUserAsync(factory, PersonLevel.Board);
        var (leadUser, lead) = await SignedInUserAsync(factory, PersonLevel.Goon);
        var (_, goon) = await SignedInUserAsync(factory, PersonLevel.Goon);
        var (_, apprentice) = await SignedInUserAsync(factory, PersonLevel.Apprentice);
        var eventId = await OnSaleAsync(board);
        var committeeId = await AddCommitteeAsync(board, eventId);
        await SetLeadAsync(board, eventId, committeeId, leadUser.PersonId);
        return new InvitationsScene(board, lead, goon, apprentice, eventId);
    }

    public static string InvitationsUrl(Guid eventId) => $"/api/events/{eventId}/invitations";

    public static Task<HttpResponseMessage> SetTemplateAsync(ApiClient board, Guid eventId, string? subject = null, string? body = null) =>
        board.PutAsync($"{InvitationsUrl(eventId)}/template", new
        {
            subject = subject ?? "Invitation for [date]",
            body = body ?? "Dear [addressee.salutation] [addressee.name] ([addressee.position]),\n\nPlease join us.",
        });

    /// <summary>Creates a verified org (primary contact confirmed "still correct") ready to invite and send to.</summary>
    public static async Task<(Guid OrgId, Guid ContactId)> VerifiedOrgAsync(ApiClient board, string? name = null)
    {
        var orgId = await CreateOrgAsync(board, NewOrg(name));
        var contactId = await CreateContactAsync(board, orgId);
        var res = await board.PostAsync($"/api/directory/{orgId}/contacts/{contactId}/still-correct");
        res.EnsureSuccessStatusCode();
        return (orgId, contactId);
    }

    public static async Task<Guid> CreateInvitationAsync(ApiClient board, Guid eventId, Guid orgId)
    {
        var res = await board.PostAsync(InvitationsUrl(eventId), new { orgIds = new[] { orgId } });
        if (res.StatusCode != HttpStatusCode.OK)
            throw new InvalidOperationException($"Create invitation failed: {(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
        var doc = await ApiClient.JsonAsync(res);
        var createdOrgId = doc.GetProperty("created").EnumerateArray().Single().GetGuid();
        Assert.Equal(orgId, createdOrgId);
        var list = await (await board.GetAsync(InvitationsUrl(eventId))).Content.ReadFromJsonAsync<JsonElement>();
        return list.EnumerateArray().Single(i => i.GetProperty("directoryOrgId").GetGuid() == orgId).GetProperty("id").GetGuid();
    }
}
