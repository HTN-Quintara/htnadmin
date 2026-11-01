using System.Globalization;
using Htn.Domain.Common;
using Htn.Domain.Leads;

namespace Htn.Api.Features.Leads;

public static class LeadsViews
{
    private static readonly LeadStatus[] Pipeline =
        [LeadStatus.Prospect, LeadStatus.Contacted, LeadStatus.InTalks, LeadStatus.Confirmed, LeadStatus.Declined];

    public static readonly IReadOnlyDictionary<LeadStatus, string> StatusNames = new Dictionary<LeadStatus, string>
    {
        [LeadStatus.Prospect] = "Prospect",
        [LeadStatus.Contacted] = "Contacted",
        [LeadStatus.InTalks] = "In talks",
        [LeadStatus.Confirmed] = "Confirmed",
        [LeadStatus.Declined] = "Declined",
    };

    public static string Peso(decimal amount) => "₱" + amount.ToString("#,##0.##", CultureInfo.InvariantCulture);

    public static string Ask(Lead l)
    {
        if (l.Kind != LeadKind.Sponsor) return l.AskNote ?? "";
        var parts = new List<string>(2);
        if (l.AskNote is not null) parts.Add(l.AskNote);
        if (l.AskAmount is { } a) parts.Add(Peso(a));
        return parts.Count == 0 ? "" : "Ask: " + string.Join(" · ", parts);
    }

    public static LeadCardDto Card(Lead l, DateOnly today, string? askTierName, string? ownerName)
    {
        var overdue = l.NextStepDue is { } due && due < today
                      && l.Status is not (LeadStatus.Confirmed or LeadStatus.Declined);
        return new LeadCardDto(
            l.Id, l.Kind, l.Status, l.Subject,
            l.ContactName, l.ContactRole, l.ContactEmail, l.ContactMobile,
            l.AskTierId, askTierName, l.AskAmount, l.AskNote, Ask(l),
            l.OwnerPersonId, ownerName, Initials(ownerName),
            l.NextStep, l.NextStepDue, overdue, l.Source == LeadSource.Directory,
            l.SponsorId, l.SpeakerId);
    }

    public static List<LeadColumnDto> Columns(IEnumerable<LeadCardDto> cards)
    {
        var byStatus = cards.GroupBy(c => c.Status).ToDictionary(g => g.Key, g => g.ToList());
        return Pipeline.Select(s =>
        {
            var list = byStatus.GetValueOrDefault(s, []);
            return new LeadColumnDto(s, StatusNames[s], list.Count, list);
        }).ToList();
    }

    private static string? Initials(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var letters = parts.Take(2).Select(p => char.ToUpperInvariant(p[0]));
        return string.Concat(letters);
    }
}
