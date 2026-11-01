namespace Htn.Domain.Common;

/// <summary>
/// Normalizes an organization name to a stable match key, so a sponsor, a partner and a registration's
/// agency/company field can be grouped and (from Phase 7) matched to one Directory organization. Must stay in step
/// with the <c>company_key</c> generated column on <c>attendees</c>:
/// <c>nullif(regexp_replace(lower(btrim(x)), '[^a-z0-9]+', ' ', 'g'), '')</c>.
/// </summary>
public static class NameKey
{
    public const int MaxLength = 160;

    /// <summary>Lower-cased, every run of non-alphanumeric characters collapsed to one space, trimmed. Null when empty.</summary>
    public static string? Of(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var lower = value.ToLowerInvariant();
        var sb = new System.Text.StringBuilder(lower.Length);
        var pendingSpace = false;
        foreach (var ch in lower)
        {
            if (ch is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                if (pendingSpace && sb.Length > 0) sb.Append(' ');
                pendingSpace = false;
                sb.Append(ch);
            }
            else
            {
                pendingSpace = true;
            }
        }
        var key = sb.ToString();
        if (key.Length == 0) return null;
        return key.Length > MaxLength ? key[..MaxLength] : key;
    }
}
