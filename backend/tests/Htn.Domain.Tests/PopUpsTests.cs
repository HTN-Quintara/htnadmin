using Htn.Domain.Common;
using Htn.Domain.PopUps;
using Xunit;

namespace Htn.Domain.Tests;

public class PopUpStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 8, 0, 0, TimeSpan.Zero);
    private static readonly Guid Committee = Guid.CreateVersion7(Now);
    private static readonly Guid Actor = Guid.CreateVersion7(Now);

    private static NewPopUpStore Valid(
        Guid? venueOrgId = null, string? venueText = "Mall activity area", PopUpDiscountMode mode = PopUpDiscountMode.None,
        int? goon = null, int? apprentice = null, int? member = null, int? board = null) =>
        new("Holiday pop-up", venueOrgId, venueText, Now.AddDays(1), Now.AddDays(2), 500m, mode, goon, apprentice, member, board, Committee);

    [Fact]
    public void Create_requires_exactly_one_venue()
    {
        Assert.Throws<DomainException>(() => PopUpStore.Create(Valid(venueOrgId: Guid.CreateVersion7(Now), venueText: "Mall"), Actor, Now));
        Assert.Throws<DomainException>(() => PopUpStore.Create(Valid(venueOrgId: null, venueText: null), Actor, Now));
    }

    [Fact]
    public void Create_requires_end_after_start()
    {
        var data = new NewPopUpStore("Pop-up", null, "Mall", Now.AddDays(2), Now.AddDays(1), 500m, PopUpDiscountMode.None,
            null, null, null, null, Committee);
        Assert.Throws<DomainException>(() => PopUpStore.Create(data, Actor, Now));
    }

    [Fact]
    public void Custom_discount_mode_requires_all_four_percentages()
    {
        Assert.Throws<DomainException>(() => PopUpStore.Create(Valid(mode: PopUpDiscountMode.Custom), Actor, Now));
        Assert.Throws<DomainException>(() => PopUpStore.Create(
            Valid(mode: PopUpDiscountMode.None, goon: 10), Actor, Now));
    }

    [Fact]
    public void Custom_mode_with_all_four_percentages_succeeds()
    {
        var p = PopUpStore.Create(Valid(mode: PopUpDiscountMode.Custom, goon: 10, apprentice: 5, member: 0, board: 0), Actor, Now);
        Assert.Equal(PopUpDiscountMode.Custom, p.DiscountMode);
        Assert.Equal(10, p.GoonPct);
    }

    [Fact]
    public void Status_moves_forward_only_one_step_at_a_time()
    {
        var p = PopUpStore.Create(Valid(), Actor, Now);
        Assert.Equal(PopUpStatus.Planned, p.Status);

        // Can't skip ahead.
        Assert.Throws<DomainException>(() => p.Close(Actor, Now));

        p.Open(Actor, Now);
        Assert.Equal(PopUpStatus.Open, p.Status);

        // No backward move.
        Assert.Throws<DomainException>(() => p.Open(Actor, Now));

        p.Close(Actor, Now);
        Assert.Equal(PopUpStatus.NeedsCount, p.Status);

        p.CompleteCount(0m, 1000m, Actor, Now);
        Assert.Equal(PopUpStatus.ClosedBalanced, p.Status);

        // Closed is final: never opens again, even indirectly.
        Assert.Throws<DomainException>(() => p.Open(Actor, Now));
    }

    [Fact]
    public void Count_with_missing_value_closes_as_missing_else_balanced()
    {
        var missing = PopUpStore.Create(Valid(), Actor, Now);
        missing.Open(Actor, Now);
        missing.Close(Actor, Now);
        missing.CompleteCount(150m, 800m, Actor, Now);
        Assert.Equal(PopUpStatus.ClosedMissing, missing.Status);
        Assert.Equal(150m, missing.MissingValue);

        var balanced = PopUpStore.Create(Valid(), Actor, Now);
        balanced.Open(Actor, Now);
        balanced.Close(Actor, Now);
        balanced.CompleteCount(0m, 800m, Actor, Now);
        Assert.Equal(PopUpStatus.ClosedBalanced, balanced.Status);
    }

    [Fact]
    public void Write_off_requires_closed_missing_and_only_once()
    {
        var p = PopUpStore.Create(Valid(), Actor, Now);
        Assert.Throws<DomainException>(() => p.MarkWrittenOff("reason", Actor, Now));

        p.Open(Actor, Now);
        p.Close(Actor, Now);
        p.CompleteCount(50m, 500m, Actor, Now);
        p.MarkWrittenOff("Shrinkage", Actor, Now);
        Assert.True(p.IsWrittenOff);
        Assert.Throws<DomainException>(() => p.MarkWrittenOff("Again", Actor, Now));
    }
}

public class PopUpAllocationTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 8, 0, 0, TimeSpan.Zero);
    private static readonly Guid PopUpId = Guid.CreateVersion7(Now);
    private static readonly Guid VariantId = Guid.CreateVersion7(Now);

    [Fact]
    public void Create_requires_a_positive_quantity()
    {
        Assert.Throws<DomainException>(() => PopUpAllocation.Create(PopUpId, VariantId, 0, Now));
    }

    [Fact]
    public void Returned_quantity_cannot_exceed_brought_and_can_only_be_set_once()
    {
        var a = PopUpAllocation.Create(PopUpId, VariantId, 10, Now);
        Assert.Throws<DomainException>(() => a.RecordReturned(11));
        Assert.Throws<DomainException>(() => a.RecordReturned(-1));

        a.RecordReturned(3);
        Assert.Equal(3, a.QtyReturned);
        Assert.Throws<DomainException>(() => a.RecordReturned(4));
    }
}
