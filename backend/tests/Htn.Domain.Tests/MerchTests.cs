using Htn.Domain.Common;
using Htn.Domain.Merch;
using Htn.Domain.Organization;

namespace Htn.Domain.Tests;

public class EventMerchSettingsTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 8, 0, 0, TimeSpan.Zero);
    private static readonly Guid EventId = Guid.CreateVersion7(Now);
    private static readonly Guid Actor = Guid.CreateVersion7(Now);

    [Fact]
    public void Default_copies_the_org_wide_defaults()
    {
        var s = EventMerchSettings.Default(EventId, 20, 15, 10, 0, Now);
        Assert.Equal(20, s.GoonPct);
        Assert.Equal(15, s.ApprenticePct);
        Assert.Equal(10, s.MemberPct);
        Assert.Equal(0, s.BoardPct);
    }

    [Theory]
    [InlineData(PersonLevel.Goon, 20)]
    [InlineData(PersonLevel.Apprentice, 15)]
    [InlineData(PersonLevel.Member, 10)]
    [InlineData(PersonLevel.Board, 0)]
    public void PctFor_resolves_by_level(PersonLevel level, int expected)
    {
        var s = EventMerchSettings.Default(EventId, 20, 15, 10, 0, Now);
        Assert.Equal(expected, s.PctFor(level));
    }

    [Fact]
    public void PctFor_an_unresolved_buyer_is_zero()
    {
        var s = EventMerchSettings.Default(EventId, 20, 15, 10, 0, Now);
        Assert.Equal(0, s.PctFor(null));
    }

    [Fact]
    public void Update_rejects_a_percent_outside_0_to_100()
    {
        var s = EventMerchSettings.Default(EventId, 20, 15, 10, 0, Now);
        Assert.Throws<DomainException>(() => s.Update(101, 15, 10, 0, Actor, Now));
        Assert.Throws<DomainException>(() => s.Update(-1, 15, 10, 0, Actor, Now));
    }

    [Fact]
    public void Update_changes_every_level()
    {
        var s = EventMerchSettings.Default(EventId, 20, 15, 10, 0, Now);
        s.Update(25, 18, 12, 5, Actor, Now);
        Assert.Equal(25, s.GoonPct);
        Assert.Equal(18, s.ApprenticePct);
        Assert.Equal(12, s.MemberPct);
        Assert.Equal(5, s.BoardPct);
        Assert.Equal(Actor, s.UpdatedById);
    }
}

public class MerchItemTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 8, 0, 0, TimeSpan.Zero);
    private static readonly Guid EventId = Guid.CreateVersion7(Now);
    private static readonly Guid Actor = Guid.CreateVersion7(Now);

    private static MerchItem New() =>
        MerchItem.Create(EventId, new NewMerchItem("Con Shirt", 350m, "#B71C1C"), Actor, Now);

    [Fact]
    public void Create_rejects_a_bad_swatch_color()
    {
        Assert.Throws<DomainException>(() => MerchItem.Create(EventId, new NewMerchItem("Shirt", 350m, "red"), Actor, Now));
    }

    [Fact]
    public void Create_rejects_negative_price()
    {
        Assert.Throws<DomainException>(() => MerchItem.Create(EventId, new NewMerchItem("Shirt", -1m, null), Actor, Now));
    }

    [Fact]
    public void Updating_a_deactivated_item_is_refused()
    {
        var item = New();
        item.Deactivate("discontinued", Actor, Now);
        Assert.Throws<DomainException>(() => item.Update(new NewMerchItem("Shirt v2", 400m, null), Actor, Now));
    }

    [Fact]
    public void Deactivating_twice_is_refused()
    {
        var item = New();
        item.Deactivate("discontinued", Actor, Now);
        Assert.Throws<DomainException>(() => item.Deactivate("again", Actor, Now));
    }
}

public class MerchVariantTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 8, 0, 0, TimeSpan.Zero);
    private static readonly Guid ItemId = Guid.CreateVersion7(Now);
    private static readonly Guid Actor = Guid.CreateVersion7(Now);

    [Fact]
    public void AdjustStock_sets_the_counted_value_not_a_delta()
    {
        var v = MerchVariant.Create(ItemId, "M", 10, Actor, Now);
        v.AdjustStock(3, Actor, Now);
        Assert.Equal(3, v.StockOnHand);
    }

    [Fact]
    public void AdjustStock_rejects_negative_stock()
    {
        var v = MerchVariant.Create(ItemId, "M", 10, Actor, Now);
        Assert.Throws<DomainException>(() => v.AdjustStock(-1, Actor, Now));
    }

    [Fact]
    public void Adjusting_a_deactivated_variant_is_refused()
    {
        var v = MerchVariant.Create(ItemId, "M", 10, Actor, Now);
        v.Deactivate("retired", Actor, Now);
        Assert.Throws<DomainException>(() => v.AdjustStock(5, Actor, Now));
    }
}

