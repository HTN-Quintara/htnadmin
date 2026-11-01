using Htn.Domain.Common;

namespace Htn.Domain.Speakers;

/// <summary>
/// A speaker's confirmation link (frame 22/23): a random 128+-bit token plus a 6-digit email OTP. Deliberately a
/// sibling table to <c>AccountToken</c>, not an extension of it — a speaker has no login, and this link is
/// multi-use ("use the same link; you'll get a new code"), the opposite of <c>AccountToken</c>'s single-use design.
/// Only hashes are stored; the raw token lives only in the emailed URL and <c>OutboxMessage.Secret</c>.
/// <para>
/// The OTP state is held entirely on this row so the 5-try lock and the resend budget survive restarts and multiple
/// API replicas: every verify attempt is one atomic conditional UPDATE (see <c>SpeakerLinksOps</c>), not a check
/// here followed by a separate write.
/// </para>
/// </summary>
public sealed class SpeakerLink
{
    public const int TokenHashLength = 64;
    public const int ReasonMaxLength = 500;
    public const int OtpAttemptLimit = 5;
    public static readonly TimeSpan OtpLifetime = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan LinkLifetime = TimeSpan.FromDays(14);
    public static readonly TimeSpan SessionLifetime = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan ResendWindow = TimeSpan.FromMinutes(10);
    public const int ResendPerWindow = 3;
    public const int ResendLifetimeLimit = 10;

    private SpeakerLink() { }

    public Guid Id { get; private set; }
    public Guid SpeakerId { get; private set; }
    public Guid EventId { get; private set; }
    public string TokenHash { get; private set; } = default!;
    public DateTimeOffset CreatedAt { get; private set; }
    public Guid? CreatedById { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? SentAt { get; private set; }
    public int SendCount { get; private set; }
    public DateTimeOffset? FirstOpenedAt { get; private set; }
    public DateTimeOffset? LastOpenedAt { get; private set; }
    public DateTimeOffset? SubmittedAt { get; private set; }
    public DateTimeOffset? LastSubmittedAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public Guid? RevokedById { get; private set; }
    public string? RevokeReason { get; private set; }

    public string? OtpHash { get; private set; }
    public DateTimeOffset? OtpSentAt { get; private set; }
    public DateTimeOffset? OtpExpiresAt { get; private set; }
    public int OtpAttempts { get; private set; }
    public DateTimeOffset? OtpLockedUntil { get; private set; }
    public DateTimeOffset? OtpWindowStartedAt { get; private set; }
    public int OtpSendCount { get; private set; }

    public string? SessionTokenHash { get; private set; }
    public DateTimeOffset? SessionExpiresAt { get; private set; }

    public bool IsRevoked => RevokedAt is not null;
    public bool IsExpired(DateTimeOffset at) => at >= ExpiresAt;
    public bool IsLocked(DateTimeOffset at) => OtpLockedUntil is { } locked && at < locked;
    public bool HasSubmitted => SubmittedAt is not null;

    /// <summary>Issues a fresh link. The caller hashes the raw token with <c>AccountTokenService.Hash</c> (or the shared helper).</summary>
    public static SpeakerLink Create(Guid speakerId, Guid eventId, string tokenHash, Guid actorId, DateTimeOffset at)
    {
        if (speakerId == Guid.Empty) throw new DomainException("A speaker is required.");
        if (tokenHash is not { Length: TokenHashLength }) throw new DomainException("Invalid token.");
        return new SpeakerLink
        {
            Id = Guid.CreateVersion7(at),
            SpeakerId = speakerId,
            EventId = eventId,
            TokenHash = tokenHash,
            CreatedAt = at,
            CreatedById = actorId,
            ExpiresAt = at + LinkLifetime,
        };
    }

    public void MarkSent(DateTimeOffset at)
    {
        if (SendCount >= ResendLifetimeLimit) throw new DomainException("This link has been resent the maximum number of times. Revoke and reissue it.");
        SentAt ??= at;
        SendCount++;
    }

    public void Revoke(string reason, Guid actorId, DateTimeOffset at)
    {
        if (IsRevoked) throw new DomainException("This link is already revoked.");
        RevokeReason = Required(reason, "Reason", ReasonMaxLength);
        RevokedAt = at;
        RevokedById = actorId;
        OtpHash = null;
        OtpExpiresAt = null;
        SessionTokenHash = null;
        SessionExpiresAt = null;
    }

    /// <summary>
    /// Requests a fresh OTP, budgeted per link: at most <see cref="ResendPerWindow"/> per rolling
    /// <see cref="ResendWindow"/>, and at most <see cref="ResendLifetimeLimit"/> ever. A fresh code resets the
    /// attempt counter, which is safe only because the resend budget is itself limited — the lock cannot be
    /// defeated by resending.
    /// </summary>
    public bool TryIssueOtp(string otpHash, DateTimeOffset at)
    {
        if (otpHash is not { Length: TokenHashLength }) throw new DomainException("Invalid code.");
        if (IsLocked(at)) return false;
        if (OtpWindowStartedAt is null || at - OtpWindowStartedAt >= ResendWindow)
        {
            OtpWindowStartedAt = at;
            OtpSendCount = 0;
        }
        if (OtpSendCount >= ResendPerWindow) return false;
        if (OtpSendCount >= ResendLifetimeLimit) return false;
        OtpHash = otpHash;
        OtpSentAt = at;
        OtpExpiresAt = at + OtpLifetime;
        OtpAttempts = 0;
        OtpSendCount++;
        return true;
    }

    /// <summary>Clears an issued session, e.g. when a new OTP is requested while a form session is open.</summary>
    public void ClearSession()
    {
        SessionTokenHash = null;
        SessionExpiresAt = null;
    }

    public void OpenSession(string sessionTokenHash, DateTimeOffset at)
    {
        if (sessionTokenHash is not { Length: TokenHashLength }) throw new DomainException("Invalid session.");
        FirstOpenedAt ??= at;
        LastOpenedAt = at;
        SessionTokenHash = sessionTokenHash;
        SessionExpiresAt = at + SessionLifetime;
        OtpHash = null;
        OtpExpiresAt = null;
    }

    public bool SessionIsValid(string sessionTokenHash, DateTimeOffset at) =>
        !IsRevoked && SessionTokenHash == sessionTokenHash && SessionExpiresAt is { } exp && at < exp;

    public void MarkSubmitted(DateTimeOffset at)
    {
        SubmittedAt ??= at;
        LastSubmittedAt = at;
    }

    private static string Required(string? value, string field, int max)
    {
        var v = value?.Trim();
        if (string.IsNullOrEmpty(v)) throw new DomainException($"{field} is required.");
        if (v.Length > max) throw new DomainException($"{field} must be at most {max} characters.");
        return v;
    }
}
