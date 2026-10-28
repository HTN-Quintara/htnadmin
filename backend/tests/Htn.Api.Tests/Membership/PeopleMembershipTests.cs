using System.Net;
using Htn.Api.Tests.CheckIn;
using Htn.Api.Tests.Infrastructure;
using Htn.Domain.Finance;
using Htn.Domain.Organization;
using Htn.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using static Htn.Api.Tests.Membership.MembershipTestData;

namespace Htn.Api.Tests.Membership;

/// <summary>
/// Phase 8B: the People page's "Create Member" dialog fee choice, the attendee-eligibility search and the person
/// detail view's Membership section.
/// </summary>
public sealed class PeopleMembershipTests(HtnApiFactory factory) : IClassFixture<HtnApiFactory>
{
    private static object NewMember(string email, string? membershipFeeChoice = null) => new
    {
        firstName = "Jo", middleName = (string?)null, lastName = "Reyes", email, level = "Member", position = (string?)null,
        membershipFeeChoice,
    };

    [Fact]
    public async Task Paid_now_records_the_membership_and_posts_the_ledger_like_the_pos_path()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var mainEvent = await MainEventOnSaleAsync(board, fee: 300m);
        var email = HtnApiFactory.UniqueEmail("member");
        await factory.SeedCheckInAsync(email);

        var res = await board.PostAsync("/api/people", NewMember(email, "PaidNow"));
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var dto = await ApiClient.JsonAsync(res);
        var personId = dto.GetProperty("id").GetGuid();

        var membership = await factory.DbAsync(db => db.Memberships.AsNoTracking().SingleAsync(m => m.PersonId == personId));
        Assert.Equal(300m, membership.FeePaid);
        Assert.Equal(MembershipPaidVia.RecordedManually, membership.PaidVia);

        var ledger = await factory.DbAsync(db => db.LedgerEntries.AsNoTracking()
            .SingleAsync(e => e.Source == LedgerSource.MembershipPayment && e.SourceId == membership.Id));
        Assert.Equal(LedgerStatus.Posted, ledger.Status);
        Assert.Equal(LedgerCategories.MembershipDuesId, ledger.CategoryId);
    }

    [Fact]
    public async Task Not_paid_yet_creates_no_membership_row()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        await MainEventOnSaleAsync(board, fee: 300m);
        var email = HtnApiFactory.UniqueEmail("member");
        await factory.SeedCheckInAsync(email);

        var res = await board.PostAsync("/api/people", NewMember(email, "NotPaidYet"));
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var personId = (await ApiClient.JsonAsync(res)).GetProperty("id").GetGuid();

        var any = await factory.DbAsync(db => db.Memberships.AsNoTracking().AnyAsync(m => m.PersonId == personId));
        Assert.False(any);
    }

    [Fact]
    public async Task Paid_at_event_also_creates_no_membership_row()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        await MainEventOnSaleAsync(board, fee: 300m);
        var email = HtnApiFactory.UniqueEmail("member");
        await factory.SeedCheckInAsync(email);

        var res = await board.PostAsync("/api/people", NewMember(email, "PaidAtEvent"));
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var personId = (await ApiClient.JsonAsync(res)).GetProperty("id").GetGuid();

        var any = await factory.DbAsync(db => db.Memberships.AsNoTracking().AnyAsync(m => m.PersonId == personId));
        Assert.False(any);
    }

    [Fact]
    public async Task Person_detail_shows_the_membership_section_paid_and_unpaid()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var mainEvent = await MainEventOnSaleAsync(board, fee: 300m);

        var paidEmail = HtnApiFactory.UniqueEmail("paidmember");
        await factory.SeedCheckInAsync(paidEmail);
        var paidId = (await ApiClient.JsonAsync(await board.PostAsync("/api/people", NewMember(paidEmail, "PaidNow"))))
            .GetProperty("id").GetGuid();

        var unpaidEmail = HtnApiFactory.UniqueEmail("unpaidmember");
        await factory.SeedCheckInAsync(unpaidEmail);
        var unpaidId = (await ApiClient.JsonAsync(await board.PostAsync("/api/people", NewMember(unpaidEmail, "NotPaidYet"))))
            .GetProperty("id").GetGuid();

        var paidDetail = await ApiClient.JsonAsync(await board.GetAsync($"/api/people/{paidId}"));
        var paidMembership = paidDetail.GetProperty("membership");
        Assert.True(paidMembership.GetProperty("paid").GetBoolean());
        Assert.Equal(300m, paidMembership.GetProperty("feePaid").GetDecimal());

        var unpaidDetail = await ApiClient.JsonAsync(await board.GetAsync($"/api/people/{unpaidId}"));
        var unpaidMembership = unpaidDetail.GetProperty("membership");
        Assert.False(unpaidMembership.GetProperty("paid").GetBoolean());
        Assert.False(string.IsNullOrEmpty(unpaidMembership.GetProperty("eventName").GetString()));
    }

    [Fact]
    public async Task Eligible_attendees_search_reports_checked_in_and_not_already_a_person()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var mainEvent = await MainEventOnSaleAsync(board);
        var eligible = await AttendeeAsync(factory, board, mainEvent, eligible: true, email: HtnApiFactory.UniqueEmail("eligibleana"));
        var ineligible = await AttendeeAsync(factory, board, mainEvent, eligible: false, email: HtnApiFactory.UniqueEmail("ineligibleana"));

        var res = await board.GetAsync("/api/people/eligible-attendees?q=ana");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var hits = await ApiClient.JsonAsync(res);
        var byEmail = hits.EnumerateArray().ToDictionary(h => h.GetProperty("email").GetString()!, h => h);

        Assert.True(byEmail[eligible.Email].GetProperty("eligible").GetBoolean());
        Assert.False(byEmail[ineligible.Email].GetProperty("eligible").GetBoolean());
    }

    [Fact]
    public async Task Goon_cannot_use_the_membership_endpoints()
    {
        var goon = await factory.SignedInAsync(PersonLevel.Goon);
        Assert.Equal(HttpStatusCode.Forbidden, (await goon.GetAsync("/api/people/eligible-attendees?q=ana")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await goon.PostAsync("/api/people", NewMember(HtnApiFactory.UniqueEmail("x"), "PaidNow"))).StatusCode);
    }

    [Fact]
    public async Task Anonymous_is_401()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.NewClient().GetAsync("/api/people/eligible-attendees?q=ana")).StatusCode);
    }
}
