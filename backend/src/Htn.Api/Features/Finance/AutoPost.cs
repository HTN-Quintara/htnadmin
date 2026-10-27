using Htn.Domain.Common;
using Htn.Domain.Finance;
using Htn.Domain.Registrations;
using Htn.Infrastructure.Audit;
using Htn.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Htn.Api.Features.Finance;

/// <summary>
/// Posts the money other features take into the ledger. "Ticket, merch, sponsor and membership payments auto-post
/// when marked paid", so this runs <b>inline, in the caller's transaction</b> — not through the outbox. An outbox hop
/// would make posting eventually consistent, and then a month could be closed while a ledger row was still in flight,
/// which breaks both "balances are derived from posted entries" and the "nothing pending" close precondition. The
/// outbox is for side effects that cannot join the database transaction, such as email; a ledger row can.
/// <para>
/// Idempotency is the <c>ux_ledger_entries_source</c> partial unique index: one live entry per source row, forever,
/// even under concurrency. The cheap <c>AnyAsync</c> below is only there to skip the work quietly.
/// </para>
/// Phases 6-8 add producers for the sponsor, supplier, merch, membership and write-off sources; the enum and the
/// check constraint already admit them, so no migration will be needed.
/// </summary>
public sealed class AutoPost(HtnDbContext db, IAuditLog audit)
{
    /// <summary>
    /// Posts a registration payment to its event fund. Returns null when there is nothing to post: a comp or a
    /// zero-total checkout moves no money, and a payment already in the ledger is left alone.
    /// </summary>
    public async Task<LedgerEntry?> TicketPaymentAsync(
        Registration registration, Payment payment, DateTimeOffset at, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(registration);
        ArgumentNullException.ThrowIfNull(payment);
        if (payment.Amount <= 0m || payment.Method == PaymentMethod.Comp) return null;
        if (await ExistsAsync(LedgerSource.TicketPayment, payment.Id, ct)) return null;

        var fund = await FinanceOps.EventFundAsync(db, registration.EventId, at, ct);
        // The registrant's own proof of payment is the receipt when they uploaded one.
        var receiptFileId = await db.PaymentProofs.AsNoTracking()
            .Where(p => p.RegistrationId == registration.Id)
            .OrderByDescending(p => p.UploadedAt)
            .Select(p => (Guid?)p.FileId)
            .FirstOrDefaultAsync(ct);
        var participantId = await db.Attendees.AsNoTracking()
            .Where(a => a.RegistrationId == registration.Id && a.SortOrder == 0)
            .Select(a => a.ParticipantId)
            .FirstOrDefaultAsync(ct);

        var entry = LedgerEntry.Posted(
            new NewLedgerEntry(
                fund.Id,
                payment.PaymentAccountId,
                LedgerCategories.TicketsId,
                await PostableDateAsync(ManilaTime.ManilaDate(payment.ReceivedAt), ct),
                LedgerDirection.In,
                payment.Amount,
                payment.Reference,
                participantId is null ? "Ticket payment" : $"Ticket payment · {participantId}",
                receiptFileId,
                LedgerSource.TicketPayment,
                payment.Id),
            payment.MarkedById,
            at);
        db.LedgerEntries.Add(entry);
        audit.Record(payment.MarkedById, FinanceAuditActions.LedgerAutoPost, nameof(LedgerEntry), entry.Id.ToString(),
            after: new
            {
                entry.FundId, entry.Amount, entry.EntryDate, source = entry.Source.ToString(),
                sourceId = payment.Id, registrationId = registration.Id,
            });
        return entry;
    }

