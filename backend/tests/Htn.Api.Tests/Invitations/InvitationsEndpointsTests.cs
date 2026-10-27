using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Htn.Api.Tests.Infrastructure;
using Htn.Api.Tests.OrgDirectory;
using static Htn.Api.Tests.Invitations.InvitationsTestData;

namespace Htn.Api.Tests.Invitations;

/// <summary>
/// Invitations.Manage is board-only with no lead/view carve-out: every route gets one 403 for a goon, a committee
/// lead and an apprentice, plus one 401 for anonymous.
/// </summary>
public sealed class InvitationsAccessTests(HtnApiFactory factory) : IClassFixture<HtnApiFactory>
{
    [Fact]
    public async Task Anonymous_gets_401()
    {
        var s = await SceneAsync(factory);
        var anon = factory.NewClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync(InvitationsUrl(s.EventId))).StatusCode);
    }

    [Theory]
    [MemberData(nameof(NonBoardRoles))]
    public async Task A_non_board_role_is_denied_every_route(string role)
    {
        var s = await SceneAsync(factory);
        var client = role switch
        {
            "lead" => s.CommitteeLead,
            "goon" => s.Goon,
            "apprentice" => s.Apprentice,
            _ => throw new ArgumentOutOfRangeException(nameof(role)),
        };
        var (orgId, _) = await VerifiedOrgAsync(s.Board);
        var invitationId = await CreateInvitationAsync(s.Board, s.EventId, orgId);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(InvitationsUrl(s.EventId))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"{InvitationsUrl(s.EventId)}/template")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsync($"{InvitationsUrl(s.EventId)}/template", new { subject = "x", body = "y" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync(InvitationsUrl(s.EventId), new { orgIds = new[] { orgId } })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync($"{InvitationsUrl(s.EventId)}/{invitationId}/preview")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync($"{InvitationsUrl(s.EventId)}/{invitationId}/send")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync($"{InvitationsUrl(s.EventId)}/{invitationId}/status", new { status = "Opened" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.DeleteAsync($"{InvitationsUrl(s.EventId)}/{invitationId}")).StatusCode);
    }

    public static IEnumerable<object[]> NonBoardRoles() => [["lead"], ["goon"], ["apprentice"]];

    [Fact]
    public async Task A_board_member_can_use_every_route()
    {
        var s = await SceneAsync(factory);
        await SetTemplateAsync(s.Board, s.EventId);
        Assert.Equal(HttpStatusCode.OK, (await s.Board.GetAsync(InvitationsUrl(s.EventId))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await s.Board.GetAsync($"{InvitationsUrl(s.EventId)}/template")).StatusCode);
    }
}

public sealed class InvitationsPickListTests(HtnApiFactory factory) : IClassFixture<HtnApiFactory>
{
    [Fact]
    public async Task Creating_skips_orgs_already_invited_without_conflicting()
    {
        var s = await SceneAsync(factory);
        var (orgA, _) = await VerifiedOrgAsync(s.Board);
        var (orgB, _) = await VerifiedOrgAsync(s.Board);
        await CreateInvitationAsync(s.Board, s.EventId, orgA);

        var res = await s.Board.PostAsync(InvitationsUrl(s.EventId), new { orgIds = new[] { orgA, orgB } });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var doc = await ApiClient.JsonAsync(res);
        var created = doc.GetProperty("created").EnumerateArray().Select(e => e.GetGuid()).ToList();
        var skipped = doc.GetProperty("skipped").EnumerateArray().Select(e => e.GetGuid()).ToList();
        Assert.Contains(orgB, created);
        Assert.Contains(orgA, skipped);
    }

