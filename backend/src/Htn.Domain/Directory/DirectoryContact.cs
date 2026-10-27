using Htn.Domain.Common;

namespace Htn.Domain.Directory;

/// <summary>The editable fields of a contact, shared by create and the general update.</summary>
public sealed record NewDirectoryContact(
    string Position, string HolderName, string Email, string? Phone, bool IsPrimary, string? CcEmails);

/// <summary>What <see cref="DirectoryContact.HeadChanged"/> hands back so the caller can write the history row.</summary>
public sealed record HolderChangeSnapshot(string PriorPosition, string PriorHolderName);

/// <summary>
/// One contact (a position, not necessarily a person) at a <see cref="DirectoryOrg"/> — e.g. "President" at a
/// partner company. "Still correct" and "the holder changed" both stamp <see cref="VerifiedAt"/>; whether the contact
/// needs a check is never stored, it is derived at read time from <see cref="VerifiedAt"/> against
/// <see cref="StaleAfterDays"/>. Voided, never deleted; <see cref="CcEmails"/> is a semicolon-delimited list (no
/// precedent elsewhere in the codebase for a string list column, so this is the simplest documented format).
/// </summary>
public sealed class DirectoryContact
{
    public const int PositionMaxLength = 80;
    public const int HolderNameMaxLength = 120;
    public const int EmailMaxLength = 254;
    public const int PhoneMaxLength = 20;
    public const int CcEmailsMaxLength = 1000;
    public const int ReasonMaxLength = 500;

    /// <summary>A contact that hasn't been verified in this many days needs a check (frame 13-equivalent rule for Phase 7).</summary>
    public const int StaleAfterDays = 90;

    private DirectoryContact() { }

    public Guid Id { get; private set; }
    public Guid OrgId { get; private set; }
    public string Position { get; private set; } = default!;
    public string HolderName { get; private set; } = default!;
    public string Email { get; private set; } = default!;
    public string? Phone { get; private set; }
    public bool IsPrimary { get; private set; }
    public DateTimeOffset? VerifiedAt { get; private set; }

    /// <summary>Semicolon-delimited additional recipients; null when there are none.</summary>
    public string? CcEmails { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public Guid? CreatedById { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public Guid? UpdatedById { get; private set; }
    public DateTimeOffset? VoidedAt { get; private set; }
    public Guid? VoidedById { get; private set; }
    public string? VoidReason { get; private set; }

    public bool IsVoided => VoidedAt is not null;

    /// <summary>True when never verified, or verified more than <see cref="StaleAfterDays"/> days ago.</summary>
    public bool NeedsCheck(DateTimeOffset now) => VerifiedAt is null || now - VerifiedAt.Value > TimeSpan.FromDays(StaleAfterDays);

    public static DirectoryContact Create(Guid orgId, NewDirectoryContact data, Guid actorId, DateTimeOffset at)
    {
        if (orgId == Guid.Empty) throw new DomainException("An organization is required.");
        var contact = new DirectoryContact
        {
            Id = Guid.CreateVersion7(at),
            OrgId = orgId,
            CreatedAt = at,
            CreatedById = actorId,
            UpdatedAt = at,
            UpdatedById = actorId,
        };
        contact.Apply(data, actorId, at);
        return contact;
    }

    public void Update(NewDirectoryContact data, Guid actorId, DateTimeOffset at)
    {
        EnsureLive();
        Apply(data, actorId, at);
    }

    /// <summary>Stamps the contact as verified and writes a <c>StillCorrect</c> history row (the caller writes it).</summary>
    public void StillCorrect(Guid actorId, DateTimeOffset at)
    {
        EnsureLive();
        VerifiedAt = at;
        Touch(actorId, at);
    }

    /// <summary>
    /// The position holder changed: updates the contact and stamps it verified (the new holder is, by definition,
    /// just confirmed). Returns the prior position/holder so the caller can snapshot it onto a history row.
    /// </summary>
    public HolderChangeSnapshot HeadChanged(
        string position, string holderName, string email, string? phone, Guid actorId, DateTimeOffset at)
    {
        EnsureLive();
        var snapshot = new HolderChangeSnapshot(Position, HolderName);
        Position = DirectoryRules.Required(position, "Position", PositionMaxLength);
        HolderName = DirectoryRules.Required(holderName, "Holder name", HolderNameMaxLength);
        SetEmail(email);
        Phone = DirectoryRules.Optional(phone, "Phone", PhoneMaxLength);
        VerifiedAt = at;
        Touch(actorId, at);
        return snapshot;
    }

    public void Void(string reason, Guid actorId, DateTimeOffset at)
    {
        if (IsVoided) throw new DomainException("This contact is already removed.");
        VoidReason = DirectoryRules.Required(reason, "Reason", ReasonMaxLength);
        VoidedAt = at;
        VoidedById = actorId;
    }

    private void Apply(NewDirectoryContact data, Guid actorId, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(data);
        Position = DirectoryRules.Required(data.Position, "Position", PositionMaxLength);
        HolderName = DirectoryRules.Required(data.HolderName, "Holder name", HolderNameMaxLength);
        SetEmail(data.Email);
        Phone = DirectoryRules.Optional(data.Phone, "Phone", PhoneMaxLength);
        IsPrimary = data.IsPrimary;
        CcEmails = NormalizeCc(data.CcEmails);
        Touch(actorId, at);
    }

    private void SetEmail(string email)
    {
        var value = DirectoryRules.Required(email, "Email", EmailMaxLength);
        if (!DirectoryRules.LooksLikeEmail(value)) throw new DomainException("Email is not valid.");
        Email = value;
    }

    private static string? NormalizeCc(string? value)
    {
        var v = value?.Trim();
        if (string.IsNullOrEmpty(v)) return null;
        var parts = v.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        foreach (var p in parts)
            if (!DirectoryRules.LooksLikeEmail(p)) throw new DomainException($"'{p}' is not a valid cc email.");
        var joined = string.Join(';', parts);
        if (joined.Length > CcEmailsMaxLength) throw new DomainException($"Cc emails must be at most {CcEmailsMaxLength} characters.");
        return joined.Length == 0 ? null : joined;
    }

    private void Touch(Guid actorId, DateTimeOffset at)
    {
        UpdatedAt = at;
        UpdatedById = actorId;
    }

    private void EnsureLive()
    {
        if (IsVoided) throw new DomainException("This contact has been removed.");
    }
}
