using Htn.Domain.Merch;
using Htn.Domain.Organization;
using Htn.Domain.PopUps;

namespace Htn.Api.Features.PopUps;

// Requests: every property nullable so FluentValidation, not the model binder, produces the message.

public sealed record NewPopUpRequest(
    string? Name, Guid? VenueDirectoryOrgId, string? VenueText, DateTimeOffset? StartsAt, DateTimeOffset? EndsAt,
    decimal? FeeAmount, PopUpDiscountMode? DiscountMode, int? GoonPct, int? ApprenticePct, int? MemberPct, int? BoardPct,
    Guid? OwnerCommitteeId, IReadOnlyList<Guid>? TeamPersonIds);

public sealed record AllocationLineRequest(Guid? VariantId, int? Qty);

public sealed record AllocationsRequest(IReadOnlyList<AllocationLineRequest>? Lines);

public sealed record CountLineRequest(Guid? VariantId, int? QtyReturned);

public sealed record CountRequest(IReadOnlyList<CountLineRequest>? Returned, decimal? CashCounted);

public sealed record WriteOffRequest(string? Reason);

public sealed record MoveToStoreRequest(IReadOnlyList<Guid>? VariantIds);

// Responses.

public sealed record PopUpTeamMemberDto(Guid PersonId, string Name);

public sealed record PopUpAllocationDto(
    Guid VariantId, string ItemName, string VariantLabel, int QtyBrought, int QtySold, int? QtyReturned, int? QtyMissing);

public sealed record PopUpDto(
    Guid Id, string Name, Guid? VenueDirectoryOrgId, string? VenueOrgName, string? VenueText, DateTimeOffset StartsAt,
    DateTimeOffset EndsAt, decimal FeeAmount, PopUpDiscountMode DiscountMode, int? GoonPct, int? ApprenticePct,
    int? MemberPct, int? BoardPct, Guid OwnerCommitteeId, string OwnerCommitteeName, PopUpStatus Status,
    IReadOnlyList<PopUpTeamMemberDto> Team, IReadOnlyList<PopUpAllocationDto> Allocations,
    decimal? TotalSales, decimal? DiscountsGiven, decimal? CashCounted, decimal? MissingValue,
    bool IsWrittenOff, DateTimeOffset? WrittenOffAt, bool CanManage, bool CanSell);

public sealed record LeftoverVariantDto(
    Guid ItemId, string ItemName, Guid VariantId, string VariantLabel, decimal Price, Guid? FromEventId,
    string FromEventName, int StockOnHand, int Reserved, int Available);
