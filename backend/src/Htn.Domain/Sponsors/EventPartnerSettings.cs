using Htn.Domain.Common;

namespace Htn.Domain.Sponsors;

/// <summary>
/// Per-event partner rules (the aside on the Partners tab): the registrant threshold at which an organization
/// becomes a partner, which registrants are counted, and whether a group registration link is offered. One row per
/// event, created lazily on first read.
/// </summary>
public sealed class EventPartnerSettings
{
    public const int MinThreshold = 5;
    public const int MaxThreshold = 1000;
    public const int ThresholdStep = 5;
    public const int DefaultThreshold = 20;

    private EventPartnerSettings() { }

    public Guid Id { get; private set; }
    public Guid EventId { get; private set; }
    public int Threshold { get; private set; }
    public PartnerCountBasis CountBasis { get; private set; }
    public bool GroupLinkEnabled { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public Guid? UpdatedById { get; private set; }

    public static EventPartnerSettings Default(Guid eventId, DateTimeOffset at) => new()
    {
        Id = Guid.CreateVersion7(at),
        EventId = eventId,
        Threshold = DefaultThreshold,
        CountBasis = PartnerCountBasis.PaidOnly,
        GroupLinkEnabled = false,
        CreatedAt = at,
        UpdatedAt = at,
    };

    public void Update(int threshold, PartnerCountBasis basis, bool groupLinkEnabled, Guid actorId, DateTimeOffset at)
    {
        SponsorRules.Defined(basis, "count basis");
        if (threshold < MinThreshold || threshold > MaxThreshold)
            throw new DomainException($"Threshold must be between {MinThreshold} and {MaxThreshold}.");
        if (threshold % ThresholdStep != 0)
            throw new DomainException($"Threshold moves in steps of {ThresholdStep}.");
        Threshold = threshold;
        CountBasis = basis;
        GroupLinkEnabled = groupLinkEnabled;
        UpdatedAt = at;
        UpdatedById = actorId;
    }
}
