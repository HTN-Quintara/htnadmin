using Htn.Domain.Common;

namespace Htn.Domain.Speakers;

/// <summary>
/// A speaker of one event. Created directly, or when a speaker lead is confirmed (then <see cref="LeadId"/> points
/// back at the lead). Travel details, the confirmation link and the plus-one hang off this record in Phase 6D; here
/// it carries the person, their confirmation status and their photo. Voided with a reason, never deleted.
/// </summary>
public sealed class Speaker
{
    public const int NameMaxLength = 120;
    public const int EmailMaxLength = 254;
    public const int AffiliationMaxLength = 120;
    public const int ReasonMaxLength = 500;

    private Speaker() { }

    public Guid Id { get; private set; }
    public Guid EventId { get; private set; }

    /// <summary>The lead this speaker was confirmed from, when it came through the pipeline.</summary>
    public Guid? LeadId { get; private set; }

    public string PersonName { get; private set; } = default!;
    public string Email { get; private set; } = default!;
    public string? Affiliation { get; private set; }
    public SpeakerStatus Status { get; private set; }
    public Guid? PhotoFileId { get; private set; }
    public DateTimeOffset? PhotoApprovedAt { get; private set; }
    public Guid? PhotoApprovedById { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public Guid? CreatedById { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public Guid? UpdatedById { get; private set; }
    public DateTimeOffset? VoidedAt { get; private set; }
    public Guid? VoidedById { get; private set; }
    public string? VoidReason { get; private set; }

    public bool IsConfirmed => Status == SpeakerStatus.Confirmed;
    public bool IsVoided => VoidedAt is not null;
    public bool PhotoApproved => PhotoApprovedAt is not null;

    /// <summary>A speaker added by hand, not through the pipeline. Starts Invited.</summary>
    public static Speaker Create(Guid eventId, string personName, string email, string? affiliation, Guid actorId, DateTimeOffset at) =>
        Build(eventId, null, personName, email, affiliation, SpeakerStatus.Invited, actorId, at);

    /// <summary>A speaker created from a confirmed speaker lead, carrying the name and email across.</summary>
    public static Speaker FromLead(Guid eventId, Guid leadId, string personName, string email, string? affiliation, Guid actorId, DateTimeOffset at) =>
        Build(eventId, leadId, personName, email, affiliation, SpeakerStatus.Confirmed, actorId, at);

    public void Update(string personName, string email, string? affiliation, Guid actorId, DateTimeOffset at)
    {
        EnsureLive();
        PersonName = Clean(personName, "Name", NameMaxLength);
        Email = Email_(email);
        Affiliation = Optional(affiliation, "Affiliation", AffiliationMaxLength);
        Touch(actorId, at);
    }

    public void Confirm(Guid actorId, DateTimeOffset at)
    {
        EnsureLive();
        if (Status == SpeakerStatus.Confirmed) return;
        Status = SpeakerStatus.Confirmed;
        Touch(actorId, at);
    }

    public void Decline(Guid actorId, DateTimeOffset at)
    {
        EnsureLive();
        Status = SpeakerStatus.Declined;
        Touch(actorId, at);
    }

    public void SetPhoto(Guid? photoFileId, Guid actorId, DateTimeOffset at)
    {
        EnsureLive();
        PhotoFileId = photoFileId;
        // A new photo is unapproved until a human approves it for the website.
        PhotoApprovedAt = null;
        PhotoApprovedById = null;
        Touch(actorId, at);
    }

    public void ApprovePhoto(Guid actorId, DateTimeOffset at)
    {
        EnsureLive();
        if (PhotoFileId is null) throw new DomainException("There is no photo to approve.");
        PhotoApprovedAt = at;
        PhotoApprovedById = actorId;
        Touch(actorId, at);
    }

    public void Void(string reason, Guid actorId, DateTimeOffset at)
    {
        if (IsVoided) throw new DomainException("This speaker is already removed.");
        VoidReason = Clean(reason, "Reason", ReasonMaxLength);
        VoidedAt = at;
        VoidedById = actorId;
    }

    private static Speaker Build(Guid eventId, Guid? leadId, string personName, string email, string? affiliation, SpeakerStatus status, Guid actorId, DateTimeOffset at)
    {
        if (eventId == Guid.Empty) throw new DomainException("An event is required.");
        return new Speaker
        {
            Id = Guid.CreateVersion7(at),
            EventId = eventId,
            LeadId = leadId,
            PersonName = Clean(personName, "Name", NameMaxLength),
            Email = Email_(email),
            Affiliation = Optional(affiliation, "Affiliation", AffiliationMaxLength),
            Status = status,
            CreatedAt = at,
            CreatedById = actorId,
            UpdatedAt = at,
            UpdatedById = actorId,
        };
    }

    private void Touch(Guid actorId, DateTimeOffset at) { UpdatedAt = at; UpdatedById = actorId; }
    private void EnsureLive() { if (IsVoided) throw new DomainException("This speaker has been removed."); }

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

    private static string Email_(string email)
    {
        var v = email?.Trim() ?? "";
        if (v.Length == 0 || v.Length > EmailMaxLength || !v.Contains('@', StringComparison.Ordinal) || v.Contains(' '))
            throw new DomainException("A valid email is required.");
        return v.ToLowerInvariant();
    }
}
