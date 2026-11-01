using Htn.Domain.Common;
using Htn.Domain.Invitations;

namespace Htn.Domain.Tests;

public class EventInvitationTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 8, 0, 0, TimeSpan.Zero);
    private static readonly Guid Event = Guid.CreateVersion7(Now);
    private static readonly Guid Org = Guid.CreateVersion7(Now);
    private static readonly Guid Contact = Guid.CreateVersion7(Now);
    private static readonly Guid Actor = Guid.CreateVersion7(Now);

    private static EventInvitation New() => EventInvitation.Create(Event, Org, Actor, Now);

    [Fact]
    public void A_new_invitation_starts_not_sent()
    {
        var inv = New();
        Assert.Equal(InvitationStatus.NotSent, inv.Status);
        Assert.True(inv.IsNotSent);
    }

    [Fact]
    public void Send_requires_the_precomputed_verified_flag_to_be_true()
    {
        var inv = New();
        var ex = Assert.Throws<DomainException>(() => inv.Send(Contact, "recipient@example.com", "Recipient Name", "Subject", "Body", contactIsVerified: false, Actor, Now));
        Assert.Contains("verified", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(InvitationStatus.NotSent, inv.Status);
    }

    [Fact]
    public void Send_moves_to_sent_and_freezes_the_snapshot()
    {
        var inv = New();
        inv.Send(Contact, "recipient@example.com", "Recipient Name", "Subject", "Body", contactIsVerified: true, Actor, Now);
        Assert.Equal(InvitationStatus.Sent, inv.Status);
        Assert.Equal(Contact, inv.ContactId);
        Assert.Equal("Subject", inv.LetterSubjectSnapshot);
        Assert.Equal("Body", inv.LetterBodySnapshot);
        Assert.Equal(Now, inv.SentAt);
    }

    [Fact]
    public void Sending_twice_is_refused()
    {
        var inv = New();
        inv.Send(Contact, "recipient@example.com", "Recipient Name", "Subject", "Body", true, Actor, Now);
        Assert.Throws<DomainException>(() => inv.Send(Contact, "recipient@example.com", "Recipient Name", "Subject2", "Body2", true, Actor, Now));
        // The snapshot from the first (successful) send is untouched.
        Assert.Equal("Subject", inv.LetterSubjectSnapshot);
    }

    [Theory]
    [InlineData(InvitationStatus.Sent, InvitationStatus.Opened, true)]
    [InlineData(InvitationStatus.Sent, InvitationStatus.Confirmed, true)]
    [InlineData(InvitationStatus.Sent, InvitationStatus.Declined, true)]
    [InlineData(InvitationStatus.Sent, InvitationStatus.Bounced, true)]
    [InlineData(InvitationStatus.Opened, InvitationStatus.Replied, true)]
    [InlineData(InvitationStatus.Opened, InvitationStatus.Confirmed, true)]
    [InlineData(InvitationStatus.Replied, InvitationStatus.Confirmed, true)]
    [InlineData(InvitationStatus.Replied, InvitationStatus.Declined, true)]
    [InlineData(InvitationStatus.Bounced, InvitationStatus.Sent, true)]
    [InlineData(InvitationStatus.Sent, InvitationStatus.NotSent, false)]
    [InlineData(InvitationStatus.Confirmed, InvitationStatus.Declined, false)]
    [InlineData(InvitationStatus.Declined, InvitationStatus.Sent, false)]
    [InlineData(InvitationStatus.Opened, InvitationStatus.Sent, false)]
    [InlineData(InvitationStatus.Bounced, InvitationStatus.Confirmed, false)]
    public void Transitions_follow_the_whitelist(InvitationStatus from, InvitationStatus to, bool allowed)
    {
        var inv = New();
        inv.Send(Contact, "recipient@example.com", "Recipient Name", "Subject", "Body", true, Actor, Now);
        // Walk to `from` via only known-good moves, skipping cases that start at Sent already.
        if (from != InvitationStatus.Sent)
        {
            // Build a short valid path to `from` for the statuses this test exercises.
            var path = from switch
            {
                InvitationStatus.Opened => new[] { InvitationStatus.Opened },
                InvitationStatus.Replied => new[] { InvitationStatus.Opened, InvitationStatus.Replied },
                InvitationStatus.Bounced => new[] { InvitationStatus.Bounced },
                InvitationStatus.Confirmed => new[] { InvitationStatus.Confirmed },
                InvitationStatus.Declined => new[] { InvitationStatus.Declined },
                _ => Array.Empty<InvitationStatus>(),
            };
            foreach (var step in path) inv.AdvanceStatus(step, Now);
        }

        if (allowed)
        {
            inv.AdvanceStatus(to, Now);
            Assert.Equal(to, inv.Status);
        }
        else
        {
            Assert.Throws<DomainException>(() => inv.AdvanceStatus(to, Now));
        }
    }

    [Fact]
    public void An_unknown_status_is_refused()
    {
        var inv = New();
        inv.Send(Contact, "recipient@example.com", "Recipient Name", "Subject", "Body", true, Actor, Now);
        Assert.Throws<DomainException>(() => inv.AdvanceStatus((InvitationStatus)999, Now));
    }
}

public class InvitationMergeTests
{
    private static readonly DateTimeOffset EventDate = new(2026, 11, 15, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Merge_substitutes_every_token()
    {
        var addressee = new InvitationAddressee("Alice Cruz", "President", "Dear Sir/Madam");
        var merged = InvitationMerge.Merge(
            "Invitation for [date]",
            "Dear [addressee.salutation] [addressee.name] ([addressee.position]), please join us on [date].",
            addressee, EventDate);

        Assert.Equal("Invitation for November 15, 2026", merged.Subject);
        Assert.Equal("Dear Dear Sir/Madam Alice Cruz (President), please join us on November 15, 2026.", merged.Body);
    }

    [Fact]
    public void A_missing_salutation_falls_back_to_a_generic_one()
    {
        var addressee = new InvitationAddressee("Alice Cruz", "President", null);
        var merged = InvitationMerge.Merge("Hi", "Dear [addressee.salutation],", addressee, EventDate);
        Assert.Equal("Dear Sir/Madam,", merged.Body);
    }
}
