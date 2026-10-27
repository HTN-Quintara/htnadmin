using Htn.Domain.Common;

namespace Htn.Domain.Finance;

/// <summary>
/// The fields every new entry shares, whatever wrote it. <paramref name="SourceId"/> is the idempotency key for an
/// automatic post (the payment id, the expense request id): one live entry per source, forever.
/// </summary>
public sealed record NewLedgerEntry(
    Guid FundId,
    Guid? PaymentAccountId,
    Guid CategoryId,
    DateOnly EntryDate,
    LedgerDirection Direction,
    decimal Amount,
    string? Reference,
    string Description,
    Guid? ReceiptFileId,
    LedgerSource Source,
    Guid? SourceId);

/// <summary>
/// One movement of money in or out of a <see cref="Fund"/>. The ledger is <b>single-entry</b>: the sign comes from
/// <see cref="Direction"/>, and the only movement written as two rows is a transfer between funds
/// (<see cref="Transfer"/>), whose legs share a <see cref="TransferGroupId"/>.
/// <para>
/// Append-only, like <see cref="Tasks.PointsAward"/>: approving and voiding stamp the row and nothing is ever deleted
/// or edited. Once a month is closed the row is stamped with <see cref="MonthCloseId"/> and can no longer be voided —
/// a correction is a <see cref="Contra"/> row dated today instead.
/// </para>
/// Only <see cref="LedgerStatus.Posted"/> rows count towards a balance.
/// </summary>
public sealed class LedgerEntry
{
    public const int ReferenceMaxLength = 100;
    public const int DescriptionMaxLength = 300;
    public const int ReasonMaxLength = 500;

    private LedgerEntry() { }

    public Guid Id { get; private set; }
    public Guid FundId { get; private set; }

    /// <summary>Which account the money landed in or left. Null only where no account is involved.</summary>
    public Guid? PaymentAccountId { get; private set; }

    public Guid CategoryId { get; private set; }

    /// <summary>The calendar date on the receipt, in Manila. A <c>date</c>, so month boundaries need no timezone maths.</summary>
    public DateOnly EntryDate { get; private set; }

    public LedgerDirection Direction { get; private set; }

    /// <summary>PHP, always more than 0. The sign is <see cref="Direction"/>, never the amount.</summary>
    public decimal Amount { get; private set; }

    /// <summary>The OR number or bank/GCash reference.</summary>
    public string? Reference { get; private set; }

    public string Description { get; private set; } = default!;

    /// <summary>Required for <see cref="LedgerSource.Manual"/> and <see cref="LedgerSource.ExpenseRequest"/>.</summary>
    public Guid? ReceiptFileId { get; private set; }

    public LedgerStatus Status { get; private set; }
    public LedgerSource Source { get; private set; }

    /// <summary>The row this entry was posted from (a payment, an expense request). Unique among live entries.</summary>
    public Guid? SourceId { get; private set; }

    /// <summary>The same value on both legs of a transfer.</summary>
    public Guid? TransferGroupId { get; private set; }

    /// <summary>Set on a <see cref="LedgerSource.Contra"/> row: the entry it offsets.</summary>
    public Guid? ContraOfEntryId { get; private set; }

    /// <summary>Stamped by the month close. A stamped row is frozen: correct it with a contra entry.</summary>
    public Guid? MonthCloseId { get; private set; }

    public Guid? EnteredById { get; private set; }
    public DateTimeOffset EnteredAt { get; private set; }
    public Guid? ApprovedById { get; private set; }
    public DateTimeOffset? ApprovedAt { get; private set; }
    public Guid? VoidedById { get; private set; }
    public DateTimeOffset? VoidedAt { get; private set; }
    public string? VoidReason { get; private set; }

    /// <summary>Positive for money in, negative for money out. The only place the sign is decided.</summary>
    public decimal SignedAmount => Direction == LedgerDirection.In ? Amount : -Amount;

    public bool IsPosted => Status == LedgerStatus.Posted;
    public bool IsVoided => Status == LedgerStatus.Voided;
    public bool IsClosed => MonthCloseId is not null;

    /// <summary>An entry added by the finance assistant. It is not money until the CFO approves it.</summary>
    public static LedgerEntry Pending(NewLedgerEntry data, Guid actorId, DateTimeOffset at)
    {
        var entry = Build(data, actorId, at);
        entry.Status = LedgerStatus.Pending;
        return entry;
    }

    /// <summary>
    /// An entry that counts immediately: typed by the CFO, or written by the system from a business event that was
    /// already approved (a registration marked paid, a transfer, the surplus sweep). The approver is the enterer.
    /// </summary>
    public static LedgerEntry Posted(NewLedgerEntry data, Guid? actorId, DateTimeOffset at)
    {
        var entry = Build(data, actorId, at);
        entry.Status = LedgerStatus.Posted;
        entry.ApprovedById = actorId;
        entry.ApprovedAt = at;
        return entry;
    }

    /// <summary>Pending to Posted. The approver may not be the person who entered it: the caller enforces that.</summary>
    public void Approve(Guid actorId, DateTimeOffset at)
    {
        if (IsVoided) throw new DomainException("This entry was voided.");
        if (IsPosted) throw new DomainException("This entry is already posted.");
        Status = LedgerStatus.Posted;
        ApprovedById = actorId;
        ApprovedAt = at;
    }

