using System.Net;
using Htn.Api.Tests.Infrastructure;
using Htn.Domain.Finance;
using Htn.Domain.Organization;
using Microsoft.EntityFrameworkCore;
using static Htn.Api.Tests.PopUps.PopUpTestData;

namespace Htn.Api.Tests.PopUps;

/// <summary>Denial matrix: anonymous 401, a non-allowed committee's lead 403, a lead of a *different* allowed
/// committee 403 on someone else's pop-up, a store-team member can sell but not manage, board can everything.</summary>
public sealed class PopUpsAuthorizationTests(HtnApiFactory factory) : IClassFixture<HtnApiFactory>
{
    [Fact]
    public async Task Anonymous_is_unauthorized()
    {
        var s = await SceneAsync(factory);
        var popUpId = await CreatePopUpAsync(s.Board, s.OwnerCommitteeId);
        var anon = factory.NewClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.SendAsync(HttpMethod.Get, "/api/pop-ups", null, csrf: true)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.SendAsync(HttpMethod.Post, "/api/pop-ups", NewPopUp(s.OwnerCommitteeId), csrf: true)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.SendAsync(HttpMethod.Get, $"/api/pop-ups/{popUpId}", null, csrf: true)).StatusCode);
    }

    [Fact]
    public async Task A_lead_of_a_committee_not_allowed_to_run_pop_ups_is_denied_create()
    {
        var (notAllowedLeadUser, notAllowedLead) = await Committees.CommitteeTestData.SignedInUserAsync(factory, PersonLevel.Goon);
        var board = (await Committees.CommitteeTestData.SignedInUserAsync(factory, PersonLevel.Board)).Client;
        var eventId = await Events.EventTestData.OnSaleAsync(board);
        var committeeId = await Committees.CommitteeTestData.AddCommitteeAsync(board, eventId, "Partnerships");
        await Committees.CommitteeTestData.SetLeadAsync(board, eventId, committeeId, notAllowedLeadUser.PersonId);
        // Not allowed: PopUpsAllowed defaults to false.
        var res = await notAllowedLead.PostAsync("/api/pop-ups", NewPopUp(committeeId));
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task A_lead_of_a_different_allowed_committee_cannot_manage_someone_elses_pop_up()
    {
        var s = await SceneAsync(factory);
        var popUpId = await CreatePopUpAsync(s.Board, s.OwnerCommitteeId);

        // Coarse gate passes (leads something, and that something is itself PopUpsAllowed) but the precise
        // per-pop-up check must still fail: this is committee B's lead against committee A's pop-up.
        Assert.Equal(HttpStatusCode.Forbidden, (await OpenAsync(s.OtherLeadClient, popUpId)).StatusCode);

        // The owner's own lead can.
        Assert.Equal(HttpStatusCode.OK, (await OpenAsync(s.OwnerLeadClient, popUpId)).StatusCode);
    }

    [Fact]
    public async Task A_store_team_member_can_sell_but_not_manage()
    {
        var s = await SceneAsync(factory);
        var (_, variantId) = await LeftoverVariantAsync(s.Board, s.EventId);
        var popUpId = await CreatePopUpAsync(s.Board, s.OwnerCommitteeId, [s.TeamMember.PersonId]);
        await AllocateAsync(s.Board, popUpId, variantId, 5);
        await OpenAsync(s.Board, popUpId);

        Assert.Equal(HttpStatusCode.OK, (await SellAsync(s.TeamMemberClient, popUpId, variantId)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await CloseAsync(s.TeamMemberClient, popUpId)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await AllocateAsync(s.TeamMemberClient, popUpId, variantId, 1)).StatusCode);
    }

    [Fact]
    public async Task A_plain_goon_on_no_team_and_leading_nothing_is_denied_sell()
    {
        var s = await SceneAsync(factory);
        var (_, variantId) = await LeftoverVariantAsync(s.Board, s.EventId);
        var popUpId = await CreatePopUpAsync(s.Board, s.OwnerCommitteeId);
        await AllocateAsync(s.Board, popUpId, variantId, 5);
        await OpenAsync(s.Board, popUpId);

        Assert.Equal(HttpStatusCode.Forbidden, (await SellAsync(s.PlainGoon, popUpId, variantId)).StatusCode);
    }

    [Fact]
    public async Task Board_can_manage_and_sell_at_any_pop_up()
    {
        var s = await SceneAsync(factory);
        var (_, variantId) = await LeftoverVariantAsync(s.Board, s.EventId);
        var popUpId = await CreatePopUpAsync(s.Board, s.OwnerCommitteeId);
        Assert.Equal(HttpStatusCode.OK, (await AllocateAsync(s.Board, popUpId, variantId, 5)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await OpenAsync(s.Board, popUpId)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SellAsync(s.Board, popUpId, variantId)).StatusCode);
    }
}

