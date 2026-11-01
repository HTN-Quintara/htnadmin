using Htn.Domain.Common;

namespace Htn.Domain.Merch;

/// <summary>Shared guard clauses for the merch entities, mirroring <c>SupplierRules</c>/<c>DirectoryRules</c>.</summary>
internal static class MerchRules
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

    /// <summary>PHP, zero or positive, at most 2 decimal places.</summary>
    public static decimal NonNegativeMoney(decimal amount, string field)
    {
        if (amount < 0m) throw new DomainException($"{field} cannot be negative.");
        if (decimal.Round(amount, 2) != amount) throw new DomainException($"{field} can have at most 2 decimal places.");
        return amount;
    }

    public static int Pct(int value, string field)
    {
        if (value < 0 || value > 100) throw new DomainException($"{field} must be between 0 and 100.");
        return value;
    }
}
