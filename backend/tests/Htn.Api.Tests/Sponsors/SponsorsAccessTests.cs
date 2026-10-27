using System.Net;
using Htn.Api.Tests.Infrastructure;
using static Htn.Api.Tests.Sponsors.SponsorsTestData;

namespace Htn.Api.Tests.Sponsors;

/// <summary>Sponsors are board-only. Unlike finance, there is no hiding: a staff member who may not manage them gets 403.</summary>
public sealed class SponsorsAccessTests(HtnApiFactory factory) : IClassFixture<HtnApiFactory>
{
    [Fact]
    public async Task A_goon_cannot_read_or_write_sponsors()
    {
        var s = await SceneAsync(factory);
        Assert.Equal(HttpStatusCode.Forbidden, (await s.Goon.GetAsync($"/api/events/{s.EventId}/sponsors")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await s.Goon.PostAsync($"/api/events/{s.EventId}/sponsors", new { kind = "Organization", name = "X", cashAmount = 1m })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await s.Goon.GetAsync($"/api/events/{s.EventId}/partners")).StatusCode);
    }

    [Fact]
    public async Task An_apprentice_cannot_read_sponsors()
    {
        var s = await SceneAsync(factory);
        Assert.Equal(HttpStatusCode.Forbidden, (await s.Apprentice.GetAsync($"/api/events/{s.EventId}/sponsors")).StatusCode);
    }

    [Fact]
    public async Task Anonymous_gets_401()
    {
        var s = await SceneAsync(factory);
        var anon = factory.NewClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync($"/api/events/{s.EventId}/sponsors")).StatusCode);
    }
}
