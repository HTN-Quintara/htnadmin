using Htn.Domain.Common;

namespace Htn.Domain.Suppliers;

/// <summary>
/// The fixed tag vocabulary for a <see cref="SupplierRating"/> (Phase 7C domain rule). Stored as a semicolon-delimited
/// string on the row — the same convention already used for <see cref="Htn.Domain.Directory.DirectoryContact.CcEmails"/>
/// — rather than a Postgres <c>text[]</c> column: there is no array-column precedent anywhere else in this schema, so
/// the brief's documented default (delimited string) is what this uses.
/// </summary>
public static class SupplierRatingTags
{
    public const string OnTime = "OnTime";
    public const string GoodQuality = "GoodQuality";
    public const string EasyToReach = "EasyToReach";
    public const string FairPrice = "FairPrice";
    public const string Late = "Late";
    public const string BelowExpectations = "BelowExpectations";

    public static readonly IReadOnlyList<string> All = [OnTime, GoodQuality, EasyToReach, FairPrice, Late, BelowExpectations];
}

/// <summary>What one rating submission carries.</summary>
public sealed record NewSupplierRating(int Stars, IReadOnlyList<string>? Tags, HireAgain HireAgain, string? Note);

/// <summary>
/// One committee lead's (or the board's) post-event rating of an <see cref="EventSupplier"/> hire — Phase 7C.
/// <b>Fully append-only</b>, like <see cref="Htn.Domain.Tasks.PointsAward"/>: a rating is never edited or deleted
/// once submitted, enforced again at the database (<c>Suppliers_Guards</c>). One rating per (hire, rater):
/// <c>ux_supplier_ratings_hire_rater</c> — the caller checks for an existing row and returns a conflict rather than
/// upserting, since "the rating shown is the average" only makes sense over independent, permanent submissions.
/// </summary>
public sealed class SupplierRating
{
    public const int NoteMaxLength = 500;
    public const int TagsMaxLength = 200;

    private SupplierRating() { }

    public Guid Id { get; private set; }
    public Guid EventSupplierId { get; private set; }
    public Guid RaterId { get; private set; }
    public int Stars { get; private set; }

    /// <summary>Semicolon-delimited subset of <see cref="SupplierRatingTags.All"/>; null when none were picked.</summary>
    public string? Tags { get; private set; }

    public HireAgain HireAgain { get; private set; }
    public string? Note { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static SupplierRating Create(Guid eventSupplierId, Guid raterId, NewSupplierRating data, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (eventSupplierId == Guid.Empty) throw new DomainException("A hire is required.");
        if (raterId == Guid.Empty) throw new DomainException("A rater is required.");
        if (data.Stars is < 1 or > 5) throw new DomainException("Stars must be between 1 and 5.");
        SupplierRules.Defined(data.HireAgain, "hire-again answer");
        return new SupplierRating
        {
            Id = Guid.CreateVersion7(at),
            EventSupplierId = eventSupplierId,
            RaterId = raterId,
            Stars = data.Stars,
            Tags = NormalizeTags(data.Tags),
            HireAgain = data.HireAgain,
            Note = SupplierRules.Optional(data.Note, "Note", NoteMaxLength),
            CreatedAt = at,
        };
    }

    private static string? NormalizeTags(IReadOnlyList<string>? tags)
    {
        if (tags is null || tags.Count == 0) return null;
        var cleaned = tags.Select(t => t.Trim()).Where(t => t.Length > 0).Distinct().ToList();
        if (cleaned.Count == 0) return null;
        foreach (var t in cleaned)
            if (!SupplierRatingTags.All.Contains(t)) throw new DomainException($"'{t}' is not a known rating tag.");
        var joined = string.Join(';', cleaned);
        if (joined.Length > TagsMaxLength) throw new DomainException($"Tags must be at most {TagsMaxLength} characters combined.");
        return joined;
    }
}
