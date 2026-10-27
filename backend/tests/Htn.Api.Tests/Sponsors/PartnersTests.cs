using System.Net;
using Htn.Api.Features.Sponsors;
using Htn.Api.Tests.Infrastructure;
using static Htn.Api.Tests.Sponsors.SponsorsTestData;

namespace Htn.Api.Tests.Sponsors;

public sealed class PartnersTests(HtnApiFactory factory) : IClassFixture<HtnApiFactory>
{
    [Fact]
    public async Task Settings_default_then_save()
    {
        var s = await SceneAsync(factory);
        var page = await ApiClient.JsonAsync(await s.Board.GetAsync($"/api/events/{s.EventId}/partners"));
        Assert.Equal(20, page.GetProperty("settings").GetProperty("threshold").GetInt32());

        var save = await s.Board.PutAsync($"/api/events/{s.EventId}/partners/settings",
            new { threshold = 30, countBasis = "AllRegistrants", groupLinkEnabled = true });
        save.EnsureSuccessStatusCode();
        var after = await ApiClient.JsonAsync(save);
        Assert.Equal(30, after.GetProperty("settings").GetProperty("threshold").GetInt32());
        Assert.True(after.GetProperty("settings").GetProperty("groupLinkEnabled").GetBoolean());
    }

    [Fact]
    public async Task Threshold_must_move_in_steps_of_five()
    {
        var s = await SceneAsync(factory);
        var bad = await s.Board.PutAsync($"/api/events/{s.EventId}/partners/settings",
            new { threshold = 22, countBasis = "PaidOnly", groupLinkEnabled = false });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
    }

    [Fact]
    public async Task Tagging_an_org_makes_it_a_partner_in_the_list()
    {
        var s = await SceneAsync(factory);
        var name = "DICT " + Guid.NewGuid().ToString("N")[..6];
        var tag = await s.Board.PostAsync($"/api/events/{s.EventId}/partners/tag", new { name, orgType = "Government agency" });
        tag.EnsureSuccessStatusCode();
        var page = await ApiClient.JsonAsync(tag);
        var mine = page.GetProperty("partners").EnumerateArray()
            .FirstOrDefault(p => p.GetProperty("displayName").GetString() == name);
        Assert.True(mine.ValueKind == System.Text.Json.JsonValueKind.Object, "the tagged org should appear as a partner");
        Assert.True(mine.GetProperty("isTagged").GetBoolean());
    }

    [Fact]
    public async Task Tagging_the_same_org_twice_is_refused()
    {
        var s = await SceneAsync(factory);
        var name = "Accenture " + Guid.NewGuid().ToString("N")[..6];
        (await s.Board.PostAsync($"/api/events/{s.EventId}/partners/tag", new { name })).EnsureSuccessStatusCode();
        var dup = await s.Board.PostAsync($"/api/events/{s.EventId}/partners/tag", new { name });
        Assert.Equal(HttpStatusCode.Conflict, dup.StatusCode);
    }

    [Fact]
    public async Task A_group_link_is_refused_before_the_threshold_is_reached()
    {
        var s = await SceneAsync(factory);
        (await s.Board.PutAsync($"/api/events/{s.EventId}/partners/settings",
            new { threshold = 20, countBasis = "PaidOnly", groupLinkEnabled = true })).EnsureSuccessStatusCode();
        var name = "Startup " + Guid.NewGuid().ToString("N")[..6];
        var page = await ApiClient.JsonAsync(await s.Board.PostAsync($"/api/events/{s.EventId}/partners/tag", new { name }));
        var pid = page.GetProperty("partners").EnumerateArray()
            .First(p => p.GetProperty("displayName").GetString() == name).GetProperty("id").GetGuid();

        var link = await s.Board.PostAsync($"/api/events/{s.EventId}/partners/{pid}/group-link");
        Assert.Equal(HttpStatusCode.Conflict, link.StatusCode);
        Assert.Equal(SponsorErrorCodes.ThresholdNotReached, (await ApiClient.JsonAsync(link)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_goon_cannot_change_partner_settings()
    {
        var s = await SceneAsync(factory);
        var res = await s.Goon.PutAsync($"/api/events/{s.EventId}/partners/settings",
            new { threshold = 25, countBasis = "PaidOnly", groupLinkEnabled = false });
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }
}
