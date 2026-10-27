using Htn.Domain.Common;

namespace Htn.Domain.Leads;

/// <summary>The editable fields of a lead, shared by create and update.</summary>
public sealed record NewLead(
    LeadKind Kind,
    string Subject,
    string? ContactName,
    string? ContactRole,
    string? ContactEmail,
    string? ContactMobile,
    Guid? AskTierId,
    decimal? AskAmount,
    string? AskNote,
    Guid? OwnerPersonId,
    string? NextStep,
    DateOnly? NextStepDue);

/// <summary>
/// A sponsor or speaker prospect on the outreach board (frame 17). One table, a <see cref="LeadKind"/> discriminator.
/// Moves along the pipeline Prospect → Contacted → In talks → Confirmed / Declined; confirming wires it to the
/// sponsor or speaker it became (<see cref="SponsorId"/> / <see cref="SpeakerId"/>). Status changes are audited;
/// the row itself is never deleted.
/// </summary>
public sealed class Lead
{
    public const int SubjectMaxLength = 160;
    public const int ContactNameMaxLength = 120;
    public const int ContactRoleMaxLength = 80;
    public const int EmailMaxLength = 254;
    public const int MobileMaxLength = 13;
    public const int AskNoteMaxLength = 120;
    public const int NextStepMaxLength = 200;
    public const int ReasonMaxLength = 300;

    private Lead() { }

    public Guid Id { get; private set; }
    public Guid EventId { get; private set; }
    public LeadKind Kind { get; private set; }
    public LeadStatus Status { get; private set; }

    /// <summary>The organization (sponsor lead) or person (speaker lead) name.</summary>
    public string Subject { get; private set; } = default!;

    /// <summary>Normalized match key for the subject (Phase 7 Directory match).</summary>
    public string NameKey { get; private set; } = default!;

    public string? ContactName { get; private set; }
    public string? ContactRole { get; private set; }
    public string? ContactEmail { get; private set; }
    public string? ContactMobile { get; private set; }

    /// <summary>Sponsor leads only: the tier and/or amount being asked for.</summary>
    public Guid? AskTierId { get; private set; }

