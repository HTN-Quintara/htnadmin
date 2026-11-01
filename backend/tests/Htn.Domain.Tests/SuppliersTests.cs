using Htn.Domain.Common;
using Htn.Domain.Suppliers;

namespace Htn.Domain.Tests;

public class SupplierTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 8, 0, 0, TimeSpan.Zero);
    private static readonly Guid Actor = Guid.CreateVersion7(Now);

    private static Supplier New(string name = "Acme Catering") =>
        Supplier.Create(new NewSupplier(SupplierCategory.Catering, name, "Jo", "jo@acme.ph", null, true, "123", "BDO 1", null), Actor, Now);

    [Fact]
    public void Create_derives_the_name_key()
    {
        var s = New("Acme  Catering!!");
        Assert.Equal("acme catering", s.NameKey);
    }

    [Fact]
    public void Two_suppliers_with_equivalent_names_share_a_name_key()
    {
        var a = New("Manila Catering Co.");
        var b = New("manila   catering co");
        Assert.Equal(a.NameKey, b.NameKey);
    }

    [Fact]
    public void Update_never_touches_category_or_name()
    {
        var s = New();
        s.Update(new UpdateSupplier("New Contact", "new@acme.ph", null, false, null, null, "notes"), Actor, Now);
        Assert.Equal(SupplierCategory.Catering, s.Category);
        Assert.Equal("Acme Catering", s.Name);
        Assert.Equal("New Contact", s.ContactName);
        Assert.False(s.IssuesOr);
    }

    [Fact]
    public void Updating_a_deactivated_supplier_is_refused()
    {
        var s = New();
        s.Deactivate("closed down", Actor, Now);
        Assert.Throws<DomainException>(() => s.Update(new UpdateSupplier(null, null, null, true, null, null, null), Actor, Now));
    }

    [Fact]
    public void Deactivating_twice_is_refused()
    {
        var s = New();
        s.Deactivate("closed down", Actor, Now);
        Assert.Throws<DomainException>(() => s.Deactivate("again", Actor, Now));
    }
}

public class SupplierDocumentTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 8, 0, 0, TimeSpan.Zero);
    private static readonly Guid Actor = Guid.CreateVersion7(Now);
    private static readonly Guid SupplierId = Guid.CreateVersion7(Now);
    private static readonly Guid FileId = Guid.CreateVersion7(Now);

    [Fact]
    public void Voiding_twice_is_refused()
    {
        var doc = SupplierDocument.Create(SupplierId, SupplierDocumentKind.Bir2303, FileId, Actor, Now);
        doc.Void("replaced", Actor, Now);
        Assert.Throws<DomainException>(() => doc.Void("again", Actor, Now));
    }
}