    /// <summary>
    /// Takes a reverted payment back out of the ledger. In an open month the entry is voided; in a closed one it can
    /// no longer be touched, so an opposite entry dated today offsets it instead. Never a delete, either way.
    /// </summary>
    public async Task<LedgerEntry?> ReversePaymentAsync(
        Payment payment, string reason, Guid? actorId, DateTimeOffset at, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(payment);
        var entry = await db.LedgerEntries
            .FirstOrDefaultAsync(e => e.Source == LedgerSource.TicketPayment && e.SourceId == payment.Id
                                      && e.Status != LedgerStatus.Voided, ct);
        if (entry is null) return null;

        if (entry.MonthCloseId is null)
        {
            var before = new { status = entry.Status.ToString() };
            entry.Void(reason, actorId ?? Guid.Empty, at);
            audit.Record(actorId, FinanceAuditActions.LedgerVoid, nameof(LedgerEntry), entry.Id.ToString(),
                before, new { status = entry.Status.ToString() }, reason);
            return entry;
        }

        var contra = LedgerEntry.Contra(
            entry, await PostableDateAsync(ManilaTime.ManilaDate(at), ct), reason, actorId, at);
        db.LedgerEntries.Add(contra);
        audit.Record(actorId, FinanceAuditActions.LedgerContra, nameof(LedgerEntry), contra.Id.ToString(),
            after: new { contra.Amount, contra.EntryDate, offsets = entry.Id }, reason: reason);
        return contra;
    }

    /// <summary>Posts the payment of an approved expense request to the Organization fund.</summary>
    public async Task<LedgerEntry> ExpenseRequestPaidAsync(
        ExpenseRequest request, Guid paymentAccountId, Guid receiptFileId, DateOnly entryDate,
        Guid actorId, DateTimeOffset at, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var fund = await FinanceOps.OrganizationFundAsync(db, ct);
        var entry = LedgerEntry.Posted(
            new NewLedgerEntry(fund.Id, paymentAccountId, request.CategoryId, entryDate, LedgerDirection.Out,
                request.Amount, null, request.Title, receiptFileId, LedgerSource.ExpenseRequest, request.Id),
            actorId, at);
        db.LedgerEntries.Add(entry);
        audit.Record(actorId, FinanceAuditActions.LedgerCreate, nameof(LedgerEntry), entry.Id.ToString(),
            after: new { entry.FundId, entry.Amount, entry.EntryDate, source = entry.Source.ToString(), requestId = request.Id });
        return entry;
    }

    /// <summary>
    /// The date an automatic entry can actually be posted on: the business date it belongs to, or — when the books
    /// for that month are already closed — the first day of the earliest month still open.
    /// <para>
    /// The desk must always be able to take money, and a reverted payment must always be able to come back out, so a
    /// closed month never blocks either; it only moves where the entry lands. The CFO's own hand-entered rows are
    /// different: those are refused with <c>month_closed</c>, because the CFO chose the date.
    /// </para>
    /// </summary>
    private async Task<DateOnly> PostableDateAsync(DateOnly preferred, CancellationToken ct)
    {
        await FinanceOps.LockPeriodsAsync(db, ct);

        // Never before the latest certified period. A month with no entries never gets a close row, so asking only
        // "is this month closed?" would happily post a late August payment into August after September was closed —
        // silently changing a balance someone has already signed off, and leaving the months closed out of order.
        var date = preferred;
        if (await FinanceOps.LatestCloseAsync(db, ct) is { } latest)
        {
            var afterLatest = new DateOnly(latest.Year, latest.Month, 1).AddMonths(1);
            if (date < afterLatest) date = afterLatest;
        }

        // Then step over any later month that is itself closed. Two years of them would mean something is very wrong.
        for (var i = 0; i < 24; i++)
        {
            var closed = await db.MonthCloses.AsNoTracking()
                .AnyAsync(m => m.ReopenedAt == null && m.Year == date.Year && m.Month == date.Month, ct);
            if (!closed) return date;
            date = new DateOnly(date.Year, date.Month, 1).AddMonths(1);
        }
        return date;
    }

    private Task<bool> ExistsAsync(LedgerSource source, Guid sourceId, CancellationToken ct) =>
        db.LedgerEntries.AsNoTracking()
            .AnyAsync(e => e.Source == source && e.SourceId == sourceId && e.Status != LedgerStatus.Voided, ct);
}
