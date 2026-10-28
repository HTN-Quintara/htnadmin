using Htn.Domain.Common;
using Htn.Domain.Merch;

namespace Htn.Domain.PopUps;

/// <summary>The fields given when creating a pop-up store. Exactly one of <see cref="VenueDirectoryOrgId"/>/<see cref="VenueText"/> is set.</summary>
public sealed record NewPopUpStore(
    string Name, Guid? VenueDirectoryOrgId, string? VenueText, DateTimeOffset StartsAt, DateTimeOffset EndsAt,
    decimal FeeAmount, PopUpDiscountMode DiscountMode, int? GoonPct, int? ApprenticePct, int? MemberPct, int? BoardPct,
    Guid OwnerCommitteeId);

/// <summary>
/// A pop-up store (Phase 8C): leftover stock sold through the same POS as an event, not tied to any one event, run
/// by the team of an <see cref="Htn.Domain.Committees.Committee.PopUpsAllowed"/> committee. See <see cref="PopUpStatus"/>
/// for the forward-only lifecycle and <see cref="PopUpDiscountMode"/> for its discount table.
/// </summary>
public sealed class PopUpStore
{
    public const int NameMaxLength = 160;
    public const int VenueTextMaxLength = 300;
    public const int ReasonMaxLength = 500;

    /// <summary>The one allowed next step from each live status. There is no backward edge, ever.</summary>
    private static readonly Dictionary<PopUpStatus, PopUpStatus> NextStatus = new()
    {
        [PopUpStatus.Planned] = PopUpStatus.Open,
        [PopUpStatus.Open] = PopUpStatus.NeedsCount,
    };

    private PopUpStore() { }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = default!;
    public Guid? VenueDirectoryOrgId { get; private set; }
    public string? VenueText { get; private set; }
    public DateTimeOffset StartsAt { get; private set; }
    public DateTimeOffset EndsAt { get; private set; }
    public decimal FeeAmount { get; private set; }
    public PopUpDiscountMode DiscountMode { get; private set; }
    public int? GoonPct { get; private set; }
    public int? ApprenticePct { get; private set; }
    public int? MemberPct { get; private set; }
    public int? BoardPct { get; private set; }
    public Guid OwnerCommitteeId { get; private set; }
    public PopUpStatus Status { get; private set; }

    /// <summary>Cash counted at the "close & count" step, kept for the money summary.</summary>
    public decimal? CashCounted { get; private set; }

    /// <summary>The missing items' value at their sale price, derived at the count step and frozen for the write-off.</summary>
    public decimal? MissingValue { get; private set; }