public class MerchSaleTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 8, 0, 0, TimeSpan.Zero);
    private static readonly Guid EventId = Guid.CreateVersion7(Now);
    private static readonly Guid ItemId = Guid.CreateVersion7(Now);
    private static readonly Guid VariantId = Guid.CreateVersion7(Now);
    private static readonly Guid Actor = Guid.CreateVersion7(Now);
    private static readonly Guid ClientRequestId = Guid.CreateVersion7(Now);

    [Fact]
    public void Create_computes_subtotal_discount_and_total()
    {
        var (sale, lines) = Htn.Domain.Merch.MerchSale.Create(
            EventId, ClientRequestId, null, null, "Walk-in", null,
            [new NewMerchSaleLine(ItemId, VariantId, 2, 350m)], discountPct: 20, MerchSaleMethod.Cash, Actor, "Booth A",
            membershipFeeIncluded: false, membershipFeeAmount: null, Now);

        Assert.Equal(700m, sale.Subtotal);
        Assert.Equal(140m, sale.DiscountAmount);
        Assert.Equal(560m, sale.Total);
        Assert.Equal(ClientRequestId, sale.ClientRequestId);
        Assert.Single(lines);
        Assert.Equal(700m, lines[0].LineTotal);
    }

    [Fact]
    public void A_goon_buyer_gets_the_snapshotted_level()
    {
        var (sale, _) = Htn.Domain.Merch.MerchSale.Create(
            EventId, ClientRequestId, Guid.CreateVersion7(Now), null, "Jo Goon", PersonLevel.Goon,
            [new NewMerchSaleLine(ItemId, VariantId, 1, 100m)], discountPct: 20, MerchSaleMethod.GCash, Actor, null,
            membershipFeeIncluded: false, membershipFeeAmount: null, Now);
        Assert.Equal(PersonLevel.Goon, sale.BuyerLevelSnapshot);
    }

    [Fact]
    public void Create_rejects_an_empty_line_list()
    {
        Assert.Throws<DomainException>(() => Htn.Domain.Merch.MerchSale.Create(
            EventId, ClientRequestId, null, null, "Walk-in", null, [], 0, MerchSaleMethod.Cash, Actor, null,
            membershipFeeIncluded: false, membershipFeeAmount: null, Now));
    }

    [Fact]
    public void Create_rejects_a_discount_outside_0_to_100()
    {
        Assert.Throws<DomainException>(() => Htn.Domain.Merch.MerchSale.Create(
            EventId, ClientRequestId, null, null, "Walk-in", null,
            [new NewMerchSaleLine(ItemId, VariantId, 1, 100m)], 101, MerchSaleMethod.Cash, Actor, null,
            membershipFeeIncluded: false, membershipFeeAmount: null, Now));
    }

    [Fact]
    public void Create_rejects_an_empty_client_request_id()
    {
        Assert.Throws<DomainException>(() => Htn.Domain.Merch.MerchSale.Create(
            EventId, Guid.Empty, null, null, "Walk-in", null,
            [new NewMerchSaleLine(ItemId, VariantId, 1, 100m)], 0, MerchSaleMethod.Cash, Actor, null,
            membershipFeeIncluded: false, membershipFeeAmount: null, Now));
    }

    [Fact]
    public void Line_rejects_zero_or_negative_quantity()
    {
        Assert.Throws<DomainException>(() => MerchSaleLine.Create(
            Guid.CreateVersion7(Now), new NewMerchSaleLine(ItemId, VariantId, 0, 100m), Now));
    }

    [Fact]
    public void A_sale_line_has_no_mutation_method_once_created()
    {
        // MerchSaleLine exposes only private setters and a factory: there is no domain method that could ever
        // rewrite a snapshot once it exists. The database half of this guarantee (Merch_Guards) is exercised by the
        // API-level integrity-guard tests against a real Postgres trigger.
        var line = MerchSaleLine.Create(Guid.CreateVersion7(Now), new NewMerchSaleLine(ItemId, VariantId, 1, 100m), Now);
        var publicMethods = typeof(MerchSaleLine).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Where(m => !m.IsSpecialName && m.DeclaringType == typeof(MerchSaleLine))
            .ToList();
        Assert.Empty(publicMethods);
        Assert.Equal(100m, line.UnitPriceSnapshot);
    }
}
