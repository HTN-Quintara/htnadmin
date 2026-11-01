using Htn.Domain.Common;

namespace Htn.Domain.Finance;

/// <summary>Shared guard clauses for the finance entities, so every message reads the same way.</summary>
internal static class FinanceRules
{
    /// <summary>A trimmed, non-empty value within <paramref name="max"/>.</summary>
    public static string Required(string? value, string field, int max)
    {
        var v = value?.Trim();
        if (string.IsNullOrEmpty(v)) throw new DomainException($"{field} is required.");
        if (v.Length > max) throw new DomainException($"{field} must be at most {max} characters.");
        return v;
    }

    /// <summary>A trimmed value within <paramref name="max"/>, or null when it was blank.</summary>
    public static string? Optional(string? value, string field, int max)
    {
        var v = value?.Trim();
        if (string.IsNullOrEmpty(v)) return null;
        if (v.Length > max) throw new DomainException($"{field} must be at most {max} characters.");
        return v;
    }

    /// <summary>PHP, strictly positive, at most 2 decimal places. Money never moves by zero.</summary>
    public static decimal Money(decimal amount, string field = "Amount")
    {
        if (amount <= 0m) throw new DomainException($"{field} must be more than 0.");
        if (decimal.Round(amount, 2) != amount) throw new DomainException($"{field} can have at most 2 decimal places.");
        return amount;
    }

    /// <summary>PHP, any sign (an opening balance or a statement balance may be negative), at most 2 decimal places.</summary>
    public static decimal SignedMoney(decimal amount, string field)
    {
        if (decimal.Round(amount, 2) != amount) throw new DomainException($"{field} can have at most 2 decimal places.");
        return amount;
    }

    public static void Defined<T>(T value, string what) where T : struct, Enum
    {
        if (!Enum.IsDefined(value)) throw new DomainException($"Unknown {what}.");
    }

    public static (int Year, int Month) Period(int year, int month)
    {
        if (year is < 2020 or > 2100) throw new DomainException("Year is out of range.");
        if (month is < 1 or > 12) throw new DomainException("Month must be 1 to 12.");
        return (year, month);
    }
}
