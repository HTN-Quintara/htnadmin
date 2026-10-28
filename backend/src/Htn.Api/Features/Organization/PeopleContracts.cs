using Htn.Domain.Organization;

namespace Htn.Api.Features.Organization;

public static class AccountStatuses
{
    public const string Active = "Active";
    public const string InvitePending = "InvitePending";
    public const string NoLogin = "NoLogin";
    public const string Deactivated = "Deactivated";
}

/// <summary>
/// A roster row. For non-Board callers <see cref="Email"/> is masked ("m•••@hackthenorth.ph") and
/// <see cref="AccountStatus"/> / <see cref="LastSignInAt"/> are null.
/// </summary>
public sealed record PersonDto(
    Guid Id,
    string FirstName,
    string? MiddleName,
    string LastName,
    string Email,
    PersonLevel Level,
    string? Position,
    bool IsActive,
    string? AccountStatus,
    DateTimeOffset? LastSignInAt,
    DateTimeOffset CreatedAt,
    bool IsCfo,
    bool IsFinanceAssistant);

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);

/// <summary>
/// The People page's "Create Member" dialog fee choice (Phase 8B). <see cref="PaidAtEvent"/> and
/// <see cref="NotPaidYet"/> have no distinct server behavior — both simply create the Person with no
/// <c>Membership</c> row yet ("dues not paid yet") — only <see cref="PaidNow"/> also records the payment.
/// Ignored unless <see cref="CreatePersonRequest.Level"/> is <see cref="PersonLevel.Member"/>.
/// </summary>
public enum MembershipFeeChoice
{
    PaidAtEvent,
    PaidNow,
    NotPaidYet,
}

public sealed record CreatePersonRequest(
    string FirstName,
    string? MiddleName,
    string LastName,
    string Email,
    PersonLevel? Level,
    string? Position,
    MembershipFeeChoice? MembershipFeeChoice = null);

/// <summary>Optional body for POST /api/people/{id}/deactivate.</summary>
public sealed record DeactivatePersonRequest(string? Reason);

/// <summary>Short reference to a person (position holders, finance roles).</summary>
public sealed record PersonRefDto(Guid Id, string FirstName, string LastName, PersonLevel Level);

public sealed record PersonPositionDto(Guid Id, string Title, PositionKind Kind);

public sealed record PersonHistoryDto(DateTimeOffset At, string Kind, string? From, string? To, string? Reason, string? ActorName);

/// <summary>An event this person checked in at (matched by email, case-insensitive), from check-in records.</summary>
public sealed record PersonAttendanceDto(Guid EventId, string EventName, DateTimeOffset CheckedInAt);

/// <summary>
/// The Member-level Membership section (Phase 8B), for the person's current main-event year.
/// <see cref="Paid"/> false means <see cref="FeePaid"/>/<see cref="PaidAt"/>/<see cref="ValidUntil"/> are null and
/// <see cref="EventName"/> names where dues can be collected instead ("not paid yet · collect at HTN Con 2026").
/// </summary>
public sealed record MembershipSummaryDto(
    int MembershipYear, bool Paid, decimal? FeePaid, DateTimeOffset? PaidAt, string? EventName, DateTimeOffset? ValidUntil);

/// <summary>GET /api/people/{id}. <see cref="History"/>, <see cref="Attendance"/> and <see cref="Membership"/> are
/// empty/null for non-Board callers.</summary>
public sealed record PersonDetailDto(
    PersonDto Person, IReadOnlyList<PersonPositionDto> Positions, IReadOnlyList<PersonHistoryDto> History,
    IReadOnlyList<PersonAttendanceDto> Attendance, MembershipSummaryDto? Membership = null);

/// <summary>One hit from the "Create Member" dialog's attendee-eligibility search, across every event.</summary>
public sealed record EligibleAttendeeHitDto(Guid AttendeeId, string Name, string Email, bool Eligible, bool AlreadyMember);

public sealed record PromotePersonRequest(PersonLevel? ToLevel, string? Reason);

public sealed record UpdatePersonRequest(string FirstName, string? MiddleName, string LastName, string Email, string? Position);

public sealed record ImportRowDto(int Row, string FirstName, string LastName, string Email, string Level, IReadOnlyList<string> Errors);

public sealed record ImportResultDto(IReadOnlyList<ImportRowDto> Rows, int Valid, int Invalid, int Created);
