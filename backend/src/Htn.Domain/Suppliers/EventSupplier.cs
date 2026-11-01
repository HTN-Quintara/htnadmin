using Htn.Domain.Common;

namespace Htn.Domain.Suppliers;

/// <summary>The fields given when hiring a supplier for an event.</summary>
public sealed record NewEventSupplierHire(Guid SupplierId, Guid CategoryId, Guid OwnerCommitteeId, string? Note);

/// <summary>The editable money/status-adjacent fields of an existing hire (status itself moves through <see cref="EventSupplier.Advance"/>).</summary>
public sealed record UpdateEventSupplierHire(
    decimal? ContractAmount, decimal? PaidAmount, decimal? NextPaymentAmount, DateOnly? NextPaymentDue, string? Note);

/// <summary>
/// One supplier hired for one event (Phase 7C) — a separate entity from <see cref="Supplier"/> itself, keyed to the
/// event and the stable <see cref="Htn.Domain.Finance.LedgerCategory"/> (never an <c>EventBudgetLine.Id</c>, which is
/// replaced wholesale on every budget save: see <c>EventBudget.SaveVersion</c>). <see cref="OwnerCommitteeId"/> is the
/// committee whose lead may rate this hire after the event (<c>Suppliers.Rate</c>).
/// <para>
/// <b>No auto-post.</b> <see cref="ContractAmount"/>/<see cref="PaidAmount"/>/<see cref="NextPaymentAmount"/> are
/// tracking figures only; cash actually leaving the organization stays the CFO's manual ledger/expense-request flow
/// (Phase 7C decision, fixed in the brief).
/// </para>
/// <para>
/// <b>Status is forward-only</b> along the exact sequence Quotation → Booked → DepositPaid → FullyPaid (judgment
/// call, since the brief offered two options and asked for the simpler one to be picked): no skipping ahead, and no
/// backward move at all, not even the one exception <c>EventInvitation</c> allows for Bounced. If a booking falls
/// through, the hire is voided with a reason and a new hire is created — <see cref="Void"/> — rather than
/// un-wedging the status, which keeps this state machine a straight line and matches "no hard deletes": the failed
/// booking stays visible as a voided row instead of silently rewinding.
/// </para>
/// Deleting the row entirely is allowed only while still <see cref="SupplierHireStatus.Quotation"/> (judgment call,
/// by analogy with <c>EventInvitation</c>'s "nothing irreversible happened yet" reasoning for <c>NotSent</c>): once
/// <c>Booked</c> or later, money may already be committed, so the row is permanent and must be voided instead,
/// enforced again at the database (<c>Suppliers_Guards</c>).
/// </summary>
public sealed class EventSupplier
{
    public const int NoteMaxLength = 500;
    public const int ReasonMaxLength = 500;

    /// <summary>The one allowed next step from each live status. See the class remarks.</summary>
    private static readonly Dictionary<SupplierHireStatus, SupplierHireStatus> NextStatus = new()
    {
        [SupplierHireStatus.Quotation] = SupplierHireStatus.Booked,
        [SupplierHireStatus.Booked] = SupplierHireStatus.DepositPaid,
        [SupplierHireStatus.DepositPaid] = SupplierHireStatus.FullyPaid,
    };

    private EventSupplier() { }

    public Guid Id { get; private set; }
    public Guid EventId { get; private set; }
    public Guid SupplierId { get; private set; }
    public Guid CategoryId { get; private set; }
    public Guid OwnerCommitteeId { get; private set; }
    public SupplierHireStatus Status { get; private set; }
    public decimal? ContractAmount { get; private set; }
    public decimal? PaidAmount { get; private set; }
    public decimal? NextPaymentAmount { get; private set; }
    public DateOnly? NextPaymentDue { get; private set; }
    public string? Note { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public Guid? CreatedById { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public Guid? UpdatedById { get; private set; }
    public DateTimeOffset? VoidedAt { get; private set; }
    public Guid? VoidedById { get; private set; }
    public string? VoidReason { get; private set; }

    public bool IsVoided => VoidedAt is not null;
    public bool IsQuotation => Status == SupplierHireStatus.Quotation;

    public static EventSupplier Create(Guid eventId, NewEventSupplierHire data, Guid actorId, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (eventId == Guid.Empty) throw new DomainException("An event is required.");
        if (data.SupplierId == Guid.Empty) throw new DomainException("A supplier is required.");
        if (data.CategoryId == Guid.Empty) throw new DomainException("A budget category is required.");
        if (data.OwnerCommitteeId == Guid.Empty) throw new DomainException("An owning committee is required.");
        return new EventSupplier
        {
            Id = Guid.CreateVersion7(at),
            EventId = eventId,
            SupplierId = data.SupplierId,
            CategoryId = data.CategoryId,
            OwnerCommitteeId = data.OwnerCommitteeId,
            Status = SupplierHireStatus.Quotation,
            Note = SupplierRules.Optional(data.Note, "Note", NoteMaxLength),
            CreatedAt = at,
            CreatedById = actorId,
            UpdatedAt = at,
            UpdatedById = actorId,
        };
    }

    /// <summary>Updates the tracking figures. Status itself never moves here — see <see cref="Advance"/>.</summary>
    public void Update(UpdateEventSupplierHire data, Guid actorId, DateTimeOffset at)
    {
        EnsureLive();
        ArgumentNullException.ThrowIfNull(data);
        ContractAmount = SupplierRules.NonNegativeMoney(data.ContractAmount, "Contract amount");
        PaidAmount = SupplierRules.NonNegativeMoney(data.PaidAmount, "Paid amount");
        NextPaymentAmount = SupplierRules.NonNegativeMoney(data.NextPaymentAmount, "Next payment amount");
        NextPaymentDue = data.NextPaymentDue;
        Note = SupplierRules.Optional(data.Note, "Note", NoteMaxLength);
        Touch(actorId, at);
    }

    /// <summary>Advances exactly one step along the forward-only sequence. Throws on any other move (see the class remarks).</summary>
    public void Advance(SupplierHireStatus newStatus, Guid actorId, DateTimeOffset at)
    {
        EnsureLive();
        if (!NextStatus.TryGetValue(Status, out var expected) || expected != newStatus)
            throw new DomainException($"Cannot move a hire from {Status} to {newStatus}.");
        Status = newStatus;
        Touch(actorId, at);
    }

    public void Void(string reason, Guid actorId, DateTimeOffset at)
    {
        EnsureLive();
        VoidReason = SupplierRules.Required(reason, "Reason", ReasonMaxLength);
        VoidedAt = at;
        VoidedById = actorId;
    }

    private void Touch(Guid actorId, DateTimeOffset at)
    {
        UpdatedAt = at;
        UpdatedById = actorId;
    }

    private void EnsureLive()
    {
        if (IsVoided) throw new DomainException("This hire has been voided.");
    }
}
