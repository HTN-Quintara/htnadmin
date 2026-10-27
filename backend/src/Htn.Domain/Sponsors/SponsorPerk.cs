using Htn.Domain.Common;

namespace Htn.Domain.Sponsors;

/// <summary>
/// One perk promised to a sponsor, snapshotted from the tier when the sponsor is confirmed (like a points award):
/// editing the tier later never rewrites a signed deal. Marked delivered, or removed with a reason. Never deleted.
/// </summary>
public sealed class SponsorPerk
{
    public const int TextMaxLength = 200;
    public const int ReasonMaxLength = 500;

    private SponsorPerk() { }

    public Guid Id { get; private set; }
    public Guid SponsorId { get; private set; }
    public Guid EventId { get; private set; }
    public Guid? SourceTierId { get; private set; }
    public string Text { get; private set; } = default!;
    public int SortOrder { get; private set; }
    public DateTimeOffset? DeliveredAt { get; private set; }
    public Guid? DeliveredById { get; private set; }
    public DateTimeOffset? RemovedAt { get; private set; }
    public string? RemoveReason { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public bool IsDelivered => DeliveredAt is not null;
    public bool IsRemoved => RemovedAt is not null;

    public static SponsorPerk Create(
        Guid sponsorId, Guid eventId, Guid? sourceTierId, string text, int sortOrder, DateTimeOffset at) => new()
    {
        Id = Guid.CreateVersion7(at),
        SponsorId = sponsorId,
        EventId = eventId,
        SourceTierId = sourceTierId,
        Text = SponsorRules.Required(text, "Perk", TextMaxLength),
        SortOrder = sortOrder,
        CreatedAt = at,
    };

    public void SetDelivered(bool delivered, Guid actorId, DateTimeOffset at)
    {
        if (IsRemoved) throw new DomainException("This perk was removed.");
        if (delivered)
        {
            if (IsDelivered) return;
            DeliveredAt = at;
            DeliveredById = actorId;
        }
        else
        {
            DeliveredAt = null;
            DeliveredById = null;
        }
    }

    public void Remove(string reason, DateTimeOffset at)
    {
        if (IsRemoved) throw new DomainException("This perk is already removed.");
        RemoveReason = SponsorRules.Required(reason, "Reason", ReasonMaxLength);
        RemovedAt = at;
    }
}
