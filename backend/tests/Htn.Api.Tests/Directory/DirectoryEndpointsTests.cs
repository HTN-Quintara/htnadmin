using System.Net;
using Htn.Api.Tests.Infrastructure;
using Htn.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using static Htn.Api.Tests.OrgDirectory.DirectoryTestData;

namespace Htn.Api.Tests.OrgDirectory;

public sealed class DirectoryEndpointsTests(HtnApiFactory factory) : IClassFixture<HtnApiFactory>
{
    [Fact]
    public async Task Create_then_read_round_trips()
    {
        var s = await SceneAsync(factory);
        var orgId = await CreateOrgAsync(s.Board, NewOrg("Acme Corp", "Company"));

        var detail = await ApiClient.JsonAsync(await s.Board.GetAsync($"/api/directory/{orgId}"));
        Assert.Equal("Acme Corp", detail.Org().GetProperty("name").GetString());
        Assert.Equal("Company", detail.Org().GetProperty("kind").GetString());
        Assert.True(detail.Org().GetProperty("isActive").GetBoolean());
        Assert.Empty(detail.Contacts());
    }

    [Fact]
    public async Task A_duplicate_name_is_rejected()
    {
        var s = await SceneAsync(factory);
        await CreateOrgAsync(s.Board, NewOrg("Unique Org Co", "Company"));
        var res = await s.Board.PostAsync("/api/directory", NewOrg("Unique Org Co", "Company"));
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
    }

    [Fact]
    public async Task Update_cannot_change_name_or_kind()
    {
        var s = await SceneAsync(factory);
        var orgId = await CreateOrgAsync(s.Board, NewOrg("Static Name Co", "Company"));

        var res = await PatchOrgAsync(s.Board, orgId, new { addresseeSalutation = "Dear Sir/Madam", notes = "Call back next quarter" });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var detail = await ApiClient.JsonAsync(res);
        Assert.Equal("Static Name Co", detail.Org().GetProperty("name").GetString());
        Assert.Equal("Dear Sir/Madam", detail.Org().GetProperty("addresseeSalutation").GetString());
    }

    [Fact]
    public async Task Deactivate_needs_a_reason_and_is_audited()
    {
        var s = await SceneAsync(factory);
        var orgId = await CreateOrgAsync(s.Board);

        Assert.Equal(HttpStatusCode.BadRequest, (await s.Board.PostAsync($"/api/directory/{orgId}/deactivate", new { reason = "" })).StatusCode);
        var res = await s.Board.PostAsync($"/api/directory/{orgId}/deactivate", new { reason = "Org closed down" });
        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);

        var detail = await ApiClient.JsonAsync(await s.Board.GetAsync($"/api/directory/{orgId}"));
        Assert.False(detail.Org().GetProperty("isActive").GetBoolean());

