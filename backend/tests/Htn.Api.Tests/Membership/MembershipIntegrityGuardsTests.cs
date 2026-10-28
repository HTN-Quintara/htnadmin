using Htn.Api.Tests.Infrastructure;
using Htn.Domain.Organization;
using Microsoft.EntityFrameworkCore;
using static Htn.Api.Tests.Membership.MembershipTestData;
using static Htn.Api.Tests.Merch.MerchTestData;

namespace Htn.Api.Tests.Membership;

/// <summary>The database half of "membership is fully immutable once written" — mirrors <c>MerchIntegrityGuardsTests</c>.</summary>
public sealed class MembershipIntegrityGuardsTests(HtnApiFactory factory) : IClassFixture<HtnApiFactory>
{
    private Task<int> SqlAsync(FormattableString sql) => factory.DbAsync(db => db.Database.ExecuteSqlInterpolatedAsync(sql));

    private async Task AssertRejectedAsync(FormattableString sql, string? message = null)
    {
        var ex = await Assert.ThrowsAnyAsync<Exception>(() => SqlAsync(sql));
        if (message is not null) Assert.Contains(message, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_membership_row_is_never_updated_or_deleted_at_the_database()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var mainEvent = await MainEventOnSaleAsync(board);
        var attendee = await AttendeeAsync(factory, board, mainEvent, eligible: true);

        var res = await board.PostAsync(SaleUrl(mainEvent.Id),
            SaleBody(Guid.NewGuid(), buyerAttendeeId: attendee.AttendeeId, includesMembership: true));
        var sale = await ApiClient.JsonAsync(res);
        var personId = await factory.DbAsync(db => db.People.AsNoTracking()
            .Where(p => p.Email == Person.NormalizeEmail(attendee.Email)).Select(p => p.Id).SingleAsync());
        var membershipId = await factory.DbAsync(db => db.Memberships.AsNoTracking()
            .Where(m => m.PersonId == personId).Select(m => m.Id).SingleAsync());

        await AssertRejectedAsync($"UPDATE memberships SET fee_paid = 0 WHERE id = {membershipId}", "append-only");
        await AssertRejectedAsync($"DELETE FROM memberships WHERE id = {membershipId}", "append-only");
    }
}
