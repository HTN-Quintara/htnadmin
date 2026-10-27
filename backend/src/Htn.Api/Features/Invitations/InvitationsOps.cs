using Htn.Api.Common;
using Htn.Domain.Directory;
using Htn.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Htn.Api.Features.Invitations;

/// <summary>The <c>code</c> extension on every invitations problem response.</summary>
public static class InvitationErrorCodes
{
    public const string ContactNotVerified = "contact_not_verified";
    public const string AlreadySent = "already_sent";
    public const string InvalidTransition = "invalid_transition";
}

/// <summary>Audit action strings, <c>&lt;entity&gt;.&lt;verb&gt;</c> like the rest of the system.</summary>
public static class InvitationAuditActions
{
    public const string TemplateUpdate = "invitation_template.update";
    public const string Create = "invitation.create";
    public const string Send = "invitation.send";
    public const string StatusAdvance = "invitation.status";
    public const string Delete = "invitation.delete";
}

public static class InvitationsOps
{
    public static IResult Conflict(string detail, string code) =>
        Problems.Result(StatusCodes.Status409Conflict, "That cannot be done right now.", detail, code);

    /// <summary>
    /// The org's currently-live primary contact (or, lacking one, null) and whether it needs a check right now —
    /// the same "verify first" rule the directory list uses, re-checked live rather than trusted from the client.
    /// </summary>
    public static async Task<DirectoryContact?> PrimaryContactAsync(HtnDbContext db, Guid orgId, CancellationToken ct) =>
        await db.DirectoryContacts.FirstOrDefaultAsync(c => c.OrgId == orgId && c.VoidedAt == null && c.IsPrimary, ct);

    public static bool IsVerified(DirectoryContact? contact, DateTimeOffset now) =>
        contact is not null && !contact.NeedsCheck(now);
}