public sealed class PopUpsAllocationTests(HtnApiFactory factory) : IClassFixture<HtnApiFactory>
{
    [Fact]
    public async Task Allocation_cannot_exceed_available_leftover_stock()
    {
        var s = await SceneAsync(factory);
        var (_, variantId) = await LeftoverVariantAsync(s.Board, s.EventId, stock: 10);
        var popUpId = await CreatePopUpAsync(s.Board, s.OwnerCommitteeId);

        var res = await AllocateAsync(s.Board, popUpId, variantId, 11);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
    }

    [Fact]
    public async Task Two_pop_ups_cannot_double_allocate_the_same_leftover_stock()
    {
        var s = await SceneAsync(factory);
        var (_, variantId) = await LeftoverVariantAsync(s.Board, s.EventId, stock: 10);
        var popUpA = await CreatePopUpAsync(s.Board, s.OwnerCommitteeId);
        var popUpB = await CreatePopUpAsync(s.Board, s.OtherCommitteeId);

        Assert.Equal(HttpStatusCode.OK, (await AllocateAsync(s.Board, popUpA, variantId, 7)).StatusCode);
        // Only 3 left for anyone else.
        Assert.Equal(HttpStatusCode.Conflict, (await AllocateAsync(s.Board, popUpB, variantId, 4)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await AllocateAsync(s.Board, popUpB, variantId, 3)).StatusCode);
    }

    [Fact]
    public async Task Two_concurrent_allocations_for_more_stock_than_exists_only_one_fully_succeeds()
    {
        var s = await SceneAsync(factory);
        // 10 on hand; each pop-up alone asks for only 7 (within the stale "10 available" either would read
        // before the other commits), but together they'd reserve 14 — proves the DB trigger's row lock
        // actually serializes concurrent allocations against the same variant, not just that a sequential
        // second call is rejected (Two_pop_ups_cannot_double_allocate_the_same_leftover_stock, above).
        var (_, variantId) = await LeftoverVariantAsync(s.Board, s.EventId, stock: 10);
        var popUpA = await CreatePopUpAsync(s.Board, s.OwnerCommitteeId);
        var popUpB = await CreatePopUpAsync(s.Board, s.OtherCommitteeId);

        var t1 = AllocateAsync(s.Board, popUpA, variantId, 7);
        var t2 = AllocateAsync(s.Board, popUpB, variantId, 7);
        var results = await Task.WhenAll(t1, t2);

        var statuses = results.Select(r => r.StatusCode).OrderBy(c => c).ToList();
        Assert.Contains(HttpStatusCode.OK, statuses);
        Assert.Contains(HttpStatusCode.Conflict, statuses);

        var reserved = await factory.DbAsync(db => db.PopUpAllocations.AsNoTracking()
            .Where(a => a.VariantId == variantId).SumAsync(a => a.QtyBrought));
        Assert.True(reserved <= 10, $"reserved {reserved} exceeded the 10 on hand");
    }

