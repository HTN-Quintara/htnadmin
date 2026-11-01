using Htn.Domain.Common;
using Htn.Domain.Directory;

namespace Htn.Domain.Tests;

public class DirectoryOrgTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 8, 0, 0, TimeSpan.Zero);
    private static readonly Guid Actor = Guid.CreateVersion7(Now);

    private static NewDirectoryOrg Org(string name = "CloudCorp PH") =>
        new(DirectoryOrgKind.Company, name, null, "https://cloudcorp.ph", null);

    [Fact]
    public void Create_computes_the_name_key()
    {
        var org = DirectoryOrg.Create(Org(), Actor, Now);
        Assert.Equal("cloudcorp ph", org.NameKey);
    }

    [Fact]
    public void A_name_with_no_letters_or_numbers_is_rejected()
    {
        var ex = Assert.Throws<DomainException>(() => DirectoryOrg.Create(Org("!!!"), Actor, Now));
        Assert.Contains("letters or numbers", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Update_leaves_name_and_kind_untouched()
    {
        var org = DirectoryOrg.Create(Org(), Actor, Now);
        org.Update(new UpdateDirectoryOrg("Dear Sir/Madam", "https://cloudcorp.ph/about", "Follow up quarterly"), Actor, Now);
        Assert.Equal("CloudCorp PH", org.Name);
        Assert.Equal(DirectoryOrgKind.Company, org.Kind);
        Assert.Equal("Dear Sir/Madam", org.AddresseeSalutation);
    }

    [Fact]
    public void Deactivating_needs_a_reason_and_is_final()
    {
        var org = DirectoryOrg.Create(Org(), Actor, Now);
        Assert.Throws<DomainException>(() => org.Deactivate("", Actor, Now));
        org.Deactivate("Closed down", Actor, Now);
        Assert.False(org.IsActive);
        Assert.Throws<DomainException>(() => org.Deactivate("Again", Actor, Now));
        Assert.Throws<DomainException>(() => org.Update(new UpdateDirectoryOrg(null, null, null), Actor, Now));
    }
}

public class DirectoryContactTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 8, 0, 0, TimeSpan.Zero);
    private static readonly Guid OrgId = Guid.CreateVersion7(Now);
    private static readonly Guid Actor = Guid.CreateVersion7(Now);

    private static NewDirectoryContact Contact(bool primary = true) =>
        new("President", "Alice Cruz", "alice@test.co", "+639171234567", primary, "finance@test.co; ops@test.co");

    [Fact]
    public void A_fresh_contact_needs_a_check()
    {
        var contact = DirectoryContact.Create(OrgId, Contact(), Actor, Now);
        Assert.True(contact.NeedsCheck(Now));
        Assert.Null(contact.VerifiedAt);
    }

    [Theory]
    [InlineData(89, false)]
    [InlineData(90, false)]
    [InlineData(91, true)]
    public void NeedsCheck_boundary_is_90_days(int daysAgo, bool expectedNeedsCheck)
    {
        var contact = DirectoryContact.Create(OrgId, Contact(), Actor, Now.AddDays(-daysAgo));
        contact.StillCorrect(Actor, Now.AddDays(-daysAgo));
        Assert.Equal(expectedNeedsCheck, contact.NeedsCheck(Now));
    }

    [Fact]
    public void StillCorrect_stamps_verified_at()
    {
        var contact = DirectoryContact.Create(OrgId, Contact(), Actor, Now);
        contact.StillCorrect(Actor, Now.AddHours(1));
        Assert.Equal(Now.AddHours(1), contact.VerifiedAt);
        Assert.False(contact.NeedsCheck(Now.AddHours(1)));
    }

    [Fact]
    public void HeadChanged_updates_the_contact_and_returns_the_prior_snapshot()
    {
        var contact = DirectoryContact.Create(OrgId, Contact(), Actor, Now);
        var snapshot = contact.HeadChanged("Vice President", "Bob Santos", "bob@test.co", null, Actor, Now.AddDays(10));

        Assert.Equal("President", snapshot.PriorPosition);
        Assert.Equal("Alice Cruz", snapshot.PriorHolderName);
        Assert.Equal("Vice President", contact.Position);
        Assert.Equal("Bob Santos", contact.HolderName);
        Assert.Equal(Now.AddDays(10), contact.VerifiedAt);
        Assert.False(contact.NeedsCheck(Now.AddDays(10)));
    }

    [Fact]
    public void Cc_emails_are_normalized_and_validated()
    {
        var contact = DirectoryContact.Create(OrgId, Contact(), Actor, Now);
        Assert.Equal("finance@test.co;ops@test.co", contact.CcEmails);
        Assert.Throws<DomainException>(() =>
            DirectoryContact.Create(OrgId, Contact() with { CcEmails = "not-an-email" }, Actor, Now));
    }

    [Fact]
    public void A_voided_contact_cannot_be_edited_again()
    {
        var contact = DirectoryContact.Create(OrgId, Contact(), Actor, Now);
        contact.Void("Org closed", Actor, Now);
        Assert.Throws<DomainException>(() => contact.StillCorrect(Actor, Now));
        Assert.Throws<DomainException>(() => contact.Void("Again", Actor, Now));
    }
}

public class DirectoryContactHistoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 8, 0, 0, TimeSpan.Zero);
    private static readonly Guid OrgId = Guid.CreateVersion7(Now);
    private static readonly Guid ContactId = Guid.CreateVersion7(Now);
    private static readonly Guid Actor = Guid.CreateVersion7(Now);

    [Fact]
    public void HolderChanged_needs_the_prior_snapshot()
    {
        var ex = Assert.Throws<DomainException>(() =>
            DirectoryContactHistory.HolderChanged(OrgId, ContactId, new HolderChangeSnapshot("", ""), Actor, Now));
        Assert.Contains("prior", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HolderChanged_carries_the_prior_position_and_holder()
    {
        var history = DirectoryContactHistory.HolderChanged(
            OrgId, ContactId, new HolderChangeSnapshot("President", "Alice"), Actor, Now);
        Assert.Equal(DirectoryContactHistoryKind.HolderChanged, history.Kind);
        Assert.Equal("President", history.PriorPosition);
        Assert.Equal("Alice", history.PriorHolderName);
    }

    [Fact]
    public void StillCorrect_carries_no_prior_snapshot()
    {
        var history = DirectoryContactHistory.StillCorrect(OrgId, ContactId, Actor, Now);
        Assert.Null(history.PriorPosition);
        Assert.Null(history.PriorHolderName);
    }
}
