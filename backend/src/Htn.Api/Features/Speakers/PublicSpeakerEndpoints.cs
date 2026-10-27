using Htn.Api.Auth;
using Htn.Api.Common;
using Htn.Domain.Common;
using Htn.Domain.Events;
using Htn.Domain.Files;
using Htn.Domain.Speakers;
using Htn.Infrastructure.Audit;
using Htn.Infrastructure.Data;
using Htn.Infrastructure.Email;
using Htn.Infrastructure.Identity;
using Htn.Infrastructure.Outbox;
using Htn.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Htn.Api.Features.Speakers;

/// <summary>
/// Frame 23: the public speaker confirmation form. Anonymous, rate limited, and every response carries
/// <c>X-Robots-Tag: noindex, nofollow</c>. An unknown token is a bare 404; a known but expired or revoked one is a
/// 410 with one identical body, so the response never tells a holder of a dead link whether it was revoked or just
/// timed out -- including across repeated code requests: the per-email OTP limiter is only ever consulted for a
/// genuinely live link, never for an unknown or dead one, so neither can flip a 404/410 into a 429 that the other
/// can't.
/// </summary>
public static class PublicSpeakerEndpoints
{
    private const string SessionHeader = "X-Speaker-Session";

    public static RouteGroupBuilder MapPublicSpeakers(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/public/speaker-links").WithTags("Public speakers").AllowAnonymous().AllowWithoutTwoFactor();
        g.AddEndpointFilter(async (ctx, next) =>
        {
            ctx.HttpContext.Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
            return await next(ctx);
        });

        g.MapGet("/{token}", Landing).RequireRateLimiting(PublicRateLimiting.SpeakerRead);
        g.MapPost("/{token}/code", RequestCode).RequireRateLimiting(PublicRateLimiting.SpeakerCode);
        g.MapPost("/{token}/verify", Verify).RequireRateLimiting(PublicRateLimiting.SpeakerVerify).Validate<VerifyRequest>();
        g.MapGet("/{token}/form", FormState).RequireRateLimiting(PublicRateLimiting.SpeakerRead);
        g.MapPost("/{token}/photo", UploadPhoto).RequireRateLimiting(PublicRateLimiting.SpeakerRead);
        g.MapPost("/{token}/submit", Submit).RequireRateLimiting(PublicRateLimiting.SpeakerRead).Validate<SubmitRequest>();
        return g;
    }

    // ---------- Landing ----------

    private static async Task<IResult> Landing(string token, HtnDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        if (await FindAsync(db, token, ct) is not { } row) return TypedResults.NotFound();
        if (row.Link.IsRevoked || row.Link.IsExpired(now)) return LinkDead();

        var travel = await db.SpeakerTravel.AsNoTracking().FirstOrDefaultAsync(t => t.SpeakerId == row.Speaker.Id, ct);
        var state = travel?.HasSubmitted == true ? "Submitted" : "NeedsCode";
        return TypedResults.Ok(new SpeakerLandingDto(
            row.EventName, row.EventStartsAt, FirstName(row.Speaker.PersonName), EmailMask.Mask(row.Speaker.Email),
            row.TravelMode, row.Link.ExpiresAt, state, null));
    }

    // ---------- Code ----------

