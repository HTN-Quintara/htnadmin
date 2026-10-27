using Htn.Domain.Common;
using Htn.Domain.Leads;
using Htn.Domain.Speakers;

namespace Htn.Domain.Tests;

public class LeadTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 8, 0, 0, TimeSpan.Zero);
    private static readonly Guid Event = Guid.CreateVersion7(Now);
    private static readonly Guid Actor = Guid.CreateVersion7(Now);

    private static NewLead Sponsor(string subject = "Globe") =>
        new(LeadKind.Sponsor, subject, "Rina", "Manager", null, null, null, 100000m, "Gold", null, "Follow up", null);

    [Fact]
    public void A_new_lead_starts_as_a_prospect()
    {
        var lead = Lead.Create(Event, Sponsor(), Actor, Now);
        Assert.Equal(LeadStatus.Prospect, lead.Status);
        Assert.Equal("globe", lead.NameKey);
    }

    [Fact]
    public void Declining_requires_a_reason_and_can_be_reopened()
    {
        var lead = Lead.Create(Event, Sponsor(), Actor, Now);
        Assert.Throws<DomainException>(() => lead.MoveTo(LeadStatus.Declined, " ", Actor, Now));
        lead.MoveTo(LeadStatus.Declined, "Budget used", Actor, Now);
        Assert.Equal("Budget used", lead.DeclineReason);
        lead.MoveTo(LeadStatus.Contacted, null, Actor, Now);
        Assert.Equal(LeadStatus.Contacted, lead.Status);
        Assert.Null(lead.DeclineReason);
    }

    [Fact]
    public void Linking_a_sponsor_confirms_and_is_write_once()
    {
        var lead = Lead.Create(Event, Sponsor(), Actor, Now);
        var sponsorId = Guid.CreateVersion7(Now);
        lead.LinkSponsor(sponsorId, Actor, Now);
        Assert.Equal(LeadStatus.Confirmed, lead.Status);
        Assert.Equal(sponsorId, lead.SponsorId);
        Assert.Throws<DomainException>(() => lead.LinkSponsor(Guid.CreateVersion7(Now), Actor, Now));
    }

    [Fact]
    public void A_speaker_lead_carries_no_sponsor_ask()
    {
        var data = new NewLead(LeadKind.Speaker, "Dr. Gomez", null, null, "g@x.ph", null, Guid.CreateVersion7(Now), 5000m, null, null, null, null);
        var lead = Lead.Create(Event, data, Actor, Now);
        Assert.Null(lead.AskTierId);
        Assert.Null(lead.AskAmount);
    }

    [Fact]
    public void A_confirmed_linked_lead_cannot_be_reopened()
    {
        var lead = Lead.Create(Event, Sponsor(), Actor, Now);
        lead.LinkSponsor(Guid.CreateVersion7(Now), Actor, Now);
        Assert.Throws<DomainException>(() => lead.MoveTo(LeadStatus.Contacted, null, Actor, Now));
    }
}

public class SpeakerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 8, 0, 0, TimeSpan.Zero);
    private static readonly Guid Event = Guid.CreateVersion7(Now);
    private static readonly Guid Actor = Guid.CreateVersion7(Now);

    [Fact]
    public void A_direct_speaker_starts_invited()
    {
        var sp = Speaker.Create(Event, "Jon Rivera", "jon@cloudcorp.ph", "CloudCorp", Actor, Now);
        Assert.Equal(SpeakerStatus.Invited, sp.Status);
        Assert.Null(sp.LeadId);
    }

    [Fact]
    public void A_speaker_from_a_lead_is_confirmed_and_linked()
    {
        var leadId = Guid.CreateVersion7(Now);
        var sp = Speaker.FromLead(Event, leadId, "Dr. Gomez", "g@dict.gov.ph", null, Actor, Now);
        Assert.Equal(SpeakerStatus.Confirmed, sp.Status);
        Assert.Equal(leadId, sp.LeadId);
    }

    [Fact]
    public void A_photo_must_exist_before_it_is_approved_and_a_new_photo_resets_approval()
    {
        var sp = Speaker.Create(Event, "Jon", "jon@x.ph", null, Actor, Now);
        Assert.Throws<DomainException>(() => sp.ApprovePhoto(Actor, Now));
        sp.SetPhoto(Guid.CreateVersion7(Now), Actor, Now);
        sp.ApprovePhoto(Actor, Now);
        Assert.True(sp.PhotoApproved);
        sp.SetPhoto(Guid.CreateVersion7(Now), Actor, Now);
        Assert.False(sp.PhotoApproved);
    }

    [Fact]
    public void An_invalid_email_is_refused()
    {
        Assert.Throws<DomainException>(() => Speaker.Create(Event, "X", "not-an-email", null, Actor, Now));
    }
}
