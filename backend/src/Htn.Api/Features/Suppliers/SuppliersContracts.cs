using System.Text.Json.Serialization;
using Htn.Domain.Suppliers;

namespace Htn.Api.Features.Suppliers;

// Requests: every property nullable so FluentValidation, not the model binder, produces the message.

public sealed record SupplierRequest(
    SupplierCategory? Category, string? Name, string? ContactName, string? ContactEmail, string? ContactPhone,
    bool? IssuesOr, string? Tin, string? BankDetails, string? Notes);

public sealed record SupplierUpdateRequest(
    string? ContactName, string? ContactEmail, string? ContactPhone, bool? IssuesOr, string? Tin, string? BankDetails, string? Notes);

public sealed record ReasonRequest(string? Reason);

public sealed record NewHireRequest(Guid? SupplierId, Guid? CategoryId, Guid? OwnerCommitteeId, string? Note);

/// <summary>
/// Money, status and note, all optional and all independent: <c>Status</c>, when present, moves the hire forward
/// exactly one step through <see cref="EventSupplier.Advance"/> (409 <c>invalid_transition</c> on any other move);
/// the money fields and note are applied unconditionally through <see cref="EventSupplier.Update"/>. The brief folds
/// "money/status fields" into one <c>PATCH</c> rather than a separate status-advance route like Invitations' —
/// documented here since it is the one place this slice departs from that otherwise-identical shape.
/// </summary>
public sealed record UpdateHireRequest(
    decimal? ContractAmount, decimal? PaidAmount, decimal? NextPaymentAmount, DateOnly? NextPaymentDue, string? Note, SupplierHireStatus? Status);

public sealed record RatingRequest(int? Stars, IReadOnlyList<string>? Tags, HireAgain? HireAgain, string? Note);

// Responses.

/// <summary>
/// The fields visible only to the board (<c>Suppliers.Manage</c>): TIN and bank details. Wrapped in its own object
/// rather than flattened, and omitted from the JSON entirely — not just nulled out — for a non-board caller,
/// via <see cref="JsonIgnoreCondition.WhenWritingNull"/> on the holding property. This is the "money-stripping DTO
/// shape" judgment call flagged in the brief: a committee lead's response to <c>GET /api/suppliers</c> never has a
/// <c>boardOnly</c> key at all, so a client can't accidentally render an absent field as if it were a blank one.
/// </summary>
public sealed record SupplierBoardOnlyDto(string? Tin, string? BankDetails);

public sealed record SupplierDto(
    Guid Id, SupplierCategory Category, string Name, string? ContactName, string? ContactEmail, string? ContactPhone,
    bool IssuesOr, string? Notes, Guid? LogoFileId, bool IsActive,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] SupplierBoardOnlyDto? BoardOnly);

public sealed record SupplierDocumentDto(
    Guid Id, Guid SupplierId, SupplierDocumentKind Kind, Guid FileId, DateTimeOffset UploadedAt, Guid? UploadedById,
    bool IsVoided, string? VoidReason);

/// <summary>Money fields on a hire, board-only — same "wrapped and omitted" shape as <see cref="SupplierBoardOnlyDto"/>.</summary>
public sealed record EventSupplierMoneyDto(decimal? ContractAmount, decimal? PaidAmount, decimal? NextPaymentAmount, DateOnly? NextPaymentDue);

public sealed record SupplierRatingDto(
    Guid Id, Guid EventSupplierId, Guid RaterId, int Stars, IReadOnlyList<string> Tags, HireAgain HireAgain, string? Note, DateTimeOffset CreatedAt);

public sealed record EventSupplierDto(
    Guid Id, Guid EventId, Guid SupplierId, string SupplierName, Guid CategoryId, string CategoryName,
    Guid OwnerCommitteeId, string OwnerCommitteeName, SupplierHireStatus Status, string? Note,
    bool IsVoided, string? VoidReason,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] EventSupplierMoneyDto? Money,
    double? AverageStars, int RatingCount, bool CanRate, bool AlreadyRatedByMe);

public sealed record SupplierDetailDto(
    SupplierDto Supplier, IReadOnlyList<SupplierDocumentDto> Documents, IReadOnlyList<EventSupplierDto> Hires);