    public DateTimeOffset? WrittenOffAt { get; private set; }
    public Guid? WrittenOffById { get; private set; }
    public string? WriteOffReason { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public Guid CreatedById { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public Guid? UpdatedById { get; private set; }

    public bool IsWrittenOff => WrittenOffAt is not null;

    public static PopUpStore Create(NewPopUpStore data, Guid actorId, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(data);
        if ((data.VenueDirectoryOrgId is null) == string.IsNullOrWhiteSpace(data.VenueText))
            throw new DomainException("Give either a Directory venue or a venue name, not both or neither.");
        if (data.EndsAt <= data.StartsAt) throw new DomainException("End time must be after the start time.");
        if (data.OwnerCommitteeId == Guid.Empty) throw new DomainException("An owning committee is required.");
        MerchRules.Defined(data.DiscountMode, "discount mode");
        if (data.DiscountMode == PopUpDiscountMode.Custom
            && (data.GoonPct is null || data.ApprenticePct is null || data.MemberPct is null || data.BoardPct is null))
            throw new DomainException("Custom discounts need all four percentages.");
        if (data.DiscountMode != PopUpDiscountMode.Custom
            && (data.GoonPct is not null || data.ApprenticePct is not null || data.MemberPct is not null || data.BoardPct is not null))
            throw new DomainException("Percentages are only set when the discount mode is Custom.");

        return new PopUpStore
        {
            Id = Guid.CreateVersion7(at),
            Name = MerchRules.Required(data.Name, "Name", NameMaxLength),
            VenueDirectoryOrgId = data.VenueDirectoryOrgId,
            VenueText = MerchRules.Optional(data.VenueText, "Venue", VenueTextMaxLength),
            StartsAt = data.StartsAt,
            EndsAt = data.EndsAt,
            FeeAmount = MerchRules.NonNegativeMoney(data.FeeAmount, "Fee amount"),
            DiscountMode = data.DiscountMode,
            GoonPct = data.GoonPct is { } g ? MerchRules.Pct(g, "Goon discount") : null,
            ApprenticePct = data.ApprenticePct is { } a ? MerchRules.Pct(a, "Apprentice discount") : null,
            MemberPct = data.MemberPct is { } m ? MerchRules.Pct(m, "Member discount") : null,
            BoardPct = data.BoardPct is { } b ? MerchRules.Pct(b, "Board discount") : null,
            OwnerCommitteeId = data.OwnerCommitteeId,
            Status = PopUpStatus.Planned,
            CreatedAt = at,
            CreatedById = actorId,
            UpdatedAt = at,
            UpdatedById = actorId,
        };
    }

    /// <summary>The discount for a buyer at this level, resolving <see cref="PopUpDiscountMode.SameAsDefault"/> against the live org defaults passed in by the caller.</summary>
    public int PctFor(Organization.PersonLevel? level, int orgGoonPct, int orgApprenticePct, int orgMemberPct, int orgBoardPct)
    {
        if (DiscountMode == PopUpDiscountMode.None) return 0;
        if (level is null) return 0;
        if (DiscountMode == PopUpDiscountMode.SameAsDefault)
            return level switch
            {
                Organization.PersonLevel.Goon => orgGoonPct,
                Organization.PersonLevel.Apprentice => orgApprenticePct,
                Organization.PersonLevel.Member => orgMemberPct,
                Organization.PersonLevel.Board => orgBoardPct,
                _ => 0,
            };
        return level switch
        {
            Organization.PersonLevel.Goon => GoonPct ?? 0,
            Organization.PersonLevel.Apprentice => ApprenticePct ?? 0,
            Organization.PersonLevel.Member => MemberPct ?? 0,
            Organization.PersonLevel.Board => BoardPct ?? 0,
            _ => 0,
        };
    }

    public void Open(Guid actorId, DateTimeOffset at) => Advance(PopUpStatus.Open, actorId, at);

    public void Close(Guid actorId, DateTimeOffset at) => Advance(PopUpStatus.NeedsCount, actorId, at);

    /// <summary>
    /// The "close & count" step's outcome: the caller has already computed each line's missing qty and this total
    /// missing value from the sale-price snapshots. Moves to <see cref="PopUpStatus.ClosedBalanced"/> when nothing
    /// is missing, else <see cref="PopUpStatus.ClosedMissing"/> (permanently — see <see cref="PopUpStatus"/>).
    /// </summary>
    public void CompleteCount(decimal missingValue, decimal cashCounted, Guid actorId, DateTimeOffset at)
    {
        if (Status != PopUpStatus.NeedsCount) throw new DomainException($"Cannot count a pop-up in status {Status}.");
        if (missingValue < 0) throw new DomainException("Missing value cannot be negative.");
        MissingValue = decimal.Round(missingValue, 2);
        CashCounted = MerchRules.NonNegativeMoney(cashCounted, "Cash counted");
        Status = missingValue > 0 ? PopUpStatus.ClosedMissing : PopUpStatus.ClosedBalanced;
        Touch(actorId, at);
    }

    /// <summary>Records who wrote the missing stock off and when. Status itself never changes — see <see cref="PopUpStatus"/>.</summary>
    public void MarkWrittenOff(string reason, Guid actorId, DateTimeOffset at)
    {
        if (Status != PopUpStatus.ClosedMissing) throw new DomainException("Only a pop-up closed with something missing can be written off.");
        if (IsWrittenOff) throw new DomainException("This pop-up has already been written off.");
        WriteOffReason = MerchRules.Required(reason, "Reason", ReasonMaxLength);
        WrittenOffAt = at;
        WrittenOffById = actorId;
        Touch(actorId, at);
    }

    private void Advance(PopUpStatus next, Guid actorId, DateTimeOffset at)
    {
        if (!NextStatus.TryGetValue(Status, out var expected) || expected != next)
            throw new DomainException($"Cannot move a pop-up from {Status} to {next}.");
        Status = next;
        Touch(actorId, at);
    }

    private void Touch(Guid actorId, DateTimeOffset at)
    {
        UpdatedAt = at;
        UpdatedById = actorId;
    }
}