    [Fact]
    public async Task Deleting_is_only_allowed_while_not_sent()
    {
        var s = await SceneAsync(factory);
        var (orgId, _) = await VerifiedOrgAsync(s.Board);
        var invitationId = await CreateInvitationAsync(s.Board, s.EventId, orgId);

        // Not yet sent: delete succeeds (undoing a pick-list mistake).
        var delete = await s.Board.DeleteAsync($"{InvitationsUrl(s.EventId)}/{invitationId}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        // Recreate, send, then deleting is refused.
        var invitationId2 = await CreateInvitationAsync(s.Board, s.EventId, orgId);
        await SetTemplateAsync(s.Board, s.EventId);
        var send = await s.Board.PostAsync($"{InvitationsUrl(s.EventId)}/{invitationId2}/send");
        Assert.Equal(HttpStatusCode.OK, send.StatusCode);

        var deleteAfterSend = await s.Board.DeleteAsync($"{InvitationsUrl(s.EventId)}/{invitationId2}");
        Assert.Equal(HttpStatusCode.Conflict, deleteAfterSend.StatusCode);
    }
}

public sealed class InvitationsSendTests(HtnApiFactory factory) : IClassFixture<HtnApiFactory>
{
    [Fact]
    public async Task Preview_does_not_mutate_state()
    {
        var s = await SceneAsync(factory);
        var (orgId, _) = await VerifiedOrgAsync(s.Board);
        var invitationId = await CreateInvitationAsync(s.Board, s.EventId, orgId);
        await SetTemplateAsync(s.Board, s.EventId);

        var preview1 = await s.Board.PostAsync($"{InvitationsUrl(s.EventId)}/{invitationId}/preview");
        Assert.Equal(HttpStatusCode.OK, preview1.StatusCode);
        var body1 = await ApiClient.JsonAsync(preview1);
        Assert.Contains("Please join us", body1.GetProperty("body").GetString());

        // Status is still NotSent after preview; sending still works afterwards.
        var list = await (await s.Board.GetAsync(InvitationsUrl(s.EventId))).Content.ReadFromJsonAsync<JsonElement>();
        var row = list.EnumerateArray().Single(i => i.GetProperty("id").GetGuid() == invitationId);
        Assert.Equal("NotSent", row.GetProperty("status").GetString());

        var send = await s.Board.PostAsync($"{InvitationsUrl(s.EventId)}/{invitationId}/send");
        Assert.Equal(HttpStatusCode.OK, send.StatusCode);
    }

