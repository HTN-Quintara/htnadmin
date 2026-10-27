using Htn.Domain.Sponsors;

namespace Htn.Api.Features.Sponsors;

// Requests: every property nullable so FluentValidation, not the model binder, produces the message.

public sealed record SponsorTierRequest(
    TierClass? Class, string? Name, decimal? CashAmount, string? PriceNote,
    string? InKindText, int? InKindItemCount, int? Slots, int? CompedSeats, int? SortOrder, IReadOnlyList<string>? Perks);

public sealed record SponsorRequest(
    SponsorKind? Kind, string? Name, string? DisplayName, bool? IsAnonymous,
    string? ContactName, string? ContactRole, string? ContactEmail, string? ContactMobile,
    Guid? TierId, decimal? CashAmount, string? InKindDescription, int? InKindItemCount,
    int? CompedSeats, string? WebsiteUrl);

public sealed record ReasonRequest(string? Reason);

public sealed record CashReceivedRequest(Guid? PaymentAccountId, DateOnly? ReceivedOn, string? Reference);

public sealed record SponsorPerkRequest(string? Text);

public sealed record PerkDeliveredRequest(bool? Delivered);

public sealed record PartnerSettingsRequest(int? Threshold, PartnerCountBasis? CountBasis, bool? GroupLinkEnabled);

public sealed record TagPartnerRequest(string? Name, string? OrgType);

public sealed record PartnerWebsiteRequest(string? WebsiteUrl);

// Responses.

public sealed record SponsorKpiDto(string Label, string Value, string? Sub);

public sealed record SponsorTierDto(
    Guid Id, TierClass Class, string Name, string PriceText, int Slots, int SlotsTaken,
    int CompedSeats, IReadOnlyList<string> Perks, bool IsActive);

public sealed record SponsorPerkDto(Guid Id, string Text, bool Delivered, bool Removed, int SortOrder);

public sealed record SponsorDto(
    Guid Id, SponsorKind Kind, string Name, string? DisplayName, bool IsAnonymous,
    string? ContactName, string? ContactRole, string? ContactEmail, string? ContactMobile,
    SponsorStatus Status, Guid? TierId, TierClass? TierClass, string? TierName,
    string ContributionType, decimal? CashAmount, string? InKindDescription, int? InKindItemCount,
    string AmountText, Guid? LogoFileId, string? WebsiteUrl, bool NeedsAssets,
    int CompsUsed, int CompsEntitlement, int PerksDelivered, int PerksTotal,
    bool CashReceived, DateTimeOffset? CashReceivedAt, string? CashReference);

public sealed record SponsorsPageDto(
    Guid EventId, string EventName, IReadOnlyList<SponsorKpiDto> Kpis,
    IReadOnlyList<SponsorTierDto> Tiers, IReadOnlyList<SponsorDto> Sponsors);

public sealed record SponsorDetailDto(SponsorDto Sponsor, IReadOnlyList<SponsorPerkDto> Perks);

public sealed record PartnerSettingsDto(int Threshold, PartnerCountBasis CountBasis, bool GroupLinkEnabled);

public sealed record PartnerDto(
    Guid? Id, string NameKey, string DisplayName, string? OrgType,
    int Registered, int Paid, int CheckedIn, int CountForBasis,
    string Status, int Remaining, bool IsPartner, bool IsTagged,
    string Assets, Guid? LogoFileId, string? WebsiteUrl, bool HasGroupLink, int GroupJoined);

public sealed record UnmatchedOrgDto(string NameKey, string SampleName, int Registered, int Paid);

public sealed record PartnersPageDto(
    PartnerSettingsDto Settings, IReadOnlyList<PartnerDto> Partners,
    IReadOnlyList<UnmatchedOrgDto> Unmatched, IReadOnlyList<string> PartnerPerks);

public sealed record GroupLinkDto(string Url);
