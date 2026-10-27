using Htn.Domain.Common;

namespace Htn.Domain.Sponsors;

/// <summary>
/// Links a comped registration (a seat on the hidden "Sponsor comp" ticket) to the sponsor that got it, so used
/// comps can be counted against the sponsor's entitlement. Append-only: a comp is released by cancelling its
/// registration, never by deleting this row.
/// </summary>
public sealed class SponsorCompRegistration
{
    private SponsorCompRegistration() { }

    public Guid Id { get; private set; }
    public Guid SponsorId { get; private set; }
    public Guid EventId { get; private set; }
    public Guid RegistrationId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public Guid? CreatedById { get; private set; }

    public static SponsorCompRegistration Create(
        Guid sponsorId, Guid eventId, Guid registrationId, Guid actorId, DateTimeOffset at)
    {
        if (sponsorId == Guid.Empty || registrationId == Guid.Empty)
            throw new DomainException("A sponsor and a registration are required.");
        return new SponsorCompRegistration
        {
            Id = Guid.CreateVersion7(at),
            SponsorId = sponsorId,
            EventId = eventId,
            RegistrationId = registrationId,
            CreatedAt = at,
            CreatedById = actorId,
        };
    }
}
