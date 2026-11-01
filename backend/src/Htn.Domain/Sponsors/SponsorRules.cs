using Htn.Domain.Common;

namespace Htn.Domain.Sponsors;

/// <summary>Shared guard clauses for the sponsors/partners entities, so every message reads the same way.</summary>
internal static class SponsorRules
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

    /// <summary>PHP, strictly positive, at most 2 decimal places.</summary>
    public static decimal Money(decimal amount, string field = "Amount")
    {
        if (amount <= 0m) throw new DomainException($"{field} must be more than 0.");
        if (decimal.Round(amount, 2) != amount) throw new DomainException($"{field} can have at most 2 decimal places.");
        return amount;
    }

    public static void Defined<T>(T value, string what) where T : struct, Enum
    {
        if (!Enum.IsDefined(value)) throw new DomainException($"Unknown {what}.");
    }

    public static int Count(int value, string field, int min, int max)
    {
        if (value < min || value > max) throw new DomainException($"{field} must be between {min} and {max}.");
        return value;
    }

    /// <summary>A website is optional, but when given it must be an http(s) URL.</summary>
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
}
