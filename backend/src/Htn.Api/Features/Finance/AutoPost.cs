using Htn.Domain.Common;
using Htn.Domain.Finance;
using Htn.Domain.Merch;
using Htn.Domain.Organization;
using Htn.Domain.Registrations;
using Htn.Domain.Sponsors;
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
    /// Posts a sponsor's cash contribution to its event fund under <c>Sponsorship</c>. The caller has already
    /// recorded the receipt on the sponsor; this writes the matching ledger row in the same transaction. One live
    /// entry per sponsor forever (the <c>ux_ledger_entries_source</c> index), so a double submit is a no-op.
    /// </summary>
    public async Task<LedgerEntry?> SponsorCashAsync(
        Sponsor sponsor, Guid paymentAccountId, DateOnly receivedOn, string? reference, Guid actorId,
        DateTimeOffset at, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(sponsor);
        if (sponsor.CashAmount is not { } amount || amount <= 0m) return null;
        if (await ExistsAsync(LedgerSource.SponsorPayment, sponsor.Id, ct)) return null;

        var fund = await FinanceOps.EventFundAsync(db, sponsor.EventId, at, ct);
        var entry = LedgerEntry.Posted(
            new NewLedgerEntry(
                fund.Id,
                paymentAccountId,
                LedgerCategories.SponsorshipId,
                await PostableDateAsync(receivedOn, ct),
                LedgerDirection.In,
                amount,
                reference,
                $"Sponsorship · {sponsor.Name}",
                null,
                LedgerSource.SponsorPayment,
                sponsor.Id),
            actorId,
            at);
        db.LedgerEntries.Add(entry);
        audit.Record(actorId, FinanceAuditActions.LedgerAutoPost, nameof(LedgerEntry), entry.Id.ToString(),
            after: new { entry.FundId, entry.Amount, entry.EntryDate, source = entry.Source.ToString(), sponsorId = sponsor.Id });
        return entry;
    }

    /// <summary>
    /// Takes a sponsor's posted cash back out: voided in an open month, offset by a contra entry in a closed one.
    /// Returns the entry and whether it was voided (a void frees the sponsor to record its cash again; a contra does
    /// not, because the original stays posted and still holds the idempotency slot).
    /// </summary>
    public async Task<(LedgerEntry Entry, bool Voided)?> ReverseSponsorCashAsync(
        Sponsor sponsor, string reason, Guid actorId, DateTimeOffset at, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(sponsor);
        var entry = await db.LedgerEntries
            .FirstOrDefaultAsync(e => e.Source == LedgerSource.SponsorPayment && e.SourceId == sponsor.Id
                                      && e.Status != LedgerStatus.Voided, ct);
        if (entry is null) return null;

        if (entry.MonthCloseId is null)
        {
            var before = new { status = entry.Status.ToString() };
            entry.Void(reason, actorId, at);
            audit.Record(actorId, FinanceAuditActions.LedgerVoid, nameof(LedgerEntry), entry.Id.ToString(),
                before, new { status = entry.Status.ToString() }, reason);
            return (entry, true);
        }

        var contra = LedgerEntry.Contra(
            entry, await PostableDateAsync(ManilaTime.ManilaDate(at), ct), reason, actorId, at);
        db.LedgerEntries.Add(contra);
        audit.Record(actorId, FinanceAuditActions.LedgerContra, nameof(LedgerEntry), contra.Id.ToString(),
            after: new { contra.Amount, contra.EntryDate, offsets = entry.Id }, reason: reason);
        return (contra, false);
    }

    /// <summary>
    /// Posts a merch sale's total to its event fund under <c>Merch sales</c> (<see cref="LedgerCategories.MerchId"/>).
    /// Called in the same transaction as the sale itself (<c>MerchEndpoints.Sell</c>), exactly like
    /// <see cref="TicketPaymentAsync"/>: one live entry per sale forever (<c>ux_ledger_entries_source</c>), so a
    /// retried request is a no-op. Returns null for a zero-total sale (100% discount) — nothing moved.
    /// </summary>
    public async Task<LedgerEntry?> MerchSaleAsync(MerchSale sale, Guid actorId, DateTimeOffset at, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(sale);
        if (sale.Total <= 0m) return null;
        if (await ExistsAsync(LedgerSource.MerchSale, sale.Id, ct)) return null;

        // Phase 8C: a pop-up sale always posts to the Organization fund, never to any event's — even though its
        // stock may have come from a specific event's leftovers — per htn-domain's "Sales post to the Organization
        // fund." sale.EventId is null for exactly this case (ck_merch_sales_one_scope), so this is the one branch
        // this shared method needs; every existing event-POS call site is unaffected (sale.PopUpId stays null there).
        var fund = sale.PopUpId is not null
            ? await FinanceOps.OrganizationFundAsync(db, ct)
            : await FinanceOps.EventFundAsync(db, sale.EventId!.Value, at, ct);
        var entry = LedgerEntry.Posted(
            new NewLedgerEntry(
                fund.Id,
                null,
                LedgerCategories.MerchId,
                await PostableDateAsync(ManilaTime.ManilaDate(at), ct),
                LedgerDirection.In,
                sale.Total,
                null,
                $"Merch sale · {sale.BuyerNameSnapshot}",
                null,
                LedgerSource.MerchSale,
                sale.Id),
            actorId,
            at);
        db.LedgerEntries.Add(entry);
        audit.Record(actorId, FinanceAuditActions.LedgerAutoPost, nameof(LedgerEntry), entry.Id.ToString(),
            after: new { entry.FundId, entry.Amount, entry.EntryDate, source = entry.Source.ToString(), saleId = sale.Id });
        return entry;
    }

    /// <summary>
    /// Posts a membership fee to the event it was paid at, if any, else the Organization fund (the People page's
    /// "Paid now" path, which isn't tied to a POS sale) — under <c>Membership dues</c>
    /// (<see cref="LedgerCategories.MembershipDuesId"/>). Called in the same transaction as the write that created
    /// the <see cref="Membership"/> row (the POS membership sale or the manual record), exactly like
    /// <see cref="MerchSaleAsync"/>: one live entry per membership forever (<c>ux_ledger_entries_source</c>).
    /// Returns null for a free membership (a zero fee) — nothing moved.
    /// </summary>
    public async Task<LedgerEntry?> MembershipPaymentAsync(
        Membership membership, string personName, Guid actorId, DateTimeOffset at, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(membership);
        if (membership.FeePaid <= 0m) return null;
        if (await ExistsAsync(LedgerSource.MembershipPayment, membership.Id, ct)) return null;

        var fund = membership.EventId is { } eventId
            ? await FinanceOps.EventFundAsync(db, eventId, at, ct)
            : await FinanceOps.OrganizationFundAsync(db, ct);
        var entry = LedgerEntry.Posted(
            new NewLedgerEntry(
                fund.Id,
                null,
                LedgerCategories.MembershipDuesId,
                await PostableDateAsync(ManilaTime.ManilaDate(at), ct),
                LedgerDirection.In,
                membership.FeePaid,
                null,
                $"Membership dues {membership.MembershipYear} · {personName}",
                null,
                LedgerSource.MembershipPayment,
                membership.Id),
            actorId,
            at);
        db.LedgerEntries.Add(entry);
        audit.Record(actorId, FinanceAuditActions.LedgerAutoPost, nameof(LedgerEntry), entry.Id.ToString(),
            after: new { entry.FundId, entry.Amount, entry.EntryDate, source = entry.Source.ToString(), membershipId = membership.Id });
        return entry;
    }

    /// <summary>
    /// Posts a CFO write-off for a pop-up's missing stock to the Organization fund under <c>Write-off</c>
    /// (<see cref="LedgerCategories.WriteOffId"/>). The pop-up stays <c>ClosedMissing</c> afterwards — this only
    /// posts the expense and records who/when (<c>PopUpStore.MarkWrittenOff</c>, called by the caller in the same
    /// transaction). One live entry per pop-up forever (<c>ux_ledger_entries_source</c>).
    /// </summary>
    public async Task<LedgerEntry?> PopUpWriteOffAsync(
        Htn.Domain.PopUps.PopUpStore popUp, decimal amount, string reason, Guid actorId, DateTimeOffset at, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(popUp);
        if (amount <= 0m) return null;
        if (await ExistsAsync(LedgerSource.PopUpWriteOff, popUp.Id, ct)) return null;

        var fund = await FinanceOps.OrganizationFundAsync(db, ct);
        var entry = LedgerEntry.Posted(
            new NewLedgerEntry(
                fund.Id,
                null,
                LedgerCategories.WriteOffId,
                await PostableDateAsync(ManilaTime.ManilaDate(at), ct),
                LedgerDirection.Out,
                amount,
                null,
                $"Pop-up write-off · {popUp.Name} · {reason}",
                null,
                LedgerSource.PopUpWriteOff,
                popUp.Id),
            actorId,
            at);
        db.LedgerEntries.Add(entry);
        audit.Record(actorId, FinanceAuditActions.LedgerAutoPost, nameof(LedgerEntry), entry.Id.ToString(),
            after: new { entry.FundId, entry.Amount, entry.EntryDate, source = entry.Source.ToString(), popUpId = popUp.Id }, reason: reason);
        return entry;
    }

    /// <summary>Whether a live automatic entry already exists for this source row (e.g. a contra left the slot taken).</summary>
    public Task<bool> HasLiveEntryAsync(LedgerSource source, Guid sourceId, CancellationToken ct) =>
        ExistsAsync(source, sourceId, ct);

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
