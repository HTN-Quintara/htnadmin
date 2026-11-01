using Htn.Domain.Common;
using Htn.Domain.Events;
using Htn.Domain.Speakers;
using Htn.Infrastructure.Data;
using Htn.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Htn.Api.Features.Speakers;

/// <summary>
/// Issues a speaker's plus-one and its single-use 50%-off voucher inside the caller's transaction. The code is
/// <c>SPK-&lt;segment&gt;-&lt;4 random&gt;</c>; a collision on <c>ux_vouchers_event_code</c> retries with a fresh
/// tail (then a wider one), the same savepoint-and-retry discipline <c>AccountTokenService.IssueAsync</c> uses.
/// </summary>
public static class PlusOneOps
{
    public const string VoucherExhausted = "voucher_code_exhausted";
    private const string Savepoint = "plus_one_voucher";
    private const int MaxShortAttempts = 5;
    private const int MaxWideAttempts = 2;

    public static async Task<(SpeakerPlusOne PlusOne, Voucher Voucher)> IssueAsync(
        HtnDbContext db, Domain.Speakers.Speaker speaker, string name, string email, bool sharesRoom,
        IPublicIdGenerator ids, Guid actorId, DateTimeOffset at, CancellationToken ct)
    {
        var segment = PlusOneCode.Segment(name);
        var plusOne = SpeakerPlusOne.Create(speaker.Id, speaker.EventId, name, segment, email, sharesRoom, at);
        db.SpeakerPlusOnes.Add(plusOne);

        IDbContextTransaction? own = null;
        var tx = db.Database.CurrentTransaction ?? (own = await db.Database.BeginTransactionAsync(ct));
        try
        {
            Voucher? voucher = null;
            for (var attempt = 1; voucher is null; attempt++)
            {
                var tailLength = attempt <= MaxShortAttempts ? 4 : 6;
                if (attempt > MaxShortAttempts + MaxWideAttempts)
                    throw new DomainException(VoucherExhausted);
                var code = PlusOneCode.Code(segment, ids.NewCode(tailLength));
                var candidate = Voucher.Create(speaker.EventId, code, VoucherKind.Percent, 50m, [], 1,
                    speaker.EventId == Guid.Empty ? null : await EventEndsAtAsync(db, speaker.EventId, ct),
                    VoucherSource.SpeakerPlusOne, at);
                await tx.CreateSavepointAsync(Savepoint, ct);
                try
                {
                    db.Vouchers.Add(candidate);
                    await db.SaveChangesAsync(ct);
                    voucher = candidate;
                }
                catch (DbUpdateException ex) when (IsCodeConflict(ex))
                {
                    db.Entry(candidate).State = EntityState.Detached;
                    await tx.RollbackToSavepointAsync(Savepoint, ct);
                }
            }
            plusOne.AttachVoucher(voucher.Id);
            await db.SaveChangesAsync(ct);
            db.Outbox.Add(OutboxProcessor.PlusOneVoucherEmail(plusOne.Id, at));
            if (own is not null) await own.CommitAsync(ct);
            return (plusOne, voucher);
        }
        finally
        {
            if (own is not null) await own.DisposeAsync();
        }
    }

    private static async Task<DateTimeOffset?> EventEndsAtAsync(HtnDbContext db, Guid eventId, CancellationToken ct) =>
        await db.Events.AsNoTracking().Where(e => e.Id == eventId).Select(e => (DateTimeOffset?)e.EndsAt).FirstOrDefaultAsync(ct);

    private static bool IsCodeConflict(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "ux_vouchers_event_code" };

    /// <summary>
    /// A concurrent double-submit can race two requests past the "no active plus-one yet" check before either
    /// commits; the second hits <c>ux_speaker_plus_ones_active</c>. The whole submit transaction still rolls back
    /// cleanly either way (no orphan voucher, no duplicate row) -- this only turns the unhandled exception into an
    /// honest 409 instead of a 500.
    /// </summary>
    public static bool IsActivePlusOneConflict(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "ux_speaker_plus_ones_active" };
}
