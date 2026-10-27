namespace Htn.Domain.Speakers;

/// <summary>Builds the plus-one voucher's name segment and full code (<c>SPK-&lt;NAME&gt;-&lt;4 random&gt;</c>).</summary>
public static class PlusOneCode
{
    public const string FallbackSegment = "GUEST";
    public const string Prefix = "SPK";

    /// <summary>
    /// The last word of the name, transliterated to <c>[A-Z0-9]</c>. The app runs with InvariantGlobalization, so
    /// <c>string.Normalize</c> can't decompose accents — folded by the same table <see cref="Events.EventSlug"/>
    /// uses for public slugs. Truncated to <see cref="SpeakerPlusOne.NameSegmentMaxLength"/>. Falls back to "GUEST"
    /// when nothing alphanumeric survives (a name written entirely in a non-Latin script), so the code is never malformed.
    /// </summary>
    public static string Segment(string name)
    {
        var words = name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var last = words.Length > 0 ? words[^1] : name;
        var sb = new System.Text.StringBuilder(last.Length);
        foreach (var ch in last)
        {
            var lower = char.ToLowerInvariant(ch);
            var folded = Events.EventSlug.Fold.TryGetValue(lower, out var f) ? f : lower.ToString();
            foreach (var fc in folded)
            {
                var upper = char.ToUpperInvariant(fc);
                if (upper is >= 'A' and <= 'Z' or >= '0' and <= '9') sb.Append(upper);
            }
            if (sb.Length >= SpeakerPlusOne.NameSegmentMaxLength) break;
        }
        var result = sb.ToString();
        if (result.Length > SpeakerPlusOne.NameSegmentMaxLength) result = result[..SpeakerPlusOne.NameSegmentMaxLength];
        return result.Length == 0 ? FallbackSegment : result;
    }

    /// <summary>The full code: <c>SPK-&lt;segment&gt;-&lt;tail&gt;</c>, matching the voucher code pattern.</summary>
    public static string Code(string segment, string tail) => $"{Prefix}-{segment}-{tail}";
}
