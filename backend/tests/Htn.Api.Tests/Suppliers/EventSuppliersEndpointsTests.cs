using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Htn.Api.Tests.Infrastructure;
using static Htn.Api.Tests.Suppliers.SuppliersTestData;

namespace Htn.Api.Tests.Suppliers;

/// <summary>The hire: board-only writes, board-or-any-lead reads, money stripped from the list/detail for non-board.</summary>
public sealed class EventSupplierHireTests(HtnApiFactory factory) : IClassFixture<HtnApiFactory>
{
    [Fact]
    public async Task A_goon_who_leads_nothing_is_denied_every_write()
    {
        var s = await SceneAsync(factory);
        var supplierId = await CreateSupplierAsync(s.Board);

        Assert.Equal(HttpStatusCode.Forbidden,
            (await s.Goon.PostAsync(EventSuppliersUrl(s.EventId), new { supplierId, categoryId = Htn.Domain.Finance.LedgerCategories.WriteOffId, ownerCommitteeId = s.CommitteeId })).StatusCode);

        var hireId = await HireAsync(s.Board, s.EventId, supplierId, s.CommitteeId);
        Assert.Equal(HttpStatusCode.Forbidden, (await PatchHireAsync(s.Goon, s.EventId, hireId, new { contractAmount = 1000m })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await s.Goon.PostAsync($"{EventSuppliersUrl(s.EventId)}/{hireId}/void", new { reason = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await s.Goon.DeleteAsync($"{EventSuppliersUrl(s.EventId)}/{hireId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await s.Goon.GetAsync(EventSuppliersUrl(s.EventId))).StatusCode);
    }

    [Fact]
    public async Task Money_fields_are_absent_for_a_committee_lead_but_present_for_the_board()
    {
        var s = await SceneAsync(factory);
        var supplierId = await CreateSupplierAsync(s.Board);
        var hireId = await HireAsync(s.Board, s.EventId, supplierId, s.CommitteeId);
        await PatchHireAsync(s.Board, s.EventId, hireId, new { contractAmount = 50_000m, paidAmount = 10_000m });

        var leadList = await ApiClient.JsonAsync(await s.CommitteeLead.GetAsync(EventSuppliersUrl(s.EventId)));
        var leadRow = leadList.EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == hireId);
        Assert.False(leadRow.TryGetProperty("money", out _), "money must be entirely absent for a non-board reader.");

        var boardList = await ApiClient.JsonAsync(await s.Board.GetAsync(EventSuppliersUrl(s.EventId)));
        var boardRow = boardList.EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == hireId);
        Assert.True(boardRow.TryGetProperty("money", out var money));
        Assert.Equal(50_000m, money.GetProperty("contractAmount").GetDecimal());
    }

    [Fact]
    public async Task Hiring_the_same_supplier_twice_while_live_is_refused_with_409()
    {
        var s = await SceneAsync(factory);
        var supplierId = await CreateSupplierAsync(s.Board);
        await HireAsync(s.Board, s.EventId, supplierId, s.CommitteeId);

        var again = await s.Board.PostAsync(EventSuppliersUrl(s.EventId),
            new { supplierId, categoryId = Htn.Domain.Finance.LedgerCategories.WriteOffId, ownerCommitteeId = s.CommitteeId });
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("already_hired", (await ApiClient.JsonAsync(again)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Status_only_moves_forward_one_step_at_a_time()
    {
        var s = await SceneAsync(factory);
        var supplierId = await CreateSupplierAsync(s.Board);
        var hireId = await HireAsync(s.Board, s.EventId, supplierId, s.CommitteeId);

        // Quotation -> DepositPaid (skipping Booked) is refused.
        var skip = await PatchHireAsync(s.Board, s.EventId, hireId, new { status = "DepositPaid" });
        Assert.Equal(HttpStatusCode.Conflict, skip.StatusCode);
        Assert.Equal("invalid_transition", (await ApiClient.JsonAsync(skip)).GetProperty("code").GetString());

        var toBooked = await PatchHireAsync(s.Board, s.EventId, hireId, new { status = "Booked" });
        Assert.Equal(HttpStatusCode.OK, toBooked.StatusCode);
        Assert.Equal("Booked", (await ApiClient.JsonAsync(toBooked)).GetProperty("status").GetString());

        // Backward is refused too.
        var backward = await PatchHireAsync(s.Board, s.EventId, hireId, new { status = "Quotation" });
        Assert.Equal(HttpStatusCode.Conflict, backward.StatusCode);
    }

    [Fact]
    public async Task Delete_is_only_allowed_while_quotation()
    {
        var s = await SceneAsync(factory);
        var supplierId = await CreateSupplierAsync(s.Board);
        var hireId = await HireAsync(s.Board, s.EventId, supplierId, s.CommitteeId);

        await PatchHireAsync(s.Board, s.EventId, hireId, new { status = "Booked" });
        var denied = await s.Board.DeleteAsync($"{EventSuppliersUrl(s.EventId)}/{hireId}");
        Assert.Equal(HttpStatusCode.Conflict, denied.StatusCode);
        Assert.Equal("not_quotation", (await ApiClient.JsonAsync(denied)).GetProperty("code").GetString());

        var quotationHireId = await HireAsync(s.Board, s.EventId, await CreateSupplierAsync(s.Board), s.CommitteeId);
        var ok = await s.Board.DeleteAsync($"{EventSuppliersUrl(s.EventId)}/{quotationHireId}");
        Assert.Equal(HttpStatusCode.NoContent, ok.StatusCode);
    }

    [Fact]
    public async Task Voiding_frees_up_the_supplier_to_be_rehired()
    {
        var s = await SceneAsync(factory);
        var supplierId = await CreateSupplierAsync(s.Board);
        var hireId = await HireAsync(s.Board, s.EventId, supplierId, s.CommitteeId);

        var voided = await s.Board.PostAsync($"{EventSuppliersUrl(s.EventId)}/{hireId}/void", new { reason = "fell through" });
        Assert.Equal(HttpStatusCode.OK, voided.StatusCode);

        var rehireId = await HireAsync(s.Board, s.EventId, supplierId, s.CommitteeId);
        Assert.NotEqual(hireId, rehireId);
    }
}

/// <summary>
/// The two-layer <c>Suppliers.Rate</c> check: a goon/apprentice who leads nothing is denied at the route; a lead of
/// a *different* committee passes the route but fails the per-hire resource check; the hire's own lead, and the
/// board, pass both.
/// </summary>
public sealed class SupplierRatingTests(HtnApiFactory factory) : IClassFixture<HtnApiFactory>
{
    private static async Task<(SuppliersScene Scene, Guid HireId)> EndedEventWithHireAsync(HtnApiFactory f)
    {
        var s = await SceneAsync(f);
        var supplierId = await CreateSupplierAsync(s.Board);
        var hireId = await HireAsync(s.Board, s.EventId, supplierId, s.CommitteeId);
        await EndEventAsync(f, s.EventId);
        return (s, hireId);
    }

    [Fact]
    public async Task A_goon_who_leads_nothing_is_denied_at_the_route()
    {
        var (s, hireId) = await EndedEventWithHireAsync(factory);
        Assert.Equal(HttpStatusCode.Forbidden, (await RateAsync(s.Goon, s.EventId, hireId)).StatusCode);
    }

    [Fact]
    public async Task An_apprentice_is_denied_at_the_route()
    {
        var (s, hireId) = await EndedEventWithHireAsync(factory);
        Assert.Equal(HttpStatusCode.Forbidden, (await RateAsync(s.Apprentice, s.EventId, hireId)).StatusCode);
    }

    [Fact]
    public async Task A_lead_of_a_different_committee_passes_the_route_but_fails_the_resource_check()
    {
        var (s, hireId) = await EndedEventWithHireAsync(factory);
        var denied = await RateAsync(s.OtherLead, s.EventId, hireId);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }

    [Fact]
    public async Task The_hires_own_committee_lead_can_rate_it()
    {
        var (s, hireId) = await EndedEventWithHireAsync(factory);
        var rated = await RateAsync(s.CommitteeLead, s.EventId, hireId);
        Assert.Equal(HttpStatusCode.OK, rated.StatusCode);
        var body = await ApiClient.JsonAsync(rated);
        Assert.Equal(5, body.GetProperty("stars").GetInt32());
        Assert.Equal("Yes", body.GetProperty("hireAgain").GetString());
        Assert.Contains("OnTime", body.GetProperty("tags").EnumerateArray().Select(t => t.GetString()));
    }

    [Fact]
    public async Task The_board_can_rate_any_hire()
    {
        var (s, hireId) = await EndedEventWithHireAsync(factory);
        Assert.Equal(HttpStatusCode.OK, (await RateAsync(s.Board, s.EventId, hireId)).StatusCode);
    }

    [Fact]
    public async Task Rating_before_the_event_ends_is_refused_with_409()
    {
        var s = await SceneAsync(factory);
        var supplierId = await CreateSupplierAsync(s.Board);
        var hireId = await HireAsync(s.Board, s.EventId, supplierId, s.CommitteeId);

        var tooEarly = await RateAsync(s.CommitteeLead, s.EventId, hireId);
        Assert.Equal(HttpStatusCode.Conflict, tooEarly.StatusCode);
        Assert.Equal("event_not_ended", (await ApiClient.JsonAsync(tooEarly)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Rating_the_same_hire_twice_by_the_same_rater_is_refused_with_409()
    {
        var (s, hireId) = await EndedEventWithHireAsync(factory);
        Assert.Equal(HttpStatusCode.OK, (await RateAsync(s.CommitteeLead, s.EventId, hireId)).StatusCode);
        var again = await RateAsync(s.CommitteeLead, s.EventId, hireId);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("already_rated", (await ApiClient.JsonAsync(again)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Two_different_raters_both_count_towards_the_average()
    {
        var (s, hireId) = await EndedEventWithHireAsync(factory);
        await RateAsync(s.CommitteeLead, s.EventId, hireId, new { stars = 5, tags = Array.Empty<string>(), hireAgain = "Yes", note = (string?)null });
        await RateAsync(s.Board, s.EventId, hireId, new { stars = 3, tags = Array.Empty<string>(), hireAgain = "Maybe", note = (string?)null });

        var list = await ApiClient.JsonAsync(await s.Board.GetAsync(EventSuppliersUrl(s.EventId)));
        var row = list.EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == hireId);
        Assert.Equal(4.0, row.GetProperty("averageStars").GetDouble());
        Assert.Equal(2, row.GetProperty("ratingCount").GetInt32());
    }

    [Fact]
    public async Task The_to_rate_list_only_shows_the_callers_own_committees_hires_once_the_event_has_ended()
    {
        var s = await SceneAsync(factory);
        var supplierA = await CreateSupplierAsync(s.Board);
        var supplierB = await CreateSupplierAsync(s.Board);
        var ownHireId = await HireAsync(s.Board, s.EventId, supplierA, s.CommitteeId);
        var otherHireId = await HireAsync(s.Board, s.EventId, supplierB, s.OtherCommitteeId);

        var tooEarly = await ApiClient.JsonAsync(await s.CommitteeLead.GetAsync($"{EventSuppliersUrl(s.EventId)}/to-rate"));
        Assert.Empty(tooEarly.EnumerateArray());

        await EndEventAsync(factory, s.EventId);

        var leadView = await ApiClient.JsonAsync(await s.CommitteeLead.GetAsync($"{EventSuppliersUrl(s.EventId)}/to-rate"));
        var leadIds = leadView.EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).ToList();
        Assert.Contains(ownHireId, leadIds);
        Assert.DoesNotContain(otherHireId, leadIds);

        var boardView = await ApiClient.JsonAsync(await s.Board.GetAsync($"{EventSuppliersUrl(s.EventId)}/to-rate"));
        var boardIds = boardView.EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).ToList();
        Assert.Contains(ownHireId, boardIds);
        Assert.Contains(otherHireId, boardIds);
    }
}
