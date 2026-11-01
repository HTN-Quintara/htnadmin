using Htn.Domain.Directory;

namespace Htn.Api.Features.Directory;

/// <summary>Projections from the directory entities to the DTOs the screen renders.</summary>
public static class DirectoryViews
{
    public static IReadOnlyList<string> SplitCc(string? ccEmails) =>
        string.IsNullOrEmpty(ccEmails) ? [] : ccEmails.Split(';', StringSplitOptions.RemoveEmptyEntries);

    public static DirectoryContactDto Contact(DirectoryContact c, DateTimeOffset now) => new(
        c.Id, c.OrgId, c.Position, c.HolderName, c.Email, c.Phone,
        c.IsPrimary, c.VerifiedAt, c.NeedsCheck(now), SplitCc(c.CcEmails));

    public static DirectoryContactHistoryDto History(DirectoryContactHistory h) => new(
        h.Id, h.ContactId, h.Kind, h.At, h.ById, h.Note, h.PriorPosition, h.PriorHolderName);

    /// <summary>An org needs a check when its primary contact (or, lacking one, every live contact) needs one.</summary>
    public static DirectoryOrgDto Org(DirectoryOrg o, IReadOnlyList<DirectoryContact> liveContacts, DateTimeOffset now)
    {
        var primary = liveContacts.FirstOrDefault(c => c.IsPrimary);
        var needsCheck = primary is not null ? primary.NeedsCheck(now)
            : liveContacts.Count > 0 && liveContacts.All(c => c.NeedsCheck(now));
        return new(o.Id, o.Kind, o.Name, o.NameKey, o.AddresseeSalutation, o.WebsiteUrl, o.LogoFileId, o.Notes, o.IsActive, needsCheck);
    }
}