    [Fact]
    public async Task Sending_with_an_unverified_contact_is_refused_with_409()
    {
        var s = await SceneAsync(factory);
        var orgId = await DirectoryTestData.CreateOrgAsync(s.Board);
        await DirectoryTestData.CreateContactAsync(s.Board, orgId); // never confirmed "still correct"
        var invitationId = await CreateInvitationAsync(s.Board, s.EventId, orgId);
        await SetTemplateAsync(s.Board, s.EventId);

        var send = await s.Board.PostAsync($"{InvitationsUrl(s.EventId)}/{invitationId}/send");
        Assert.Equal(HttpStatusCode.Conflict, send.StatusCode);
        var body = await ApiClient.JsonAsync(send);
        Assert.Equal("contact_not_verified", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Sending_with_a_freshly_verified_contact_succeeds_and_enqueues_exactly_one_email()
    {
        var s = await SceneAsync(factory);
        var (orgId, _) = await VerifiedOrgAsync(s.Board);
        var invitationId = await CreateInvitationAsync(s.Board, s.EventId, orgId);
        await SetTemplateAsync(s.Board, s.EventId);

        var send = await s.Board.PostAsync($"{InvitationsUrl(s.EventId)}/{invitationId}/send");
        Assert.Equal(HttpStatusCode.OK, send.StatusCode);
        var detail = await ApiClient.JsonAsync(send);
        Assert.Equal("Sent", detail.GetProperty("status").GetString());
        Assert.NotNull(detail.GetProperty("letterSubjectSnapshot").GetString());

        await factory.ProcessOutboxAsync();
        var mails = factory.Emails.Sent.Where(m => m.Category == "invitation-letter").ToList();
        Assert.Single(mails);

        // Sending again is refused.
        var sendAgain = await s.Board.PostAsync($"{InvitationsUrl(s.EventId)}/{invitationId}/send");
        Assert.Equal(HttpStatusCode.Conflict, sendAgain.StatusCode);
    }
}

public sealed class InvitationsStatusTests(HtnApiFactory factory) : IClassFixture<HtnApiFactory>
{
    private static async Task<Guid> SentInvitationAsync(InvitationsScene s)
    {
        var (orgId, _) = await VerifiedOrgAsync(s.Board);
        var invitationId = await CreateInvitationAsync(s.Board, s.EventId, orgId);
        await SetTemplateAsync(s.Board, s.EventId);
        var send = await s.Board.PostAsync($"{InvitationsUrl(s.EventId)}/{invitationId}/send");
        Assert.Equal(HttpStatusCode.OK, send.StatusCode);
        return invitationId;
    }

    [Fact]
    public async Task Status_advances_through_valid_transitions()
    {
        var s = await SceneAsync(factory);
        var invitationId = await SentInvitationAsync(s);

        var opened = await s.Board.PostAsync($"{InvitationsUrl(s.EventId)}/{invitationId}/status", new { status = "Opened" });
        Assert.Equal(HttpStatusCode.OK, opened.StatusCode);
        var confirmed = await s.Board.PostAsync($"{InvitationsUrl(s.EventId)}/{invitationId}/status", new { status = "Confirmed" });
        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        Assert.Equal("Confirmed", (await ApiClient.JsonAsync(confirmed)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task An_invalid_transition_is_rejected_with_409()
    {
        var s = await SceneAsync(factory);
        var invitationId = await SentInvitationAsync(s);

        await s.Board.PostAsync($"{InvitationsUrl(s.EventId)}/{invitationId}/status", new { status = "Confirmed" });
        var backToOpened = await s.Board.PostAsync($"{InvitationsUrl(s.EventId)}/{invitationId}/status", new { status = "Opened" });
        Assert.Equal(HttpStatusCode.Conflict, backToOpened.StatusCode);
        var code = (await ApiClient.JsonAsync(backToOpened)).GetProperty("code").GetString();
        Assert.Equal("invalid_transition", code);
    }

    [Fact]
    public async Task Bounced_re_flags_the_organizations_contact()
    {
        var s = await SceneAsync(factory);
        var (orgId, contactId) = await VerifiedOrgAsync(s.Board);
        var invitationId = await CreateInvitationAsync(s.Board, s.EventId, orgId);
        await SetTemplateAsync(s.Board, s.EventId);
        await s.Board.PostAsync($"{InvitationsUrl(s.EventId)}/{invitationId}/send");

        var bounced = await s.Board.PostAsync($"{InvitationsUrl(s.EventId)}/{invitationId}/status", new { status = "Bounced" });
        Assert.Equal(HttpStatusCode.OK, bounced.StatusCode);

        var detail = await ApiClient.JsonAsync(await s.Board.GetAsync($"/api/directory/{orgId}"));
        var contact = detail.GetProperty("contacts").EnumerateArray()
            .Single(c => c.GetProperty("id").GetGuid() == contactId);
        Assert.True(contact.GetProperty("needsCheck").GetBoolean());
        Assert.True(contact.GetProperty("verifiedAt").ValueKind is JsonValueKind.Null);

        var history = detail.GetProperty("history").EnumerateArray();
        Assert.Contains(history, h => h.GetProperty("kind").GetString() == "Bounced");

        // The org now shows up under needsCheck=true.
        var needsCheck = await ApiClient.JsonAsync(await s.Board.GetAsync("/api/directory?needsCheck=true"));
        Assert.Contains(needsCheck.EnumerateArray(), o => o.GetProperty("id").GetGuid() == orgId);

        // And it can be cleared back to Sent to retry.
        var retried = await s.Board.PostAsync($"{InvitationsUrl(s.EventId)}/{invitationId}/status", new { status = "Sent" });
        Assert.Equal(HttpStatusCode.OK, retried.StatusCode);
    }
}
