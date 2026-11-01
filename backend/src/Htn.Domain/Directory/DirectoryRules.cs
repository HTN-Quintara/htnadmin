using Htn.Domain.Common;

namespace Htn.Domain.Directory;

/// <summary>Shared guard clauses for the directory entities, so every message reads the same way.</summary>
internal static class DirectoryRules
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

    public static void Defined<T>(T value, string what) where T : struct, Enum
    {
        if (!Enum.IsDefined(value)) throw new DomainException($"Unknown {what}.");
    }

    public static string? Website(string? value, int max)
    {
        var v = value?.Trim();
        if (string.IsNullOrEmpty(v)) return null;
        if (v.Length > max) throw new DomainException($"Website must be at most {max} characters.");
        if (!v.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !v.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            throw new DomainException("Website must start with http:// or https://");
        return v;
    }

    // A light RFC-ish check: the real validation lives in the endpoint validator; this only guards the invariant.
    public static bool LooksLikeEmail(string value) =>
        value.Contains('@', StringComparison.Ordinal) && !value.Contains(' ');
}
