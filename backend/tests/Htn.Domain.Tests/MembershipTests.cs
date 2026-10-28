using Htn.Domain.Common;
using Htn.Domain.Organization;

namespace Htn.Domain.Tests;

public sealed class MembershipTests
{
    private static readonly DateTimeOffset PaidAt = new(2026, 11, 22, 8, 0, 0, TimeSpan.Zero);
    private static readonly Guid PersonId = Guid.CreateVersion7(PaidAt);
    private static readonly Guid EventId = Guid.CreateVersion7(PaidAt);
    private static readonly Guid RecordedBy = Guid.CreateVersion7(PaidAt);

    private static Membership NewMembership(DateTimeOffset? validUntil = null) =>
        Membership.Create(PersonId, 2026, 300m, PaidAt, MembershipPaidVia.Pos, EventId, RecordedBy,
            validUntil ?? PaidAt.AddYears(1), PaidAt);

    [Fact]
    public void Create_sets_every_field()
    {
        var m = NewMembership();
        Assert.Equal(PersonId, m.PersonId);
        Assert.Equal(2026, m.MembershipYear);
        Assert.Equal(300m, m.FeePaid);
        Assert.Equal(PaidAt, m.PaidAt);
        Assert.Equal(MembershipPaidVia.Pos, m.PaidVia);
        Assert.Equal(EventId, m.EventId);
        Assert.Equal(RecordedBy, m.RecordedById);
        Assert.Equal(7, m.Id.Version);
    }

    [Fact]
    public void Create_rounds_the_fee_to_two_decimals()
    {
        var m = Membership.Create(PersonId, 2026, 300.005m, PaidAt, MembershipPaidVia.Pos, EventId, RecordedBy, PaidAt.AddYears(1), PaidAt);
        Assert.Equal(300.00m, m.FeePaid);
    }

    [Fact]
    public void Create_rejects_an_empty_person_id() =>
        Assert.Throws<DomainException>(() =>
            Membership.Create(Guid.Empty, 2026, 300m, PaidAt, MembershipPaidVia.Pos, EventId, RecordedBy, PaidAt.AddYears(1), PaidAt));

    [Fact]
    public void Create_rejects_a_year_outside_the_configured_range() =>
        Assert.Throws<DomainException>(() =>
            Membership.Create(PersonId, 1999, 300m, PaidAt, MembershipPaidVia.Pos, EventId, RecordedBy, PaidAt.AddYears(1), PaidAt));

    [Fact]
    public void Create_rejects_a_negative_fee() =>
        Assert.Throws<DomainException>(() =>
            Membership.Create(PersonId, 2026, -1m, PaidAt, MembershipPaidVia.Pos, EventId, RecordedBy, PaidAt.AddYears(1), PaidAt));

    [Fact]
    public void Create_rejects_an_empty_recorded_by() =>
        Assert.Throws<DomainException>(() =>
            Membership.Create(PersonId, 2026, 300m, PaidAt, MembershipPaidVia.Pos, EventId, Guid.Empty, PaidAt.AddYears(1), PaidAt));

    [Fact]
    public void Create_rejects_a_valid_until_on_or_before_the_payment_date() =>
        Assert.Throws<DomainException>(() =>
            Membership.Create(PersonId, 2026, 300m, PaidAt, MembershipPaidVia.Pos, EventId, RecordedBy, PaidAt, PaidAt));

    [Fact]
    public void Create_allows_a_null_event_for_a_manual_record_with_no_event_tie()
    {
        var m = Membership.Create(PersonId, 2026, 300m, PaidAt, MembershipPaidVia.RecordedManually, null, RecordedBy, PaidAt.AddYears(1), PaidAt);
        Assert.Null(m.EventId);
        Assert.Equal(MembershipPaidVia.RecordedManually, m.PaidVia);
    }

    [Fact]
    public void There_is_no_update_method_on_the_entity()
    {
        // Membership exposes only private setters and Create: there is no domain method that could ever rewrite a
        // row once it exists — a renewal is a new row for the next membership year. The database half of this
        // guarantee (Membership_Guards) is exercised by the API-level integrity-guard tests against a real
        // Postgres trigger, mirroring MerchSaleTests' equivalent assertion about MerchSaleLine.
        var methods = typeof(Membership).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Where(m => m.DeclaringType == typeof(Membership) && !m.IsSpecialName)
            .ToList();
        Assert.Empty(methods);
    }
}
