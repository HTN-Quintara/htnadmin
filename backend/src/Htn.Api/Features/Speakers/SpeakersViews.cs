using Htn.Domain.Common;
using Htn.Domain.Speakers;

namespace Htn.Api.Features.Speakers;

public static class SpeakersViews
{
    /// <summary>The six statuses screen 22 shows, derived — never stored — from the link's timestamps.</summary>
    public static (string State, string? Sub) LinkState(SpeakerLink? link, DateTimeOffset now)
    {
        if (link is null) return ("Not sent", null);
        if (link.IsRevoked) return ("Revoked", "Revoked · link no longer works");
        if (link.IsExpired(now)) return ("Expired", "Not opened in 14 days");
        if (link.HasSubmitted) return ("Submitted", Date(link.SubmittedAt));
        if (link.FirstOpenedAt is not null) return ("Opened", $"Code verified {Date(link.LastOpenedAt)}");
        return ("Sent", $"{Date(link.SentAt)} · expires {Date(link.ExpiresAt)}");
    }

    public static bool CanSend(SpeakerLink? link, DateTimeOffset now) =>
        link is null || link.IsRevoked || link.IsExpired(now);

    private static string Date(DateTimeOffset? at) =>
        at is { } v ? ManilaTime.ToManila(v).ToString("MMM d") : "—";
}
