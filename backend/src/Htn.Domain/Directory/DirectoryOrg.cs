using Htn.Domain.Common;

namespace Htn.Domain.Directory;

/// <summary>The fields given when creating an organization: its name and kind never change afterwards.</summary>
public sealed record NewDirectoryOrg(DirectoryOrgKind Kind, string Name, string? AddresseeSalutation, string? WebsiteUrl, string? Notes);

/// <summary>The editable fields of an existing organization (name and kind are frozen once set).</summary>
public sealed record UpdateDirectoryOrg(string? AddresseeSalutation, string? WebsiteUrl, string? Notes);

/// <summary>
/// One organization in the global HTN directory (government agency, company, university or non-profit) — Phase 7.
/// A sponsor, a lead and an event partner each keep their own per-event record and only ever reference an org by its
/// normalized <see cref="NameKey"/>; <see cref="DirectoryOrgId"/> on those rows is a denormalized join populated by
/// the backfill. The name and kind are frozen once the row is created (enforced again at the database): renaming an
/// organization here would silently break that join, so a rename is a new org, not an edit of this one. Deactivated
/// with a reason, never deleted.
/// </summary>
public sealed class DirectoryOrg
{
    public const int NameMaxLength = 160;
    public const int SalutationMaxLength = 160;
    public const int WebsiteMaxLength = 300;
    public const int NotesMaxLength = 2000;
    public const int ReasonMaxLength = 500;

    private DirectoryOrg() { }

    public Guid Id { get; private set; }
    public DirectoryOrgKind Kind { get; private set; }
    public string Name { get; private set; } = default!;

    /// <summary>Normalized match key (<see cref="Htn.Domain.Common.NameKey"/>), the join used by the backfill.</summary>
    public string NameKey { get; private set; } = default!;

    public string? AddresseeSalutation { get; private set; }
    public string? WebsiteUrl { get; private set; }
    public Guid? LogoFileId { get; private set; }
    public string? Notes { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public Guid? CreatedById { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public Guid? UpdatedById { get; private set; }
    public DateTimeOffset? DeactivatedAt { get; private set; }
    public Guid? DeactivatedById { get; private set; }
    public string? DeactivateReason { get; private set; }

    public bool IsActive => DeactivatedAt is null;

    public static DirectoryOrg Create(NewDirectoryOrg data, Guid actorId, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(data);
        DirectoryRules.Defined(data.Kind, "organization kind");
        var org = new DirectoryOrg
        {
            Id = Guid.CreateVersion7(at),
            Kind = data.Kind,
            Name = DirectoryRules.Required(data.Name, "Name", NameMaxLength),
            CreatedAt = at,
            CreatedById = actorId,
            UpdatedAt = at,
            UpdatedById = actorId,
        };
        org.NameKey = Htn.Domain.Common.NameKey.Of(org.Name) ?? throw new DomainException("Name must contain letters or numbers.");
        org.ApplyEditable(data.AddresseeSalutation, data.WebsiteUrl, data.Notes, actorId, at);
        return org;
    }

    public void Update(UpdateDirectoryOrg data, Guid actorId, DateTimeOffset at)
    {
        EnsureActive();
        ArgumentNullException.ThrowIfNull(data);
        ApplyEditable(data.AddresseeSalutation, data.WebsiteUrl, data.Notes, actorId, at);
    }

    public void SetLogo(Guid? logoFileId, Guid actorId, DateTimeOffset at)
    {
        EnsureActive();
        LogoFileId = logoFileId;
        Touch(actorId, at);
    }

    public void Deactivate(string reason, Guid actorId, DateTimeOffset at)
    {
        if (!IsActive) throw new DomainException("This organization is already deactivated.");
        DeactivateReason = DirectoryRules.Required(reason, "Reason", ReasonMaxLength);
        DeactivatedAt = at;
        DeactivatedById = actorId;
    }

    private void ApplyEditable(string? salutation, string? websiteUrl, string? notes, Guid actorId, DateTimeOffset at)
    {
        AddresseeSalutation = DirectoryRules.Optional(salutation, "Addressee salutation", SalutationMaxLength);
        WebsiteUrl = DirectoryRules.Website(websiteUrl, WebsiteMaxLength);
        Notes = DirectoryRules.Optional(notes, "Notes", NotesMaxLength);
        Touch(actorId, at);
    }

    private void Touch(Guid actorId, DateTimeOffset at)
    {
        UpdatedAt = at;
        UpdatedById = actorId;
    }

    private void EnsureActive()
    {
        if (!IsActive) throw new DomainException("This organization has been deactivated.");
    }
}