        var audit = await factory.DbAsync(db => db.AuditEntries.AsNoTracking()
            .SingleAsync(a => a.Action == "directory_org.deactivate" && a.EntityId == orgId.ToString()));
        Assert.Equal("Org closed down", audit.Reason);
    }

    [Fact]
    public async Task Create_contact_and_mark_still_correct()
    {
        var s = await SceneAsync(factory);
        var orgId = await CreateOrgAsync(s.Board);
        var contactId = await CreateContactAsync(s.Board, orgId);

        var before = (await ApiClient.JsonAsync(await s.Board.GetAsync($"/api/directory/{orgId}"))).Contacts().Single();
        Assert.True(before.GetProperty("needsCheck").GetBoolean());

        var res = await s.Board.PostAsync($"/api/directory/{orgId}/contacts/{contactId}/still-correct");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var detail = await ApiClient.JsonAsync(res);
        var contact = detail.Contacts().Single();
        Assert.False(contact.GetProperty("needsCheck").GetBoolean());
        Assert.NotNull(contact.GetProperty("verifiedAt").GetString());

        var history = detail.History().Single();
        Assert.Equal("StillCorrect", history.GetProperty("kind").GetString());

        var audit = await factory.DbAsync(db => db.AuditEntries.AsNoTracking()
            .SingleAsync(a => a.Action == "directory_contact.still_correct" && a.EntityId == contactId.ToString()));
        Assert.NotNull(audit);
    }

    [Fact]
    public async Task Head_changed_preserves_the_prior_holder_in_history_and_audit()
    {
        var s = await SceneAsync(factory);
        var orgId = await CreateOrgAsync(s.Board);
        var contactId = await CreateContactAsync(s.Board, orgId, NewContact(position: "President", holder: "Alice Cruz"));

        var res = await s.Board.PostAsync($"/api/directory/{orgId}/contacts/{contactId}/head-changed", new
        {
            position = "Vice President",
            holderName = "Bob Santos",
            email = "bob.santos@example.ph",
        });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var detail = await ApiClient.JsonAsync(res);
        var contact = detail.Contacts().Single();
        Assert.Equal("Vice President", contact.GetProperty("position").GetString());
        Assert.Equal("Bob Santos", contact.GetProperty("holderName").GetString());
        Assert.False(contact.GetProperty("needsCheck").GetBoolean());

        var history = detail.History().Single();
        Assert.Equal("HolderChanged", history.GetProperty("kind").GetString());
        Assert.Equal("President", history.GetProperty("priorPosition").GetString());
        Assert.Equal("Alice Cruz", history.GetProperty("priorHolderName").GetString());

        var audit = await factory.DbAsync(db => db.AuditEntries.AsNoTracking()
            .SingleAsync(a => a.Action == "directory_contact.head_changed" && a.EntityId == contactId.ToString()));
        Assert.Contains("Alice Cruz", audit.BeforeJson);
    }

    [Fact]
    public async Task Only_one_contact_can_be_primary_at_a_time()
    {
        var s = await SceneAsync(factory);
        var orgId = await CreateOrgAsync(s.Board);
        var first = await CreateContactAsync(s.Board, orgId, NewContact(holder: "Alice Cruz", isPrimary: true));
        var second = await CreateContactAsync(s.Board, orgId, NewContact(holder: "Bob Santos", isPrimary: true));

        var detail = await ApiClient.JsonAsync(await s.Board.GetAsync($"/api/directory/{orgId}"));
        var contacts = detail.Contacts();
        Assert.Single(contacts, c => c.GetProperty("id").GetGuid() == second && c.GetProperty("isPrimary").GetBoolean());
        Assert.Single(contacts, c => c.GetProperty("id").GetGuid() == first && !c.GetProperty("isPrimary").GetBoolean());
    }

    [Fact]
    public async Task Void_contact_needs_a_reason_is_audited_and_cannot_be_voided_twice()
    {
        var s = await SceneAsync(factory);
        var orgId = await CreateOrgAsync(s.Board);
        var contactId = await CreateContactAsync(s.Board, orgId);

        Assert.Equal(HttpStatusCode.BadRequest,
            (await s.Board.PostAsync($"/api/directory/{orgId}/contacts/{contactId}/void", new { reason = "" })).StatusCode);

        var res = await s.Board.PostAsync($"/api/directory/{orgId}/contacts/{contactId}/void", new { reason = "Role discontinued" });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var detail = await ApiClient.JsonAsync(res);
        Assert.Empty(detail.Contacts()); // voided contacts drop out of the live list

        var again = await s.Board.PostAsync($"/api/directory/{orgId}/contacts/{contactId}/void", new { reason = "Again" });
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);

        var audit = await factory.DbAsync(db => db.AuditEntries.AsNoTracking()
            .SingleAsync(a => a.Action == "directory_contact.void" && a.EntityId == contactId.ToString()));
        Assert.Equal("Role discontinued", audit.Reason);
    }

    [Fact]
    public async Task Needs_check_filter_on_the_list_endpoint()
    {
        var s = await SceneAsync(factory);
        var staleOrg = await CreateOrgAsync(s.Board, NewOrg("Stale Org", "University"));
        await CreateContactAsync(s.Board, staleOrg); // never verified: needs a check

        var freshOrg = await CreateOrgAsync(s.Board, NewOrg("Fresh Org", "NonProfit"));
        var freshContact = await CreateContactAsync(s.Board, freshOrg);
        await s.Board.PostAsync($"/api/directory/{freshOrg}/contacts/{freshContact}/still-correct");

        var needsCheck = await ApiClient.JsonAsync(await s.Board.GetAsync("/api/directory?needsCheck=true"));
        var ids = needsCheck.EnumerateArray().Select(o => o.GetProperty("id").GetGuid()).ToList();
        Assert.Contains(staleOrg, ids);
        Assert.DoesNotContain(freshOrg, ids);

        var ok = await ApiClient.JsonAsync(await s.Board.GetAsync("/api/directory?needsCheck=false"));
        var okIds = ok.EnumerateArray().Select(o => o.GetProperty("id").GetGuid()).ToList();
        Assert.Contains(freshOrg, okIds);
        Assert.DoesNotContain(staleOrg, okIds);
    }
}
