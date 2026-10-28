using System.Net;
using Htn.Api.Tests.CheckIn;
using Htn.Api.Tests.Infrastructure;
using Htn.Api.Tests.Registrations;
using static Htn.Api.Tests.Events.EventTestData;

namespace Htn.Api.Tests.Membership;

internal static class MembershipTestData
{
    // Every test that needs a main event needs its own membership year — "one main event per membership year" is a
    // global uniqueness rule, not scoped to one event — so this ticks up for every call across the whole test run
    // rather than picking at random, which would risk two tests colliding on the same year once there are enough of them.
    private static int _nextYear = 2000;

    /// <summary>An on-sale main event with a membership fee/year configured and one ticket, for Phase 8B's POS membership tile.</summary>
    public static async Task<OnSaleEvent> MainEventOnSaleAsync(ApiClient board, decimal fee = 300m, int? year = null)
    {
        var ev = await CreateAsync(board);
        var id = ev.Id();
        var ticket = await AddTicketAsync(board, id);
        var put = await board.PutAsync($"/api/events/{id}", new
        {
            name = ev.GetProperty("name").GetString(), slug = (string?)null, startsAt = Start, endsAt = Start.AddHours(10),
            venue = "SMX Convention Center, Pasay", capacity = 400, description = "Talks and workshops.",
            manualRegistration = (bool?)null, groupRegistration = (bool?)null, publicPage = (bool?)null, proofMode = (string?)null,
            isMainEvent = true, membershipFee = fee, membershipYear = year ?? System.Threading.Interlocked.Increment(ref _nextYear),
        });
        if (put.StatusCode != HttpStatusCode.OK)
            throw new InvalidOperationException($"Main-event update failed: {(int)put.StatusCode} {await put.Content.ReadAsStringAsync()}");
        var publish = await board.PostAsync($"/api/events/{id}/publish");
        if (publish.StatusCode != HttpStatusCode.OK)
            throw new InvalidOperationException($"Publish failed: {(int)publish.StatusCode} {await publish.Content.ReadAsStringAsync()}");
        return new OnSaleEvent(id, ev.GetProperty("slug").GetString()!, ticket.Id());
    }

    /// <summary>
    /// A paid attendee registered at <paramref name="mainEvent"/> who has <b>not</b> checked in there, but — when
    /// <paramref name="eligible"/> — has checked in at a different, seeded event under the same email (MemberRule
    /// reads check-ins across every event, not just this one).
    /// </summary>
    public static async Task<TicketHolder> AttendeeAsync(
        HtnApiFactory factory, ApiClient board, OnSaleEvent mainEvent, bool eligible, string? email = null)
    {
        email ??= HtnApiFactory.UniqueEmail("attendee");
        var holder = await CheckInTestData.PaidAsync(factory, board, mainEvent, email);
        if (eligible) await factory.SeedCheckInAsync(email);
        return holder;
    }

    public static object SaleBody(
        Guid clientRequestId, Guid? buyerPersonId = null, Guid? buyerAttendeeId = null, string? buyerName = null,
        object[]? lines = null, bool includesMembership = false, string method = "Cash") => new
    {
        clientRequestId, buyerPersonId, buyerAttendeeId, buyerName, lines = lines ?? [], method, station = "Booth A",
        includesMembership,
    };
}
