using System.Net;
using Htn.Api.Tests.Events;
using Htn.Api.Tests.Infrastructure;
using Htn.Api.Tests.Registrations;
using Htn.Domain.Finance;
using Htn.Domain.Organization;
using Htn.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using static Htn.Api.Tests.Membership.MembershipTestData;
using static Htn.Api.Tests.Merch.MerchTestData;

namespace Htn.Api.Tests.Membership;

/// <summary>
/// Phase 8B: the "Membership 2026" POS tile on <c>POST /pos/sale</c> — promoting an eligible Attendee to a Member,
/// writing the Membership row and posting the ledger entry, all in the same transaction as the sale.
/// </summary>
public sealed class MembershipPosSaleTests(HtnApiFactory factory) : IClassFixture<HtnApiFactory>
{
    [Fact]
    public async Task A_membership_only_sale_creates_the_person_the_membership_row_and_posts_the_ledger()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var mainEvent = await MainEventOnSaleAsync(board, fee: 300m);
        var attendee = await AttendeeAsync(factory, board, mainEvent, eligible: true);

        var res = await board.PostAsync(SaleUrl(mainEvent.Id),
            SaleBody(Guid.NewGuid(), buyerAttendeeId: attendee.AttendeeId, includesMembership: true));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var dto = await ApiClient.JsonAsync(res);
        Assert.True(dto.GetProperty("membershipFeeIncluded").GetBoolean());
        Assert.Equal(300m, dto.GetProperty("membershipFeeAmount").GetDecimal());
        Assert.Equal(300m, dto.GetProperty("total").GetDecimal());
        Assert.Equal(0m, dto.GetProperty("subtotal").GetDecimal());

        var email = Person.NormalizeEmail(attendee.Email);
        var person = await factory.DbAsync(db => db.People.AsNoTracking().SingleAsync(p => p.Email == email));
        Assert.Equal(PersonLevel.Member, person.Level);

        var membership = await factory.DbAsync(db => db.Memberships.AsNoTracking().SingleAsync(m => m.PersonId == person.Id));
        Assert.Equal(300m, membership.FeePaid);
        Assert.Equal(MembershipPaidVia.Pos, membership.PaidVia);
        Assert.Equal(mainEvent.Id, membership.EventId);

        var ledger = await factory.DbAsync(db => db.LedgerEntries.AsNoTracking()
            .SingleAsync(e => e.Source == LedgerSource.MembershipPayment && e.SourceId == membership.Id));
        Assert.Equal(LedgerStatus.Posted, ledger.Status);
        Assert.Equal(300m, ledger.Amount);
        Assert.Equal(LedgerCategories.MembershipDuesId, ledger.CategoryId);
    }

    [Fact]
    public async Task Membership_and_merch_in_the_same_cart_adds_the_undiscounted_fee_on_top_of_the_discounted_merch()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var mainEvent = await MainEventOnSaleAsync(board, fee: 300m);
        var itemId = await CreateItemAsync(board, mainEvent.Id, NewItem(price: 100m));
        var variantId = await CreateVariantAsync(board, mainEvent.Id, itemId);
        var attendee = await AttendeeAsync(factory, board, mainEvent, eligible: true);

        var res = await board.PostAsync(SaleUrl(mainEvent.Id),
            SaleBody(Guid.NewGuid(), buyerAttendeeId: attendee.AttendeeId,
                lines: [new { variantId, qty = 1 }], includesMembership: true));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var dto = await ApiClient.JsonAsync(res);
        // The Attendee always pays full merch price (no discount), and membership rides on top, also undiscounted.
        Assert.Equal(100m, dto.GetProperty("subtotal").GetDecimal());
        Assert.Equal(0m, dto.GetProperty("discountAmount").GetDecimal());
        Assert.Equal(400m, dto.GetProperty("total").GetDecimal());
    }

    [Fact]
    public async Task A_buyer_who_already_is_a_person_is_refused_with_already_a_member()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var mainEvent = await MainEventOnSaleAsync(board);
        var existing = await factory.CreateUserAsync(PersonLevel.Goon);

        var res = await board.PostAsync(SaleUrl(mainEvent.Id),
            SaleBody(Guid.NewGuid(), buyerPersonId: existing.PersonId, includesMembership: true));
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        var body = await ApiClient.JsonAsync(res);
        Assert.Equal("already_a_member", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task An_attendee_who_never_checked_in_anywhere_is_refused_with_not_checked_in()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var mainEvent = await MainEventOnSaleAsync(board);
        var attendee = await AttendeeAsync(factory, board, mainEvent, eligible: false);

        var res = await board.PostAsync(SaleUrl(mainEvent.Id),
            SaleBody(Guid.NewGuid(), buyerAttendeeId: attendee.AttendeeId, includesMembership: true));
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        var body = await ApiClient.JsonAsync(res);
        Assert.Equal("not_checked_in", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_sale_with_no_resolvable_buyer_cant_buy_membership()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var mainEvent = await MainEventOnSaleAsync(board);

        var res = await board.PostAsync(SaleUrl(mainEvent.Id),
            SaleBody(Guid.NewGuid(), buyerName: "Walk-in Juan", includesMembership: true));
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        var body = await ApiClient.JsonAsync(res);
        Assert.Equal("buyer_required_for_membership", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Membership_cant_be_sold_at_an_event_that_isnt_the_main_event()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var ordinary = await EventTestData.OnSaleAsync(board);
        var attendee = await AttendeeAsync(factory, board,
            new OnSaleEvent(ordinary, "", await FirstTicketIdAsync(factory, ordinary)), eligible: true);

        var res = await board.PostAsync(SaleUrl(ordinary),
            SaleBody(Guid.NewGuid(), buyerAttendeeId: attendee.AttendeeId, includesMembership: true));
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        var body = await ApiClient.JsonAsync(res);
        Assert.Equal("membership_not_available", body.GetProperty("code").GetString());
    }

    private static Task<Guid> FirstTicketIdAsync(HtnApiFactory factory, Guid eventId) =>
        factory.DbAsync(db => db.TicketTypes.AsNoTracking().Where(t => t.EventId == eventId).Select(t => t.Id).SingleAsync());
}
