using System.Security.Cryptography;
using System.Text;
using Htn.Domain.Speakers;
using Htn.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Htn.Api.Features.Speakers;

public static class SpeakerLinkErrorCodes
{
    public const string CodeInvalid = "code_invalid";
    public const string CodeExpired = "code_expired";
    public const string CodeLocked = "code_locked";
    public const string CodeResendLimit = "code_resend_limit";
    public const string LinkExpired = "link_expired";
    public const string SessionRequired = "session_required";
    public const string EventClosed = "event_closed";
    public const string NoLiveLink = "no_live_link";
}

/// <summary>
/// The OTP state machine's race-safe primitives. Every state change that must survive concurrent requests from
/// multiple API replicas is one atomic conditional UPDATE via <c>ExecuteUpdateAsync</c> — never a read, a C# check,
/// then a separate write — the same discipline <c>AccountTokenService.TryConsumeAsync</c> uses for password resets.
/// </summary>
public static class SpeakerLinksOps
{
    /// <summary>SHA-256 of the raw value, lower-hex. Shared by the link token, the OTP (bound to the token) and the session token.</summary>
    public static string Hash(string raw) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));

    /// <summary>The OTP hash is bound to the link's own token, so a code observed on one link can never be replayed on another.</summary>
    public static string OtpHash(string rawToken, string code) => Hash($"{rawToken}:{code}");

    public static string NewOtpCode() => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");

    public static async Task<SpeakerLink?> FindLiveAsync(HtnDbContext db, string tokenHash, CancellationToken ct) =>
        await db.SpeakerLinks.FirstOrDefaultAsync(l => l.TokenHash == tokenHash, ct);

    /// <summary>
    /// One atomic attempt: increments <c>otp_attempts</c> only when the code is live and the link isn't locked,
    /// and only if the submitted code matches what was last issued. Returns what happened, never throws for a wrong
    /// guess. On the 5th consecutive failure the lock is set and the code is cleared in the same statement.
    /// </summary>
    public static async Task<OtpVerifyResult> TryVerifyOtpAsync(
        HtnDbContext db, Guid linkId, string rawToken, string code, DateTimeOffset at, CancellationToken ct)
    {
        var link = await db.SpeakerLinks.AsNoTracking().FirstOrDefaultAsync(l => l.Id == linkId, ct);
        if (link is null || link.IsRevoked) return OtpVerifyResult.NoLiveCode;
        if (link.IsLocked(at)) return OtpVerifyResult.Locked;
        if (link.OtpHash is null || link.OtpExpiresAt is not { } exp || at >= exp) return OtpVerifyResult.Expired;

        var submittedHash = OtpHash(rawToken, code);
        var matches = CryptographicOperations.FixedTimeEquals(
            Convert.FromHexString(submittedHash), Convert.FromHexString(link.OtpHash));

        if (matches)
        {
            // Success: consume the code so it cannot be replayed, only if it is still the live one.
            var rows = await db.SpeakerLinks
                .Where(l => l.Id == linkId && l.OtpHash == link.OtpHash && l.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(l => l.OtpHash, (string?)null).SetProperty(l => l.OtpExpiresAt, (DateTimeOffset?)null), ct);
            return rows == 1 ? OtpVerifyResult.Success : OtpVerifyResult.Expired;
        }

        // Failure: one atomic increment, locking on the 5th. Only applies while this is still the live code.
        var updated = await db.SpeakerLinks
            .Where(l => l.Id == linkId && l.OtpHash == link.OtpHash && l.RevokedAt == null && l.OtpAttempts < SpeakerLink.OtpAttemptLimit)
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.OtpAttempts, l => l.OtpAttempts + 1), ct);
        if (updated == 0) return OtpVerifyResult.Expired;

        var after = await db.SpeakerLinks.AsNoTracking().Where(l => l.Id == linkId).Select(l => l.OtpAttempts).FirstAsync(ct);
        if (after >= SpeakerLink.OtpAttemptLimit)
        {
            var lockedUntil = at + SpeakerLink.LockDuration;
            await db.SpeakerLinks.Where(l => l.Id == linkId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(l => l.OtpLockedUntil, lockedUntil)
                    .SetProperty(l => l.OtpHash, (string?)null)
                    .SetProperty(l => l.OtpExpiresAt, (DateTimeOffset?)null), ct);
            return OtpVerifyResult.JustLocked;
        }
        return OtpVerifyResult.WrongCode(SpeakerLink.OtpAttemptLimit - after);
    }
}

/// <summary>The outcome of one OTP verification attempt.</summary>
public readonly record struct OtpVerifyResult(OtpVerifyOutcome Outcome, int AttemptsLeft)
{
    public static readonly OtpVerifyResult Success = new(OtpVerifyOutcome.Success, 0);
    public static readonly OtpVerifyResult Expired = new(OtpVerifyOutcome.Expired, 0);
    public static readonly OtpVerifyResult Locked = new(OtpVerifyOutcome.Locked, 0);
    public static readonly OtpVerifyResult JustLocked = new(OtpVerifyOutcome.JustLocked, 0);
    public static readonly OtpVerifyResult NoLiveCode = new(OtpVerifyOutcome.Expired, 0);
    public static OtpVerifyResult WrongCode(int attemptsLeft) => new(OtpVerifyOutcome.WrongCode, attemptsLeft);
}

public enum OtpVerifyOutcome { Success, WrongCode, Expired, Locked, JustLocked }
