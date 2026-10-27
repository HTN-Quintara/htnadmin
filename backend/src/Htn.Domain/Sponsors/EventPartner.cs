using Htn.Domain.Common;

namespace Htn.Domain.Sponsors;

/// <summary>
/// A manually tagged partner organization, and the only partner data that is stored: the registrant count and
/// "partner" status are derived at read time from registrations grouped by company. Untagged with a reason, never
/// deleted. (Phase 7 adds the link to the global Directory; until then <see cref="NameKey"/> is the match.)
/// </summary>
public sealed class EventPartner
{
    public const int NameMaxLength = 160;
    public const int OrgTypeMaxLength = 80;
    public const int WebsiteMaxLength = 300;
    public const int ReasonMaxLength = 500;

    private EventPartner() { }

    public Guid Id { get; private set; }
    public Guid EventId { get; private set; }
    public string NameKey { get; private set; } = default!;
    public string DisplayName { get; private set; } = default!;
    public string? OrgType { get; private set; }
    public DateTimeOffset TaggedAt { get; private set; }
    public Guid? TaggedById { get; private set; }
    public DateTimeOffset? UntaggedAt { get; private set; }
    public string? UntagReason { get; private set; }
    public Guid? LogoFileId { get; private set; }
    public string? WebsiteUrl { get; private set; }

    /// <summary>Phase 7 Directory join, populated by <c>DirectoryOps.BackfillAsync</c> matching on <see cref="NameKey"/>.</summary>
    public Guid? DirectoryOrgId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public Guid? CreatedById { get; private set; }

    public bool IsTagged => UntaggedAt is null;

    public static EventPartner Tag(
        Guid eventId, string displayName, string? orgType, Guid actorId, DateTimeOffset at)
    {
        if (eventId == Guid.Empty) throw new DomainException("An event is required.");
        var name = SponsorRules.Required(displayName, "Name", NameMaxLength);
        return new EventPartner
        {
            Id = Guid.CreateVersion7(at),
            EventId = eventId,
            DisplayName = name,
            NameKey = Htn.Domain.Common.NameKey.Of(name) ?? throw new DomainException("Name must contain letters or numbers."),
            OrgType = SponsorRules.Optional(orgType, "Type", OrgTypeMaxLength),
            TaggedAt = at,
            TaggedById = actorId,
            CreatedAt = at,
            CreatedById = actorId,
        };
    }

    public void Untag(string reason, DateTimeOffset at)
    {
        if (!IsTagged) throw new DomainException("This organization is not tagged as a partner.");
        UntagReason = SponsorRules.Required(reason, "Reason", ReasonMaxLength);
        UntaggedAt = at;
    }

    public void SetLogo(Guid? logoFileId)
    {
        if (!IsTagged) throw new DomainException("This organization is not tagged as a partner.");
        LogoFileId = logoFileId;
    }

    public void SetWebsite(string? websiteUrl)
    {
        if (!IsTagged) throw new DomainException("This organization is not tagged as a partner.");
        WebsiteUrl = SponsorRules.Website(websiteUrl, WebsiteMaxLength);
    }
}