    [Fact]
    public async Task Move_to_store_nulls_event_id_and_freezes_the_source_event()
    {
        var s = await SceneAsync(factory);
        var (itemId, variantId) = await LeftoverVariantAsync(s.Board, s.EventId);

        var item = await factory.DbAsync(db => db.MerchItems.AsNoTracking().SingleAsync(i => i.Id == itemId));
        Assert.Null(item.EventId);
        Assert.Equal(s.EventId, item.LeftoverSourceEventId);

        var leftover = await s.Board.GetAsync("/api/merch/leftover-inventory");
        Assert.Equal(HttpStatusCode.OK, leftover.StatusCode);
        var body = await ApiClient.JsonAsync(leftover);
        Assert.Contains(body.EnumerateArray(), v => v.GetProperty("variantId").GetGuid() == variantId);
    }
}

public sealed class PopUpsLifecycleTests(HtnApiFactory factory) : IClassFixture<HtnApiFactory>
{
    [Fact]
    public async Task Open_close_count_moves_through_the_forward_only_statuses()
    {
        var s = await SceneAsync(factory);
        var (_, variantId) = await LeftoverVariantAsync(s.Board, s.EventId, stock: 10);
        var popUpId = await CreatePopUpAsync(s.Board, s.OwnerCommitteeId);
        await AllocateAsync(s.Board, popUpId, variantId, 10);

        // Can't close or count before opening.
        Assert.Equal(HttpStatusCode.Conflict, (await CloseAsync(s.Board, popUpId)).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await OpenAsync(s.Board, popUpId)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await OpenAsync(s.Board, popUpId)).StatusCode); // no re-opening

        await SellAsync(s.Board, popUpId, variantId, qty: 3);
        Assert.Equal(HttpStatusCode.OK, (await CloseAsync(s.Board, popUpId)).StatusCode);

        // Brought 10, sold 3, returned 5 => 2 missing.
        var counted = await CountAsync(s.Board, popUpId, variantId, qtyReturned: 5, cashCounted: 450m);
        Assert.Equal(HttpStatusCode.OK, counted.StatusCode);
        var detail = await DetailAsync(s.Board, popUpId);
        Assert.Equal("ClosedMissing", detail.GetProperty("status").GetString());
        Assert.True(detail.GetProperty("missingValue").GetDecimal() > 0);
    }

    [Fact]
    public async Task A_fully_returned_pop_up_closes_balanced()
    {
        var s = await SceneAsync(factory);
        var (_, variantId) = await LeftoverVariantAsync(s.Board, s.EventId, stock: 10);
        var popUpId = await CreatePopUpAsync(s.Board, s.OwnerCommitteeId);
        await AllocateAsync(s.Board, popUpId, variantId, 10);
        await OpenAsync(s.Board, popUpId);
        await CloseAsync(s.Board, popUpId);

        var counted = await CountAsync(s.Board, popUpId, variantId, qtyReturned: 10, cashCounted: 0m);
        Assert.Equal(HttpStatusCode.OK, counted.StatusCode);
        var detail = await DetailAsync(s.Board, popUpId);
        Assert.Equal("ClosedBalanced", detail.GetProperty("status").GetString());
        Assert.Equal(0m, detail.GetProperty("missingValue").GetDecimal());
    }
}

public sealed class PopUpsWriteOffTests(HtnApiFactory factory) : IClassFixture<HtnApiFactory>
{
    private static async Task<(PopUpScene Scene, Guid PopUpId, Guid VariantId)> ClosedMissingAsync(HtnApiFactory factory)
    {
        var s = await SceneAsync(factory);
        var (_, variantId) = await LeftoverVariantAsync(s.Board, s.EventId, stock: 10);
        var popUpId = await CreatePopUpAsync(s.Board, s.OwnerCommitteeId);
        await AllocateAsync(s.Board, popUpId, variantId, 10);
        await OpenAsync(s.Board, popUpId);
        await SellAsync(s.Board, popUpId, variantId, qty: 2);
        await CloseAsync(s.Board, popUpId);
        await CountAsync(s.Board, popUpId, variantId, qtyReturned: 3, cashCounted: 0m); // 10-2-3 = 5 missing
        return (s, popUpId, variantId);
    }