    /// <summary>Cancels the entry with a reason. Never a delete, and never allowed once the month is closed.</summary>
    public void Void(string reason, Guid actorId, DateTimeOffset at)
    {
        if (IsVoided) throw new DomainException("This entry is already voided.");
        if (IsClosed) throw new DomainException("This month is closed. Post a correcting entry dated today instead.");
        VoidReason = FinanceRules.Required(reason, "Reason", ReasonMaxLength);
        Status = LedgerStatus.Voided;
        VoidedById = actorId;
        VoidedAt = at;
    }

    /// <summary>
    /// The two legs of a transfer between funds: one <see cref="LedgerDirection.Out"/> of <paramref name="from"/> and
    /// one <see cref="LedgerDirection.In"/> to <paramref name="to"/>, sharing a group id. Both are posted at once.
    /// </summary>
    public static (LedgerEntry Out, LedgerEntry In) Transfer(
        Fund from, Fund to, Guid categoryId, DateOnly entryDate, decimal amount, string description,
        Guid? actorId, DateTimeOffset at, LedgerSource source = LedgerSource.Transfer)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        if (from.Id == to.Id) throw new DomainException("Pick two different funds for a transfer.");
        if (source is not (LedgerSource.Transfer or LedgerSource.SurplusSweep))
            throw new DomainException("A transfer is recorded as a transfer or a surplus sweep.");
        from.EnsureOpen();
        to.EnsureOpen();
        var group = Guid.CreateVersion7(at);
        var text = FinanceRules.Required(description, "Description", DescriptionMaxLength);

        var outLeg = Posted(
            new NewLedgerEntry(from.Id, null, categoryId, entryDate, LedgerDirection.Out, amount, null, text, null, source, null),
            actorId, at);
        var inLeg = Posted(
            new NewLedgerEntry(to.Id, null, categoryId, entryDate, LedgerDirection.In, amount, null, text, null, source, null),
            actorId, at);
        outLeg.TransferGroupId = group;
        inLeg.TransferGroupId = group;
        return (outLeg, inLeg);
    }

    /// <summary>
    /// Offsets <paramref name="original"/> with an opposite entry dated <paramref name="entryDate"/>. Used when the
    /// original sits in a closed month and so can no longer be voided.
    /// </summary>
    public static LedgerEntry Contra(
        LedgerEntry original, DateOnly entryDate, string reason, Guid? actorId, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(original);
        if (!original.IsPosted) throw new DomainException("Only a posted entry is offset by a contra entry.");
        if (original.Source == LedgerSource.Contra) throw new DomainException("A contra entry is not offset again.");
        var why = FinanceRules.Required(reason, "Reason", ReasonMaxLength);
        var entry = Posted(
            new NewLedgerEntry(
                original.FundId,
                original.PaymentAccountId,
                original.CategoryId,
                entryDate,
                original.Direction == LedgerDirection.In ? LedgerDirection.Out : LedgerDirection.In,
                original.Amount,
                original.Reference,
                Truncate($"Reversal · {why}", DescriptionMaxLength),
                null,
                LedgerSource.Contra,
                null),
            actorId, at);
        entry.ContraOfEntryId = original.Id;
        return entry;
    }

    /// <summary>Freezes the row as part of a month close.</summary>
    public void StampClosed(Guid monthCloseId)
    {
        if (!IsPosted) throw new DomainException("Only posted entries are closed.");
        if (IsClosed) throw new DomainException("This entry is already in a closed month.");
        MonthCloseId = monthCloseId;
    }

    /// <summary>Releases the row when its month is reopened.</summary>
    public void StampReopened() => MonthCloseId = null;

    private static LedgerEntry Build(NewLedgerEntry data, Guid? actorId, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(data);
        FinanceRules.Defined(data.Direction, "direction");
        FinanceRules.Defined(data.Source, "entry source");
        if (data.FundId == Guid.Empty) throw new DomainException("A fund is required.");
        if (data.CategoryId == Guid.Empty) throw new DomainException("A category is required.");
        if (RequiresReceipt(data.Source) && data.ReceiptFileId is null)
            throw new DomainException("Attach the receipt. Every entry you add needs one.");
        return new LedgerEntry
        {
            Id = Guid.CreateVersion7(at),
            FundId = data.FundId,
            PaymentAccountId = data.PaymentAccountId,
            CategoryId = data.CategoryId,
            EntryDate = data.EntryDate,
            Direction = data.Direction,
            Amount = FinanceRules.Money(data.Amount),
            Reference = FinanceRules.Optional(data.Reference, "Reference", ReferenceMaxLength),
            Description = FinanceRules.Required(data.Description, "Description", DescriptionMaxLength),
            ReceiptFileId = data.ReceiptFileId,
            Source = data.Source,
            SourceId = data.SourceId,
            EnteredById = actorId,
            EnteredAt = at,
        };
    }

    /// <summary>
    /// A receipt is required for everything a person types in. A system row (an auto-posted ticket payment, a transfer
    /// leg, the surplus sweep) has no external receipt to attach; for a ticket payment the registrant's own proof of
    /// payment is copied onto the entry when there is one.
    /// </summary>
    public static bool RequiresReceipt(LedgerSource source) =>
        source is LedgerSource.Manual or LedgerSource.ExpenseRequest;

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
