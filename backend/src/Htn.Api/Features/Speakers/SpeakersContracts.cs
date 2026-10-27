using Htn.Domain.Events;

namespace Htn.Api.Features.Speakers;

// ---------- Staff ----------

public sealed record SpeakerKpiDto(string Label, string Value);

public sealed record SpeakerRowDto(
    Guid Id, string Name, string Session, string PhotoState,
    string LinkState, string? LinkStateSub, string Dates, string Travel, string PlusOne);

public sealed record SpeakersListDto(TravelMode TravelMode, IReadOnlyList<SpeakerKpiDto> Kpis, IReadOnlyList<SpeakerRowDto> Speakers);

public sealed record SpeakerLogDto(DateTimeOffset At, string What);

public sealed record SpeakerPlusOneDto(Guid Id, string Name, string Email, bool SharesRoom, string VoucherCode, bool Registered);

public sealed record SpeakerTravelDto(
    string? ArrivalDate, string? DepartureDate, string? FlyingFrom, string? PreferredArrivalTime,
    bool HasIdFullName, string? BusFrom, string? BusEta, bool NeedsTerminalPickup, string? RoomNote, string? NeedsNotes);

public sealed record SpeakerDetailDto(
    Guid Id, string Name, string Email, string? Affiliation, string Status, string Session,
    string PhotoState, bool CanApprovePhoto, SpeakerTravelDto? Travel, SpeakerPlusOneDto? PlusOne,
    IReadOnlyList<SpeakerLogDto> Log, bool CanSend, bool Sent);

public sealed record SpeakerPatchRequest(string? Name, string? Email, string? Affiliation);

public sealed record SpeakerTravelPatchRequest(
    DateOnly? ArrivalDate, DateOnly? DepartureDate, string? RoomNote);

public sealed record TravelModeRequest(TravelMode? TravelMode);

public sealed record SpeakerReasonRequest(string? Reason);

public sealed record SendUnsentResultDto(int Sent);

// ---------- Public ----------

public sealed record SpeakerLandingDto(
    string EventName, DateTimeOffset EventStartsAt, string SpeakerFirstName, string MaskedEmail,
    TravelMode TravelMode, DateTimeOffset ExpiresAt, string State, SpeakerFormStateDto? Summary);

public sealed record VerifyRequest(string? Code);

public sealed record VerifyResultDto(string SessionToken, DateTimeOffset SessionExpiresAt, SpeakerFormStateDto Form);

public sealed record SpeakerFormStateDto(
    string SpeakerName, string Session, TravelMode TravelMode, string? PhotoFileId, bool PhotoApproved,
    DateOnly? ArrivalDate, DateOnly? DepartureDate, string? FlyingFrom, string? PreferredArrivalTime,
    string? BusFrom, string? BusEta, bool NeedsTerminalPickup, string? NeedsNotes,
    bool HasPlusOne, string? PlusOneName, string? PlusOneEmail, bool PlusOneSharesRoom, bool Submitted);

public sealed record SubmitRequest(
    DateOnly? ArrivalDate, DateOnly? DepartureDate, string? FlyingFrom, string? PreferredArrivalTime, string? IdFullName,
    string? BusFrom, string? BusEta, bool? NeedsTerminalPickup, string? NeedsNotes, bool? Consent,
    bool? HasPlusOne, string? PlusOneName, string? PlusOneEmail, bool? PlusOneSharesRoom);

public sealed record SubmitResultDto(DateTimeOffset SubmittedAt, string? PlusOneVoucherCode);

public sealed record PhotoUploadResultDto(Guid FileId);
