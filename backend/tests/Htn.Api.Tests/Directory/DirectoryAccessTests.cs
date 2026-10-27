using System.Net;
using Htn.Api.Tests.Infrastructure;
using static Htn.Api.Tests.OrgDirectory.DirectoryTestData;

namespace Htn.Api.Tests.OrgDirectory;

/// <summary>
/// Directory.Manage is board-only; Directory.View is board or any committee lead, anywhere — this is the policy
/// composition most likely to be wrong, so both directions get an explicit test.
/// </summary>
public sealed class DirectoryAccessTests(HtnApiFactory factory) : IClassFixture<HtnApiFactory>
{
    [Fact]
    public async Task A_committee_lead_can_read_the_directory_but_not_write_it()
    {
        var s = await SceneAsync(factory);
        var orgId = await CreateOrgAsync(s.Board);

        Assert.Equal(HttpStatusCode.OK, (await s.CommitteeLead.GetAsync("/api/directory")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await s.CommitteeLead.GetAsync($"/api/directory/{orgId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await s.CommitteeLead.PostAsync("/api/directory", NewOrg())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await PatchOrgAsync(s.CommitteeLead, orgId, new { notes = "nope" })).StatusCode);
    }

    [Fact]
    public async Task A_plain_goon_who_leads_nothing_cannot_read_the_directory()
    {
        var s = await SceneAsync(factory);
        Assert.Equal(HttpStatusCode.Forbidden, (await s.Goon.GetAsync("/api/directory")).StatusCode);
    }

    [Fact]
    public async Task An_apprentice_cannot_read_or_write_the_directory()
    {
        var s = await SceneAsync(factory);
        Assert.Equal(HttpStatusCode.Forbidden, (await s.Apprentice.GetAsync("/api/directory")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await s.Apprentice.PostAsync("/api/directory", NewOrg())).StatusCode);
    }

    [Fact]
    public async Task A_board_member_can_read_and_write_the_directory()
    {
        var s = await SceneAsync(factory);
        Assert.Equal(HttpStatusCode.OK, (await s.Board.GetAsync("/api/directory")).StatusCode);
        var orgId = await CreateOrgAsync(s.Board);
        Assert.Equal(HttpStatusCode.OK, (await s.Board.GetAsync($"/api/directory/{orgId}")).StatusCode);
    }

    [Fact]
    public async Task Anonymous_gets_401()
    {
        var anon = factory.NewClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/api/directory")).StatusCode);
    }
}