public class EventSupplierTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 8, 0, 0, TimeSpan.Zero);
    private static readonly Guid Actor = Guid.CreateVersion7(Now);
    private static readonly Guid EventId = Guid.CreateVersion7(Now);
    private static readonly Guid SupplierId = Guid.CreateVersion7(Now);
    private static readonly Guid CategoryId = Guid.CreateVersion7(Now);
    private static readonly Guid CommitteeId = Guid.CreateVersion7(Now);

    private static EventSupplier New() =>
        EventSupplier.Create(EventId, new NewEventSupplierHire(SupplierId, CategoryId, CommitteeId, "note"), Actor, Now);

    [Fact]
    public void A_new_hire_starts_at_quotation()
    {
        var h = New();
        Assert.Equal(SupplierHireStatus.Quotation, h.Status);
        Assert.True(h.IsQuotation);
    }

    [Theory]
    [InlineData(SupplierHireStatus.Quotation, SupplierHireStatus.Booked, true)]
    [InlineData(SupplierHireStatus.Booked, SupplierHireStatus.DepositPaid, true)]
    [InlineData(SupplierHireStatus.DepositPaid, SupplierHireStatus.FullyPaid, true)]
    [InlineData(SupplierHireStatus.Quotation, SupplierHireStatus.DepositPaid, false)]
    [InlineData(SupplierHireStatus.Quotation, SupplierHireStatus.FullyPaid, false)]
    [InlineData(SupplierHireStatus.Booked, SupplierHireStatus.FullyPaid, false)]
    [InlineData(SupplierHireStatus.Booked, SupplierHireStatus.Quotation, false)]
    [InlineData(SupplierHireStatus.DepositPaid, SupplierHireStatus.Booked, false)]
    [InlineData(SupplierHireStatus.FullyPaid, SupplierHireStatus.DepositPaid, false)]
    [InlineData(SupplierHireStatus.FullyPaid, SupplierHireStatus.Quotation, false)]
    public void The_whitelist_is_forward_only_one_step_at_a_time(SupplierHireStatus from, SupplierHireStatus to, bool allowed)
    {
        var h = New();
        // Walk to `from` via only known-good forward moves.
        var path = from switch
        {
            SupplierHireStatus.Quotation => Array.Empty<SupplierHireStatus>(),
            SupplierHireStatus.Booked => [SupplierHireStatus.Booked],
            SupplierHireStatus.DepositPaid => [SupplierHireStatus.Booked, SupplierHireStatus.DepositPaid],
            SupplierHireStatus.FullyPaid => [SupplierHireStatus.Booked, SupplierHireStatus.DepositPaid, SupplierHireStatus.FullyPaid],
            _ => throw new ArgumentOutOfRangeException(nameof(from)),
        };
        foreach (var step in path) h.Advance(step, Actor, Now);

        if (allowed)
        {
            h.Advance(to, Actor, Now);
            Assert.Equal(to, h.Status);
        }
        else
        {
            Assert.Throws<DomainException>(() => h.Advance(to, Actor, Now));
        }
    }

    [Fact]
    public void Advancing_a_voided_hire_is_refused()
    {
        var h = New();
        h.Void("fell through", Actor, Now);
        Assert.Throws<DomainException>(() => h.Advance(SupplierHireStatus.Booked, Actor, Now));
    }

    [Fact]
    public void Voiding_twice_is_refused()
    {
        var h = New();
        h.Void("fell through", Actor, Now);
        Assert.Throws<DomainException>(() => h.Void("again", Actor, Now));
    }

    [Fact]
    public void Update_rejects_negative_money()
    {
        var h = New();
        Assert.Throws<DomainException>(() =>
            h.Update(new UpdateEventSupplierHire(-1m, null, null, null, null), Actor, Now));
    }

    [Fact]
    public void Update_accepts_zero_paid_amount()
    {
        var h = New();
        h.Update(new UpdateEventSupplierHire(10_000m, 0m, null, null, null), Actor, Now);
        Assert.Equal(0m, h.PaidAmount);
    }
}

public class SupplierRatingTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 8, 0, 0, TimeSpan.Zero);
    private static readonly Guid HireId = Guid.CreateVersion7(Now);
    private static readonly Guid RaterId = Guid.CreateVersion7(Now);

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public void Stars_outside_1_to_5_are_refused(int stars)
    {
        Assert.Throws<DomainException>(() =>
            SupplierRating.Create(HireId, RaterId, new NewSupplierRating(stars, null, HireAgain.Yes, null), Now));
    }

    [Fact]
    public void Hire_again_requires_a_defined_value()
    {
        Assert.Throws<DomainException>(() =>
            SupplierRating.Create(HireId, RaterId, new NewSupplierRating(5, null, (HireAgain)99, null), Now));
    }

    [Fact]
    public void Tags_are_joined_with_semicolons_and_deduplicated()
    {
        var rating = SupplierRating.Create(HireId, RaterId,
            new NewSupplierRating(4, [SupplierRatingTags.OnTime, SupplierRatingTags.FairPrice, SupplierRatingTags.OnTime], HireAgain.Maybe, null), Now);
        Assert.Equal("OnTime;FairPrice", rating.Tags);
    }

    [Fact]
    public void An_unknown_tag_is_refused()
    {
        Assert.Throws<DomainException>(() =>
            SupplierRating.Create(HireId, RaterId, new NewSupplierRating(3, ["NotARealTag"], HireAgain.No, null), Now));
    }

    [Fact]
    public void No_tags_leaves_tags_null()
    {
        var rating = SupplierRating.Create(HireId, RaterId, new NewSupplierRating(5, null, HireAgain.Yes, "Great!"), Now);
        Assert.Null(rating.Tags);
        Assert.Equal("Great!", rating.Note);
    }
}
