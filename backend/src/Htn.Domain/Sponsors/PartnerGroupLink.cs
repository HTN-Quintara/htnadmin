using Htn.Domain.Common;

namespace Htn.Domain.Sponsors;

/// <summary>
/// A shareable registration link for a partner organization, offered only once the partner has reached the event's
/// threshold. Only the token hash is stored; the raw token lives in the URL. Revoked with a reason, never deleted,
/// and at most one live link per partner. Sign-ups are attributed to the partner by company, so this row only needs
/// to mint and retire the URL.
/// </summary>
public sealed class PartnerGroupLink
{
    public const int TokenHashLength = 64;
    public const int ReasonMaxLength = 500;

    private PartnerGroupLink() { }

    public Guid Id { get; private set; }
    public Guid EventId { get; private set; }
    public Guid EventPartnerId { get; private set; }
    public string TokenHash { get; private set; } = default!;
    public DateTimeOffset CreatedAt { get; private set; }
    public Guid? CreatedById { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public Guid? RevokedById { get; private set; }
    public string? RevokeReason { get; private set; }

    public bool IsRevoked => RevokedAt is not null;

    public static PartnerGroupLink Create(
        Guid eventId, Guid eventPartnerId, string tokenHash, Guid actorId, DateTimeOffset at)
    {
        if (eventPartnerId == Guid.Empty) throw new DomainException("A partner is required.");
        if (tokenHash is not { Length: TokenHashLength }) throw new DomainException("Invalid token.");
        return new PartnerGroupLink
        {
            Id = Guid.CreateVersion7(at),
            EventId = eventId,
            EventPartnerId = eventPartnerId,
            TokenHash = tokenHash,
            CreatedAt = at,
            CreatedById = actorId,
        };
    }

    public void Revoke(string reason, Guid actorId, DateTimeOffset at)
    {
        if (IsRevoked) throw new DomainException("This link is already revoked.");
        RevokeReason = SponsorRules.Required(reason, "Reason", ReasonMaxLength);
        RevokedAt = at;
        RevokedById = actorId;
    }
}
