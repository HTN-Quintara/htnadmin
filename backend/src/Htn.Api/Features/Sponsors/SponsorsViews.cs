using System.Globalization;
using Htn.Domain.Sponsors;

namespace Htn.Api.Features.Sponsors;

/// <summary>Projections from the sponsor entities to the DTOs the screen renders. Display strings are composed here.</summary>
public static class SponsorsViews
{
    public static string Peso(decimal amount) => "₱" + amount.ToString("#,##0.##", CultureInfo.InvariantCulture);

    public static string ContributionType(decimal? cash, string? inKind) =>
        cash is not null && inKind is not null ? "Cash + In-kind"
        : cash is not null ? "Cash"
        : inKind is not null ? "In-kind"
        : "—";

    public static string AmountText(decimal? cash, string? inKind, int? inKindCount)
    {
        var parts = new List<string>(2);
        if (cash is { } c) parts.Add(Peso(c));
        if (inKind is not null) parts.Add(inKindCount is { } n ? $"{n} {inKind}" : inKind);
        return parts.Count == 0 ? "—" : string.Join(" + ", parts);
    }

    public static string TierPrice(SponsorTier t)
    {
        var parts = new List<string>(2);
        if (t.CashAmount is { } c) parts.Add(Peso(c) + (t.PriceNote is { } note ? $" {note}" : ""));
        if (t.InKindText is not null) parts.Add(parts.Count == 0 ? $"In-kind: {t.InKindText}" : t.InKindText);
        return parts.Count == 0 ? "—" : string.Join(" + ", parts);
    }

    public static SponsorTierDto Tier(SponsorTier t, int slotsTaken, IReadOnlyList<string> perks) =>
        new(t.Id, t.Class, t.Name, TierPrice(t), t.Slots, slotsTaken, t.CompedSeats, perks, t.IsActive);

    public static SponsorPerkDto Perk(SponsorPerk p) =>
        new(p.Id, p.Text, p.IsDelivered, p.IsRemoved, p.SortOrder);

    public static SponsorDto Sponsor(
        Sponsor s, TierClass? tierClass, string? tierName, int compsUsed, int perksDelivered, int perksTotal)
    {
        var needsAssets = s.Kind == SponsorKind.Organization && s.IsConfirmed
                          && (s.LogoFileId is null || s.WebsiteUrl is null);
        return new SponsorDto(
            s.Id, s.Kind, s.Name, s.DisplayName, s.IsAnonymous,
            s.ContactName, s.ContactRole, s.ContactEmail, s.ContactMobile,
            s.Status, s.TierId, tierClass, tierName,
            ContributionType(s.CashAmount, s.InKindDescription), s.CashAmount, s.InKindDescription, s.InKindItemCount,
            AmountText(s.CashAmount, s.InKindDescription, s.InKindItemCount),
            s.LogoFileId, s.WebsiteUrl, needsAssets,
            compsUsed, s.CompedSeats, perksDelivered, perksTotal,
            s.CashPosted, s.CashReceivedAt, s.CashReference);
    }
}
