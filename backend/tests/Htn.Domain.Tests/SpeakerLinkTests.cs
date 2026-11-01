using Htn.Domain.Common;
using Htn.Domain.Speakers;

namespace Htn.Domain.Tests;

public class SpeakerLinkTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 8, 0, 0, TimeSpan.Zero);
    private static readonly Guid Speaker = Guid.CreateVersion7(Now);
    private static readonly Guid Event = Guid.CreateVersion7(Now);
    private static readonly Guid Actor = Guid.CreateVersion7(Now);
    private static readonly string Hash = new('a', 64);

    [Fact]
    public void A_link_expires_14_days_from_creation()
    {
        var link = SpeakerLink.Create(Speaker, Event, Hash, Actor, Now);
        Assert.Equal(Now + SpeakerLink.LinkLifetime, link.ExpiresAt);
        Assert.False(link.IsExpired(Now));
        Assert.True(link.IsExpired(Now + TimeSpan.FromDays(15)));
    }

    [Fact]
    public void Revoking_clears_the_otp_and_session_and_is_write_once()
    {
        var link = SpeakerLink.Create(Speaker, Event, Hash, Actor, Now);
        link.TryIssueOtp(new string('b', 64), Now);
        link.OpenSession(new string('c', 64), Now);
        link.Revoke("No longer speaking", Actor, Now);
        Assert.True(link.IsRevoked);
        Assert.Null(link.OtpHash);
        Assert.Null(link.SessionTokenHash);
        Assert.Throws<DomainException>(() => link.Revoke("Again", Actor, Now));
    }

    [Fact]
    public void An_otp_window_resets_the_resend_count_but_the_lifetime_cap_still_applies()
    {
        var link = SpeakerLink.Create(Speaker, Event, Hash, Actor, Now);
        for (var i = 0; i < SpeakerLink.ResendPerWindow; i++)
            Assert.True(link.TryIssueOtp(new string((char)('a' + i), 64), Now));
        // Budget exhausted within the window.
        Assert.False(link.TryIssueOtp(new string('z', 64), Now));
        // After the window rolls, the count resets.
        Assert.True(link.TryIssueOtp(new string('y', 64), Now + SpeakerLink.ResendWindow));
    }

    [Fact]
    public void Opening_a_session_consumes_the_otp()
    {
        var link = SpeakerLink.Create(Speaker, Event, Hash, Actor, Now);
        link.TryIssueOtp(new string('d', 64), Now);
        link.OpenSession(new string('e', 64), Now);
        Assert.Null(link.OtpHash);
        Assert.True(link.SessionIsValid(new string('e', 64), Now));
        Assert.False(link.SessionIsValid(new string('e', 64), Now + SpeakerLink.SessionLifetime + TimeSpan.FromMinutes(1)));
    }
}

public class SpeakerTravelTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 8, 0, 0, TimeSpan.Zero);
    private static readonly Guid Speaker = Guid.CreateVersion7(Now);
    private static readonly Guid Event = Guid.CreateVersion7(Now);

    private static TravelSubmission Plane() => new(
        new DateOnly(2026, 11, 21), new DateOnly(2026, 11, 23), "Davao", "Afternoon", "Lara Gomez",
        null, null, false, null, null, SpeakerTravel.CurrentConsentVersion);

    [Fact]
    public void Submitting_stamps_consent_once_and_allows_resubmission()
    {
        var travel = SpeakerTravel.Create(Speaker, Event);
        travel.Submit(Plane(), Now);
        Assert.True(travel.HasSubmitted);
        var firstConsent = travel.ConsentAt;
        travel.Submit(Plane() with { FlyingFrom = "Cebu" }, Now + TimeSpan.FromDays(1));
        Assert.Equal(firstConsent, travel.ConsentAt); // consent is write-once
        Assert.Equal("Cebu", travel.FlyingFrom);
    }

    [Fact]
    public void Departure_before_arrival_is_refused()
    {
        var travel = SpeakerTravel.Create(Speaker, Event);
        var bad = Plane() with { ArrivalDate = new DateOnly(2026, 11, 23), DepartureDate = new DateOnly(2026, 11, 21) };
        Assert.Throws<DomainException>(() => travel.Submit(bad, Now));
    }

    [Fact]
    public void Purging_nulls_only_the_flight_and_id_name_fields()
    {
        var travel = SpeakerTravel.Create(Speaker, Event);
        travel.Submit(Plane(), Now);
        var (hadId, hadFlying, hadArrival) = travel.Purge(Now + TimeSpan.FromDays(31));
        Assert.True(hadId); Assert.True(hadFlying); Assert.True(hadArrival);
        Assert.Null(travel.IdFullName);
        Assert.Null(travel.FlyingFrom);
        Assert.Null(travel.PreferredArrivalTime);
        Assert.NotNull(travel.ArrivalDate); // dates survive the purge
        Assert.True(travel.IsPurged);
    }

    [Fact]
    public void A_purged_row_cannot_be_submitted_again()
    {
        var travel = SpeakerTravel.Create(Speaker, Event);
        travel.Submit(Plane(), Now);
        travel.Purge(Now + TimeSpan.FromDays(31));
        Assert.Throws<DomainException>(() => travel.Submit(Plane(), Now + TimeSpan.FromDays(32)));
    }
}

public class PlusOneCodeTests
{
    [Theory]
    [InlineData("Maria Gomez", "GOMEZ")]
    [InlineData("Jean-Luc Picard", "PICARD")]
    [InlineData("José García", "GARCIA")]
    public void Segments_the_last_word_and_strips_accents(string name, string expected) =>
        Assert.Equal(expected, PlusOneCode.Segment(name));

    [Fact]
    public void Falls_back_to_guest_when_nothing_survives() =>
        Assert.Equal("GUEST", PlusOneCode.Segment("日本語"));

    [Fact]
    public void Builds_the_full_code() =>
        Assert.Equal("SPK-GOMEZ-7Q4X", PlusOneCode.Code("GOMEZ", "7Q4X"));
}