    public decimal? AskAmount { get; private set; }
    public string? AskNote { get; private set; }
    public Guid? OwnerPersonId { get; private set; }
    public string? NextStep { get; private set; }
    public DateOnly? NextStepDue { get; private set; }
    public LeadSource Source { get; private set; }
    public Guid? SponsorId { get; private set; }
    public Guid? SpeakerId { get; private set; }
    public DateTimeOffset? ConfirmedAt { get; private set; }
    public Guid? ConfirmedById { get; private set; }
    public DateTimeOffset? DeclinedAt { get; private set; }
    public Guid? DeclinedById { get; private set; }
    public string? DeclineReason { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public Guid? CreatedById { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public Guid? UpdatedById { get; private set; }

    public bool IsConfirmed => Status == LeadStatus.Confirmed;
    public bool IsDeclined => Status == LeadStatus.Declined;
    public bool IsLinked => SponsorId is not null || SpeakerId is not null;

    public static Lead Create(Guid eventId, NewLead data, Guid actorId, DateTimeOffset at)
    {
        if (eventId == Guid.Empty) throw new DomainException("An event is required.");
        var lead = new Lead
        {
            Id = Guid.CreateVersion7(at),
            EventId = eventId,
            Kind = Defined(data.Kind),
            Status = LeadStatus.Prospect,
            Source = LeadSource.Manual,
            CreatedAt = at,
            CreatedById = actorId,
            UpdatedAt = at,
            UpdatedById = actorId,
        };
        lead.Apply(data, actorId, at);
        return lead;
    }

    public void Update(NewLead data, Guid actorId, DateTimeOffset at)
    {
        if (IsConfirmed || IsDeclined) throw new DomainException("This lead is closed. Reopen it to edit the details.");
        if (Defined(data.Kind) != Kind) throw new DomainException("A lead's kind cannot change.");
        Apply(data, actorId, at);
    }

    /// <summary>Moves the lead to <paramref name="status"/>, following the pipeline. Declining needs a reason.</summary>
    public void MoveTo(LeadStatus status, string? reason, Guid actorId, DateTimeOffset at)
    {
        if (!Enum.IsDefined(status)) throw new DomainException("Unknown status.");
        if (status == Status) return;
        if (IsConfirmed && IsLinked)
            throw new DomainException("This lead is confirmed and already linked.");
        switch (status)
        {
            case LeadStatus.Declined:
                DeclineReason = Clean(reason, "Reason", ReasonMaxLength);
                DeclinedAt = at;
                DeclinedById = actorId;
                ConfirmedAt = null;
                ConfirmedById = null;
                break;
            case LeadStatus.Confirmed:
                ConfirmedAt = at;
                ConfirmedById = actorId;
                DeclinedAt = null;
                DeclinedById = null;
                DeclineReason = null;
                break;
            default:
                // Reopening from a terminal state, or moving between open stages, clears the terminal stamps.
                ConfirmedAt = null;
                ConfirmedById = null;
                DeclinedAt = null;
                DeclinedById = null;
                DeclineReason = null;
                break;
        }
        Status = status;
        Touch(actorId, at);
    }

    /// <summary>Confirms and links the lead to the sponsor it became. Idempotent on the link.</summary>
    public void LinkSponsor(Guid sponsorId, Guid actorId, DateTimeOffset at)
    {
        if (Kind != LeadKind.Sponsor) throw new DomainException("This is not a sponsor lead.");
        if (SponsorId is not null) throw new DomainException("This lead is already linked to a sponsor.");
        if (sponsorId == Guid.Empty) throw new DomainException("A sponsor is required.");
        SponsorId = sponsorId;
        ConfirmInternal(actorId, at);
    }

    /// <summary>Confirms and links the lead to the speaker it became.</summary>
    public void LinkSpeaker(Guid speakerId, Guid actorId, DateTimeOffset at)
    {
        if (Kind != LeadKind.Speaker) throw new DomainException("This is not a speaker lead.");
        if (SpeakerId is not null) throw new DomainException("This lead is already linked to a speaker.");
        if (speakerId == Guid.Empty) throw new DomainException("A speaker is required.");
        SpeakerId = speakerId;
        ConfirmInternal(actorId, at);
    }

    private void ConfirmInternal(Guid actorId, DateTimeOffset at)
    {
        Status = LeadStatus.Confirmed;
        ConfirmedAt = at;
        ConfirmedById = actorId;
        DeclinedAt = null;
        DeclinedById = null;
        DeclineReason = null;
        Touch(actorId, at);
    }

    private void Apply(NewLead data, Guid actorId, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(data);
        Subject = Clean(data.Subject, Kind == LeadKind.Sponsor ? "Organization" : "Name", SubjectMaxLength);
        NameKey = Htn.Domain.Common.NameKey.Of(Subject) ?? throw new DomainException("Name must contain letters or numbers.");
        ContactName = Optional(data.ContactName, "Contact name", ContactNameMaxLength);
        ContactRole = Optional(data.ContactRole, "Contact role", ContactRoleMaxLength);
        ContactEmail = Optional(data.ContactEmail, "Contact email", EmailMaxLength);
        ContactMobile = Optional(data.ContactMobile, "Contact mobile", MobileMaxLength);
        if (Kind == LeadKind.Sponsor)
        {
            AskTierId = data.AskTierId == Guid.Empty ? null : data.AskTierId;
            AskAmount = data.AskAmount is { } a ? Money(a) : null;
            AskNote = Optional(data.AskNote, "Ask", AskNoteMaxLength);
        }
        else
        {
            AskTierId = null;
            AskAmount = null;
            AskNote = Optional(data.AskNote, "Ask", AskNoteMaxLength);
        }
        OwnerPersonId = data.OwnerPersonId == Guid.Empty ? null : data.OwnerPersonId;
        NextStep = Optional(data.NextStep, "Next step", NextStepMaxLength);
        NextStepDue = data.NextStepDue;
        Touch(actorId, at);
    }

    private void Touch(Guid actorId, DateTimeOffset at) { UpdatedAt = at; UpdatedById = actorId; }

    private static LeadKind Defined(LeadKind kind)
    {
        if (!Enum.IsDefined(kind)) throw new DomainException("Unknown lead kind.");
        return kind;
    }

    private static decimal Money(decimal amount)
    {
        if (amount <= 0m) throw new DomainException("Ask amount must be more than 0.");
        if (decimal.Round(amount, 2) != amount) throw new DomainException("Ask amount can have at most 2 decimal places.");
        return amount;
    }

    private static string Clean(string? value, string field, int max)
    {
        var v = value?.Trim();
        if (string.IsNullOrEmpty(v)) throw new DomainException($"{field} is required.");
        if (v.Length > max) throw new DomainException($"{field} must be at most {max} characters.");
        return v;
    }

    private static string? Optional(string? value, string field, int max)
    {
        var v = value?.Trim();
        if (string.IsNullOrEmpty(v)) return null;
        if (v.Length > max) throw new DomainException($"{field} must be at most {max} characters.");
        return v;
    }
}
