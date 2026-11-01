using Htn.Api.Common;
using Htn.Domain.Directory;
using Htn.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Htn.Api.Features.Directory;

/// <summary>The <c>code</c> extension on every directory problem response, mirrored in the frontend.</summary>
public static class DirectoryErrorCodes
{
    public const string DuplicateName = "duplicate_name";
    public const string AlreadyDeactivated = "already_deactivated";
    public const string ContactAlreadyVoided = "contact_already_voided";
}

/// <summary>Audit action strings, <c>&lt;entity&gt;.&lt;verb&gt;</c> like the rest of the system.</summary>
public static class DirectoryAuditActions
{
    public const string OrgCreate = "directory_org.create";
    public const string OrgUpdate = "directory_org.update";
    public const string OrgDeactivate = "directory_org.deactivate";
    public const string OrgLogo = "directory_org.logo";
    public const string ContactCreate = "directory_contact.create";
    public const string ContactUpdate = "directory_contact.update";
    public const string ContactStillCorrect = "directory_contact.still_correct";
    public const string ContactHeadChanged = "directory_contact.head_changed";
    public const string ContactVoid = "directory_contact.void";
}

public static class DirectoryOps
{
    public static IResult Conflict(string detail, string code) =>
        Problems.Result(StatusCodes.Status409Conflict, "That cannot be done right now.", detail, code);

    public static Task<bool> DuplicateNameAsync(HtnDbContext db, string nameKey, Guid? exceptId, CancellationToken ct) =>
        db.DirectoryOrgs.AnyAsync(o => o.DeactivatedAt == null && o.NameKey == nameKey
                                        && (exceptId == null || o.Id != exceptId), ct);

    /// <summary>When a contact is (re)made primary, every other live contact of the org loses the flag.</summary>
    public static async Task UnsetOtherPrimariesAsync(
        HtnDbContext db, Guid orgId, Guid exceptContactId, Guid actorId, DateTimeOffset at, CancellationToken ct)
    {
        var others = await db.DirectoryContacts
            .Where(c => c.OrgId == orgId && c.Id != exceptContactId && c.VoidedAt == null && c.IsPrimary)
            .ToListAsync(ct);
        foreach (var other in others)
            other.Update(new NewDirectoryContact(other.Position, other.HolderName, other.Email, other.Phone, false, other.CcEmails),
                actorId, at);
    }
}
