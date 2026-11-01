using System.Net;
using Htn.Api.Tests.Infrastructure;
using Htn.Api.Tests.OrgDirectory;
using static Htn.Api.Tests.Invitations.InvitationsTestData;

namespace Htn.Api.Tests.Invitations;

/// <summary>
/// The invitation letter email renders the merged, admin-entered template text and directory data as HTML-encoded
/// content, never raw markup — a stored-XSS-adjacent check, since both the template body and an org/contact's
/// holder name are attacker-reachable (an admin account, or compromised directory data) ending up in an email.
/// </summary>
public sealed class InvitationLetterEmailTests(HtnApiFactory factory) : IClassFixture<HtnApiFactory>
{
    [Fact]
    public async Task A_script_tag_in_the_contact_name_is_html_encoded_not_executed()
    {
        var s = await SceneAsync(factory);
        var orgId = await DirectoryTestData.CreateOrgAsync(s.Board);
        var contactId = await DirectoryTestData.CreateContactAsync(
            s.Board, orgId, DirectoryTestData.NewContact(holder: "<script>alert(1)</script> Cruz"));
        var stillCorrect = await s.Board.PostAsync($"/api/directory/{orgId}/contacts/{contactId}/still-correct");
        Assert.Equal(HttpStatusCode.OK, stillCorrect.StatusCode);

        var invitationId = await CreateInvitationAsync(s.Board, s.EventId, orgId);
        await SetTemplateAsync(s.Board, s.EventId,
            subject: "Invitation for [date]",
            body: "Dear [addressee.name],\n\nPlease join us.");
        var send = await s.Board.PostAsync($"{InvitationsUrl(s.EventId)}/{invitationId}/send");
        Assert.Equal(HttpStatusCode.OK, send.StatusCode);

        await factory.ProcessOutboxAsync();
        var mail = factory.Emails.Sent.Last(m => m.Category == "invitation-letter");

        Assert.DoesNotContain("<script>alert(1)</script>", mail.Html, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", mail.Html, StringComparison.Ordinal);
        // The plain-text alternative carries the raw text — fine, since it isn't rendered as markup.
        Assert.Contains("<script>alert(1)</script>", mail.Text, StringComparison.Ordinal);
    }
}
