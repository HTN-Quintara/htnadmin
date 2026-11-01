using System.Globalization;

namespace Htn.Domain.Invitations;

/// <summary>What the merge needs about the addressee; the caller builds this from a live <c>DirectoryOrg</c>/<c>DirectoryContact</c>.</summary>
public sealed record InvitationAddressee(string Name, string Position, string? Salutation);

/// <summary>What one merge produces. Frozen onto the invitation's snapshot fields once sent.</summary>
public sealed record MergedLetter(string Subject, string Body);

/// <summary>
/// Merges an <see cref="EventInvitationTemplate"/> against one addressee and an event date, substituting the
/// literal tokens <c>[date]</c>, <c>[addressee.name]</c>, <c>[addressee.position]</c>, <c>[addressee.salutation]</c>.
/// Used both for the no-op preview endpoint and for the actual send, so preview and sent snapshot can never drift.
/// </summary>
public static class InvitationMerge
{
    public static MergedLetter Merge(string subject, string body, InvitationAddressee addressee, DateTimeOffset eventDate)
    {
        ArgumentNullException.ThrowIfNull(addressee);
        var dateText = eventDate.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture);
        var salutation = string.IsNullOrWhiteSpace(addressee.Salutation) ? "Sir/Madam" : addressee.Salutation;
        return new MergedLetter(Replace(subject, dateText, addressee, salutation), Replace(body, dateText, addressee, salutation));
    }

    private static string Replace(string text, string dateText, InvitationAddressee addressee, string salutation) =>
        text
            .Replace("[date]", dateText, StringComparison.OrdinalIgnoreCase)
            .Replace("[addressee.name]", addressee.Name, StringComparison.OrdinalIgnoreCase)
            .Replace("[addressee.position]", addressee.Position, StringComparison.OrdinalIgnoreCase)
            .Replace("[addressee.salutation]", salutation, StringComparison.OrdinalIgnoreCase);
}
