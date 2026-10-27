using Htn.Domain.Common;

namespace Htn.Domain.Invitations;

/// <summary>Shared guard clauses for the invitations entities, so every message reads the same way (mirrors <c>DirectoryRules</c>).</summary>
internal static class InvitationRules
{
    public static string Required(string? value, string field, int max)
    {
        var v = value?.Trim();
        if (string.IsNullOrEmpty(v)) throw new DomainException($"{field} is required.");
        if (v.Length > max) throw new DomainException($"{field} must be at most {max} characters.");
        return v;
    }

    public static string? Optional(string? value, string field, int max)
    {
        var v = value?.Trim();
        if (string.IsNullOrEmpty(v)) return null;
        if (v.Length > max) throw new DomainException($"{field} must be at most {max} characters.");
        return v;
    }
}