    [Fact]
    public async Task Write_off_is_cfo_only_and_posts_an_expense()
    {
        var (s, popUpId, _) = await ClosedMissingAsync(factory);

        Assert.Equal(HttpStatusCode.Forbidden, (await WriteOffAsync(s.Board, popUpId)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await WriteOffAsync(s.OwnerLeadClient, popUpId)).StatusCode);

        var res = await WriteOffAsync(s.CfoClient, popUpId, "Confirmed shrinkage");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var entries = await factory.DbAsync(db => db.LedgerEntries.AsNoTracking()
            .Where(e => e.Source == LedgerSource.PopUpWriteOff && e.SourceId == popUpId).ToListAsync());
        Assert.Single(entries);
        Assert.Equal(LedgerDirection.Out, entries[0].Direction);
        Assert.Equal(Funds.OrganizationId, entries[0].FundId);

        // Writing off again is refused.
        Assert.Equal(HttpStatusCode.Conflict, (await WriteOffAsync(s.CfoClient, popUpId)).StatusCode);
    }
}

public sealed class PopUpsSaleTests(HtnApiFactory factory) : IClassFixture<HtnApiFactory>
{
    [Fact]
    public async Task A_pop_up_sale_posts_to_the_organization_fund_specifically()
    {
        var s = await SceneAsync(factory);
        var (_, variantId) = await LeftoverVariantAsync(s.Board, s.EventId, stock: 10);
        var popUpId = await CreatePopUpAsync(s.Board, s.OwnerCommitteeId);
        await AllocateAsync(s.Board, popUpId, variantId, 10);
        await OpenAsync(s.Board, popUpId);

        var sale = await SellAsync(s.Board, popUpId, variantId, qty: 2);
        Assert.Equal(HttpStatusCode.OK, sale.StatusCode);
        var saleId = (await ApiClient.JsonAsync(sale)).GetProperty("id").GetGuid();

        var entry = await factory.DbAsync(db => db.LedgerEntries.AsNoTracking()
            .SingleAsync(e => e.Source == LedgerSource.MerchSale && e.SourceId == saleId));
        Assert.Equal(Funds.OrganizationId, entry.FundId);

        // Confirm it is NOT the event's own fund (would exist only if the event itself has had money posted).
        var eventFundId = await factory.DbAsync(db => db.Funds.AsNoTracking()
            .Where(f => f.EventId == s.EventId).Select(f => (Guid?)f.Id).SingleOrDefaultAsync());
        Assert.NotEqual(eventFundId, entry.FundId);
    }

    [Fact]
    public async Task A_pop_up_sale_decrements_the_allocation_not_just_raw_variant_stock()
    {
        var s = await SceneAsync(factory);
        var (_, variantId) = await LeftoverVariantAsync(s.Board, s.EventId, stock: 10);
        var popUpA = await CreatePopUpAsync(s.Board, s.OwnerCommitteeId);
        var popUpB = await CreatePopUpAsync(s.Board, s.OtherCommitteeId);
        await AllocateAsync(s.Board, popUpA, variantId, 6);
        await AllocateAsync(s.Board, popUpB, variantId, 4);
        await OpenAsync(s.Board, popUpA);
        await OpenAsync(s.Board, popUpB);

        // Pop-up A sells all 6 of its own allocation; it may not sell a 7th even though raw stock has more
        // (allocated to pop-up B).
        Assert.Equal(HttpStatusCode.OK, (await SellAsync(s.Board, popUpA, variantId, qty: 6)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await SellAsync(s.Board, popUpA, variantId, qty: 1)).StatusCode);

        // Pop-up B's own 4 units are untouched.
        Assert.Equal(HttpStatusCode.OK, (await SellAsync(s.Board, popUpB, variantId, qty: 4)).StatusCode);
    }
}
