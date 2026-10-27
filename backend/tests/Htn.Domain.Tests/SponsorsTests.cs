using Htn.Domain.Common;
using Htn.Domain.Sponsors;

namespace Htn.Domain.Tests;

public class NameKeyTests
{
    [Theory]
    [InlineData("CloudCorp PH", "cloudcorp ph")]
    [InlineData("  D.I.C.T.  ", "d i c t")]
    [InlineData("Ayala Land, Inc.", "ayala land inc")]
    [InlineData("UP Diliman", "up diliman")]
    public void Normalizes_like_the_generated_column(string input, string expected) =>
        Assert.Equal(expected, NameKey.Of(input));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("!!!")]
    public void Returns_null_when_there_is_nothing_to_key_on(string input) =>
        Assert.Null(NameKey.Of(input));
}

public class SponsorTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 8, 0, 0, TimeSpan.Zero);
    private static readonly Guid Event = Guid.CreateVersion7(Now);
    private static readonly Guid Actor = Guid.CreateVersion7(Now);

    private static NewSponsor Org(decimal? cash = 1000m, string? inKind = null, int? inKindCount = null, string name = "Acme") =>
        new(SponsorKind.Organization, name, null, false, "Kai", "Partnerships", null, null, null, cash, inKind, inKindCount, 0, null);

    [Fact]
    public void A_sponsor_needs_a_contribution()
    {
        var ex = Assert.Throws<DomainException>(() => Sponsor.Create(Event, Org(cash: null), Actor, Now));
        Assert.Contains("cash amount", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Cash_cannot_be_recorded_before_confirming()
    {
        var sponsor = Sponsor.Create(Event, Org(), Actor, Now);
        Assert.Throws<DomainException>(() => sponsor.MarkCashReceived(Guid.CreateVersion7(Now), null, Actor, Now));
    }

    [Fact]
    public void Recording_cash_requires_a_cash_contribution()
    {
        var sponsor = Sponsor.Create(Event, Org(cash: null, inKind: "400 tote bags", inKindCount: 400), Actor, Now);
        sponsor.Confirm(Actor, Now);
        Assert.Throws<DomainException>(() => sponsor.MarkCashReceived(Guid.CreateVersion7(Now), null, Actor, Now));
    }

    [Fact]
    public void A_confirmed_sponsor_can_record_and_reverse_its_cash()
    {
        var sponsor = Sponsor.Create(Event, Org(), Actor, Now);
        sponsor.Confirm(Actor, Now);
        var account = Guid.CreateVersion7(Now);
        sponsor.MarkCashReceived(account, "OR-1", Actor, Now);
        Assert.True(sponsor.CashPosted);
        sponsor.ClearCashReceipt(Actor, Now);
        Assert.False(sponsor.CashPosted);
        Assert.Null(sponsor.CashPaymentAccountId);
    }

    [Fact]
    public void A_sponsor_cannot_be_unconfirmed_or_voided_while_its_cash_is_posted()
    {
        var sponsor = Sponsor.Create(Event, Org(), Actor, Now);
        sponsor.Confirm(Actor, Now);
        sponsor.MarkCashReceived(Guid.CreateVersion7(Now), null, Actor, Now);
        Assert.Throws<DomainException>(() => sponsor.Unconfirm(Actor, Now));
        Assert.Throws<DomainException>(() => sponsor.Void("nope", Actor, Now));
    }

    [Fact]
    public void An_individual_can_be_anonymous_and_is_not_credited_by_name()
    {
        var data = new NewSponsor(SponsorKind.Individual, "Ramon Villanueva", null, true, null, null, null, null, null, 5000m, null, null, 0, null);
        var sponsor = Sponsor.Create(Event, data, Actor, Now);
        Assert.True(sponsor.IsAnonymous);
        Assert.Null(sponsor.DisplayName);
    }

    [Fact]
    public void An_organization_is_never_anonymous()
    {
        var data = new NewSponsor(SponsorKind.Organization, "Acme", null, true, null, null, null, null, null, 1000m, null, null, 0, null);
        var sponsor = Sponsor.Create(Event, data, Actor, Now);
        Assert.False(sponsor.IsAnonymous);
    }
}

public class SponsorTierTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_tier_keeps_only_clean_perks()
    {
        var data = new NewSponsorTier(TierClass.Gold, "Gold", 100000m, null, null, null, 3, 3, 0,
            ["  Logo on lanyard ", "", "   ", "Booth"]);
        var tier = SponsorTier.Create(Guid.CreateVersion7(Now), data, Guid.CreateVersion7(Now), Now);
        Assert.Equal(["Logo on lanyard", "Booth"], tier.Perks);
    }

    [Fact]
    public void A_tier_needs_a_price()
    {
        var data = new NewSponsorTier(TierClass.Silver, "Silver", null, null, null, null, 1, 0, 0, []);
        Assert.Throws<DomainException>(() => SponsorTier.Create(Guid.CreateVersion7(Now), data, Guid.CreateVersion7(Now), Now));
    }
}
