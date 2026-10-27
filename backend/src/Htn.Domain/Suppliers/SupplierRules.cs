using Htn.Domain.Common;

namespace Htn.Domain.Suppliers;

/// <summary>Shared guard clauses for the suppliers entities, so every message reads the same way (mirrors <c>DirectoryRules</c>).</summary>
internal static class SupplierRules
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

    /// <summary>PHP, zero or positive (a hire may start with nothing paid yet), at most 2 decimal places.</summary>
    public static decimal? NonNegativeMoney(decimal? amount, string field)
    {
        if (amount is null) return null;
        if (amount.Value < 0m) throw new DomainException($"{field} cannot be negative.");
        if (decimal.Round(amount.Value, 2) != amount.Value) throw new DomainException($"{field} can have at most 2 decimal places.");
        return amount;
    }
}