    private static async Task<IResult> RequestCode(
        string token, HtnDbContext db, EmailRateLimiter emailLimiter, IAuditLog audit, TimeProvider clock, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        if (await FindAsync(db, token, ct) is not { } row) return TypedResults.NotFound();
        // Dead-link first: an email-keyed limiter can never run before a token resolves to a speaker, so the only
        // way to keep an unknown and a known-but-dead token indistinguishable over repeated requests is to make sure
        // neither one ever reaches the limiter -- only a genuinely live link's code requests are rate-limited here.
        if (row.Link.IsRevoked || row.Link.IsExpired(now)) return LinkDead();
        if (!emailLimiter.TrySpeakerOtp(row.Speaker.Email))
            return Problems.Result(StatusCodes.Status429TooManyRequests, "Too many requests.", "Try again later.", SpeakerLinkErrorCodes.CodeResendLimit);

        var code = SpeakerLinksOps.NewOtpCode();
        var hash = SpeakerLinksOps.OtpHash(token, code);
        var tracked = await db.SpeakerLinks.FirstAsync(l => l.Id == row.Link.Id, ct);
        if (!tracked.TryIssueOtp(hash, now))
            return Problems.Result(StatusCodes.Status429TooManyRequests, "Too many requests.",
                "Too many codes requested. Try again in a few minutes.", SpeakerLinkErrorCodes.CodeResendLimit);
        db.SpeakerLinkAccess.Add(SpeakerLinkAccess.Create(row.Link.Id, row.Speaker.Id, row.Link.EventId, SpeakerLinkAccessKind.CodeSent, now));
        db.Outbox.Add(OutboxProcessor.SpeakerOtpEmail(row.Link.Id, code, now));
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    // ---------- Verify ----------

    private static async Task<IResult> Verify(
        string token, VerifyRequest req, HtnDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        if (await FindAsync(db, token, ct) is not { } row) return TypedResults.NotFound();
        if (row.Link.IsRevoked || row.Link.IsExpired(now)) return LinkDead();

        var result = await SpeakerLinksOps.TryVerifyOtpAsync(db, row.Link.Id, token, req.Code!, now, ct);
        switch (result.Outcome)
        {
            case OtpVerifyOutcome.Locked:
                return LockedResponse(row.Link.OtpLockedUntil);
            case OtpVerifyOutcome.JustLocked:
                db.SpeakerLinkAccess.Add(SpeakerLinkAccess.Create(row.Link.Id, row.Speaker.Id, row.Link.EventId, SpeakerLinkAccessKind.CodeLocked, now));
                await db.SaveChangesAsync(ct);
                return LockedResponse(now + SpeakerLink.LockDuration);
            case OtpVerifyOutcome.WrongCode:
                db.SpeakerLinkAccess.Add(SpeakerLinkAccess.Create(row.Link.Id, row.Speaker.Id, row.Link.EventId, SpeakerLinkAccessKind.CodeFailed, now));
                await db.SaveChangesAsync(ct);
                return Problems.Result(StatusCodes.Status400BadRequest, "That code is not right.", null, SpeakerLinkErrorCodes.CodeInvalid,
                    new Dictionary<string, object?> { ["attemptsLeft"] = result.AttemptsLeft });
            case OtpVerifyOutcome.Expired:
                return Problems.Result(StatusCodes.Status410Gone, "That code expired.", "Request a new code.", SpeakerLinkErrorCodes.CodeExpired);
        }

        var sessionRaw = Convert.ToHexStringLower(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        var link = await db.SpeakerLinks.FirstAsync(l => l.Id == row.Link.Id, ct);
        link.OpenSession(SpeakerLinksOps.Hash(sessionRaw), now);
        db.SpeakerLinkAccess.Add(SpeakerLinkAccess.Create(link.Id, row.Speaker.Id, link.EventId, SpeakerLinkAccessKind.Opened, now));
        await db.SaveChangesAsync(ct);

        var form = await FormStateAsync(db, row.Speaker, row.TravelMode, ct);
        return TypedResults.Ok(new VerifyResultDto(sessionRaw, link.SessionExpiresAt!.Value, form));
    }

    // ---------- Form / photo / submit (session-gated) ----------

    private static async Task<IResult> FormState(string token, HttpContext http, HtnDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (await SessionAsync(db, token, http, clock, ct) is not { } row) return row2Result(http);
        var form = await FormStateAsync(db, row.Speaker, row.TravelMode, ct);
        return TypedResults.Ok(form);
    }

    private static async Task<IResult> UploadPhoto(
        string token, HttpRequest request, HttpContext http, HtnDbContext db, IFileStorage storage, TimeProvider clock, CancellationToken ct)
    {
        if (await SessionAsync(db, token, http, clock, ct) is not { } row) return row2Result(http);
        var (file, error) = await Uploads.ReadSingleFileAsync(request, "file", StoredFilePurposes.SpeakerPhotoMaxBytes, "The photo must be 2 MB or smaller.", ct);
        if (error is not null) return error;
        if (FileSignatures.DetectImage(file!.Bytes, allowJpeg: true) is not { } image)
            return Problems.Field("file", "Upload a JPG or PNG image.", "unsupported_type");
        var stripped = FileSignatures.StripMetadata(file.Bytes);
        if (stripped is null) return Problems.Field("file", "Upload a JPG or PNG image.", "unsupported_type");

        var now = clock.GetUtcNow();
        var stored = StoredFile.Create(image.ContentType, stripped.LongLength, Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(stripped)),
            StoredFilePurposes.SpeakerPhoto, file.FileName, now, null);
        await storage.SaveAsync(stored.Id, stripped, ct);
        db.StoredFiles.Add(stored);
        var speaker = await db.Speakers.FirstAsync(sp => sp.Id == row.Speaker.Id, ct);
        speaker.SetPhoto(stored.Id, speaker.CreatedById ?? Guid.Empty, now);
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(new PhotoUploadResultDto(stored.Id));
    }

    private static async Task<IResult> Submit(
        string token, SubmitRequest req, HttpContext http, HtnDbContext db, IPublicIdGenerator ids, IAuditLog audit,
        TimeProvider clock, CancellationToken ct)
    {
        if (await SessionAsync(db, token, http, clock, ct) is not { } row) return row2Result(http);
        var now = clock.GetUtcNow();
        if (row.EventEndsAt.AddDays(30) < now)
            return Problems.Result(StatusCodes.Status410Gone, "This event has closed.", "The confirmation window has closed.", SpeakerLinkErrorCodes.EventClosed);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var travel = await db.SpeakerTravel.FirstOrDefaultAsync(t => t.SpeakerId == row.Speaker.Id, ct);
        if (travel is null) { travel = SpeakerTravel.Create(row.Speaker.Id, row.Link.EventId); db.SpeakerTravel.Add(travel); }
        try
        {
            travel.Submit(new TravelSubmission(
                req.ArrivalDate, req.DepartureDate, req.FlyingFrom, req.PreferredArrivalTime, req.IdFullName,
                req.BusFrom, req.BusEta, req.NeedsTerminalPickup ?? false, null, req.NeedsNotes,
                Domain.Speakers.SpeakerTravel.CurrentConsentVersion), now);
        }
        catch (DomainException ex) { return Problems.Field("departureDate", ex.Message, "invalid_dates"); }

        string? voucherCode = null;
        var existingPlusOne = await db.SpeakerPlusOnes.FirstOrDefaultAsync(p => p.SpeakerId == row.Speaker.Id && p.RemovedAt == null, ct);
        if (req.HasPlusOne == true)
        {
            var nameChanged = existingPlusOne is not null && !string.Equals(existingPlusOne.Name, req.PlusOneName, StringComparison.Ordinal);
            if (existingPlusOne is null || nameChanged)
            {
                existingPlusOne?.Remove("Plus-one details changed", row.Speaker.CreatedById ?? Guid.Empty, now);
                if (existingPlusOne?.VoucherId is { } oldVoucherId)
                {
                    var oldVoucher = await db.Vouchers.FirstOrDefaultAsync(v => v.Id == oldVoucherId, ct);
                    oldVoucher?.Deactivate(now);
                }
                var speaker = await db.Speakers.FirstAsync(sp => sp.Id == row.Speaker.Id, ct);
                Voucher voucher;
                try
                {
                    (_, voucher) = await PlusOneOps.IssueAsync(db, speaker, req.PlusOneName!, req.PlusOneEmail!, req.PlusOneSharesRoom ?? false, ids, speaker.CreatedById ?? Guid.Empty, now, ct);
                }
                catch (DbUpdateException ex) when (PlusOneOps.IsActivePlusOneConflict(ex))
                {
                    // A concurrent submit from the same speaker (e.g. a double-tap) already created the active row;
                    // the whole transaction is rolled back by the exception handler, so nothing here is half-done.
                    return Problems.Result(StatusCodes.Status409Conflict, "That cannot be done right now.",
                        "This was already submitted a moment ago. Reload and try again.", "concurrent_submit");
                }
                voucherCode = voucher.Code;
            }
            else
            {
                voucherCode = existingPlusOne.VoucherId is { } vid
                    ? await db.Vouchers.AsNoTracking().Where(v => v.Id == vid).Select(v => v.Code).FirstOrDefaultAsync(ct)
                    : null;
            }
        }
        else if (existingPlusOne is not null)
        {
            existingPlusOne.Remove("Plus-one removed on resubmission", row.Speaker.CreatedById ?? Guid.Empty, now);
            if (existingPlusOne.VoucherId is { } vid2)
            {
                var v = await db.Vouchers.FirstOrDefaultAsync(x => x.Id == vid2, ct);
                v?.Deactivate(now);
            }
        }

        var link = await db.SpeakerLinks.FirstAsync(l => l.Id == row.Link.Id, ct);
        link.MarkSubmitted(now);
        db.SpeakerLinkAccess.Add(SpeakerLinkAccess.Create(link.Id, row.Speaker.Id, link.EventId, SpeakerLinkAccessKind.Submitted, now));
        audit.Record(null, "speaker.form_submitted", nameof(Domain.Speakers.Speaker), row.Speaker.Id.ToString(),
            after: new { hasPlusOne = req.HasPlusOne == true });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return TypedResults.Ok(new SubmitResultDto(now, voucherCode));
    }

    // ---------- Helpers ----------

    private sealed record LinkRow(SpeakerLink Link, Domain.Speakers.Speaker Speaker, string EventName, DateTimeOffset EventStartsAt, DateTimeOffset EventEndsAt, TravelMode TravelMode);

    private static async Task<LinkRow?> FindAsync(HtnDbContext db, string token, CancellationToken ct)
    {
        var hash = SpeakerLinksOps.Hash(token.Trim());
        var link = await db.SpeakerLinks.AsNoTracking().FirstOrDefaultAsync(l => l.TokenHash == hash, ct);
        if (link is null) return null;
        var speaker = await db.Speakers.AsNoTracking().FirstOrDefaultAsync(sp => sp.Id == link.SpeakerId, ct);
        if (speaker is null || speaker.VoidedAt is not null) return null;
        var ev = await db.Events.AsNoTracking().Where(e => e.Id == link.EventId)
            .Select(e => new { e.Name, e.StartsAt, e.EndsAt, e.TravelMode }).FirstOrDefaultAsync(ct);
        return ev is null ? null : new LinkRow(link, speaker, ev.Name, ev.StartsAt, ev.EndsAt, ev.TravelMode);
    }

    /// <summary>Validates the X-Speaker-Session header against the link's current session. Null means "already answered".</summary>
    private static async Task<LinkRow?> SessionAsync(HtnDbContext db, string token, HttpContext http, TimeProvider clock, CancellationToken ct)
    {
        if (await FindAsync(db, token, ct) is not { } row) { http.Items["speaker_result"] = TypedResults.NotFound(); return null; }
        var now = clock.GetUtcNow();
        if (row.Link.IsRevoked || row.Link.IsExpired(now)) { http.Items["speaker_result"] = LinkDead(); return null; }
        var sessionToken = http.Request.Headers[SessionHeader].FirstOrDefault();
        if (string.IsNullOrEmpty(sessionToken) || !row.Link.SessionIsValid(SpeakerLinksOps.Hash(sessionToken), now))
        {
            http.Items["speaker_result"] = Problems.Result(StatusCodes.Status401Unauthorized, "Verify your code again.",
                "Your session has expired. Request a new code.", SpeakerLinkErrorCodes.SessionRequired);
            return null;
        }
        return row;
    }

    private static IResult row2Result(HttpContext http) => (IResult)http.Items["speaker_result"]!;

    private static async Task<SpeakerFormStateDto> FormStateAsync(HtnDbContext db, Domain.Speakers.Speaker speaker, TravelMode travelMode, CancellationToken ct)
    {
        var travel = await db.SpeakerTravel.AsNoTracking().FirstOrDefaultAsync(t => t.SpeakerId == speaker.Id, ct);
        var sessionText = await (
            from ss in db.SessionSpeakers.AsNoTracking()
            join s in db.Sessions.AsNoTracking() on ss.SessionId equals s.Id
            where ss.SpeakerId == speaker.Id
            orderby s.StartsAt
            select s.Kind + " · " + Htn.Api.Features.Agenda.AgendaViews.Time(s.StartsAt)).FirstOrDefaultAsync(ct);
        var plus = await db.SpeakerPlusOnes.AsNoTracking().FirstOrDefaultAsync(p => p.SpeakerId == speaker.Id && p.RemovedAt == null, ct);

        return new SpeakerFormStateDto(
            speaker.PersonName, sessionText ?? "No session yet", travelMode, speaker.PhotoFileId?.ToString(), speaker.PhotoApproved,
            travel?.ArrivalDate, travel?.DepartureDate, travel?.FlyingFrom, travel?.PreferredArrivalTime,
            travel?.BusFrom, travel?.BusEta, travel?.NeedsTerminalPickup ?? false, travel?.NeedsNotes,
            plus is not null, plus?.Name, plus?.Email, plus?.SharesRoom ?? false, travel?.HasSubmitted ?? false);
    }

    private static string FirstName(string fullName)
    {
        var trimmed = fullName.Trim();
        var space = trimmed.IndexOf(' ');
        return space > 0 ? trimmed[..space] : trimmed;
    }

    /// <summary>Expired and revoked share one body, so a link holder can't tell which happened.</summary>
    private static IResult LinkDead() =>
        Problems.Result(StatusCodes.Status410Gone, "This link no longer works.", "Ask the Programs team for a new one.", SpeakerLinkErrorCodes.LinkExpired);

    private static IResult LockedResponse(DateTimeOffset? lockedUntil) =>
        Problems.Result(StatusCodes.Status429TooManyRequests, "Too many attempts.",
            "Too many attempts. Try again later.", SpeakerLinkErrorCodes.CodeLocked,
            new Dictionary<string, object?> { ["lockedUntil"] = lockedUntil });
}
