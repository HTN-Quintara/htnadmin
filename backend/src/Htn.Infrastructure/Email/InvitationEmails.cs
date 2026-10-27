using Htn.Domain.Invitations;
using Htn.Infrastructure.Data;
using Htn.Infrastructure.Email.Templates;
using Microsoft.EntityFrameworkCore;

namespace Htn.Infrastructure.Email;

/// <summary>
/// The invitation letter email, built from the <see cref="EventInvitation"/>'s frozen snapshot — never a live
/// re-merge, since the whole point of the snapshot is that the sent copy can't drift. Mirrors <c>SpeakerEmails</c>'s
/// "re-read current state, return null if obsolete" shape.
/// </summary>
public sealed class InvitationEmails(HtnDbContext db, EmailRenderer renderer)
{
    public async Task<EmailMessage?> InvitationLetterAsync(InvitationLetterEmailPayload p)
    {
        var invitation = await db.EventInvitations.AsNoTracking().SingleOrDefaultAsync(i => i.Id == p.InvitationId);
        // Defensive: this fires right after Send in the same flow, so it should always still be Sent-or-later with
        // a snapshot. If it's somehow not, there's nothing frozen to deliver. The recipient address/name are read
        // from the frozen snapshot too, never re-joined from the live DirectoryContact row — a contact edit between
        // enqueue and delivery must never redirect or re-address a letter whose body already claims it went
        // somewhere/someone else.
        if (invitation is null || invitation.Status == InvitationStatus.NotSent
            || invitation.LetterSubjectSnapshot is null || invitation.LetterBodySnapshot is null
            || invitation.RecipientEmail is null || invitation.RecipientName is null)
            return null;

        var eventName = await db.Events.AsNoTracking().Where(e => e.Id == invitation.EventId).Select(e => e.Name).SingleOrDefaultAsync();
        if (eventName is null) return null;

        var logo = await renderer.LogoAsync();
        var paragraphs = invitation.LetterBodySnapshot
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        var html = await renderer.RenderAsync<InvitationLetterEmail>(new Dictionary<string, object?>
        {
            [nameof(InvitationLetterEmail.Subject)] = invitation.LetterSubjectSnapshot,
            [nameof(InvitationLetterEmail.EventName)] = eventName,
            [nameof(InvitationLetterEmail.Paragraphs)] = paragraphs,
            [nameof(InvitationLetterEmail.BrandColor)] = await renderer.BrandColorAsync(),
            [nameof(InvitationLetterEmail.LogoCid)] = logo?.ContentId,
        });
        var text = string.Join("\n\n", paragraphs) + $"\n\n{Brand.OrgName}";
        return new EmailMessage(invitation.RecipientEmail, invitation.RecipientName, invitation.LetterSubjectSnapshot, html, text,
            EmailCategories.InvitationLetter, logo is null ? null : [logo]);
    }
}
