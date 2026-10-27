using Htn.Domain.Common;

namespace Htn.Domain.Suppliers;

/// <summary>The fields given when creating a supplier: category and name never change afterwards.</summary>
public sealed record NewSupplier(
    SupplierCategory Category, string Name, string? ContactName, string? ContactEmail, string? ContactPhone,
    bool IssuesOr, string? Tin, string? BankDetails, string? Notes);

/// <summary>The editable fields of an existing supplier (category and name are frozen once set).</summary>
public sealed record UpdateSupplier(
    string? ContactName, string? ContactEmail, string? ContactPhone, bool IssuesOr, string? Tin, string? BankDetails, string? Notes);

/// <summary>
/// One supplier in the global HTN supplier list (Phase 7C): venue, catering, printing, tech/AV, merch or transport —
/// mirroring <see cref="Htn.Domain.Directory.DirectoryOrg"/>'s global-catalog-with-deactivate shape. <see cref="Category"/>
/// and <see cref="Name"/> (and so <see cref="NameKey"/>) are frozen once the row is created, exactly like
/// <c>DirectoryOrg.Kind</c>/<c>Name</c>: a rename or re-categorization is a new supplier, not an edit of this one,
/// enforced again at the database (<c>Suppliers_Guards</c>).
/// <para>
/// <see cref="Tin"/> and <see cref="BankDetails"/> are stored plainly here, same as every other field — the domain
/// rule "prices, TIN and bank details are visible to the board only" is enforced purely at the DTO projection
/// (<c>SuppliersViews</c> in the API layer), never by splitting or encrypting the entity. That is a deliberate
/// judgment call: nothing in the brief asked for field-level encryption, and the existing finance/sponsor slices
/// use the same "strip at the DTO" idiom for money.
/// </para>
/// Deactivated with a reason, never deleted.
/// </summary>
public sealed class Supplier
{
    public const int NameMaxLength = 160;
    public const int ContactNameMaxLength = 120;
    public const int ContactEmailMaxLength = 254;
    public const int ContactPhoneMaxLength = 20;
    public const int TinMaxLength = 40;
    public const int BankDetailsMaxLength = 500;
    public const int NotesMaxLength = 2000;
    public const int ReasonMaxLength = 500;

    private Supplier() { }

    public Guid Id { get; private set; }
    public SupplierCategory Category { get; private set; }
    public string Name { get; private set; } = default!;

    /// <summary>Normalized match key (<see cref="Htn.Domain.Common.NameKey"/>).</summary>
    public string NameKey { get; private set; } = default!;

    public string? ContactName { get; private set; }
    public string? ContactEmail { get; private set; }
    public string? ContactPhone { get; private set; }
    public bool IssuesOr { get; private set; }

    /// <summary>Board-only at the DTO layer; see the class remarks.</summary>
    public string? Tin { get; private set; }

    /// <summary>Board-only at the DTO layer; see the class remarks.</summary>
    public string? BankDetails { get; private set; }

    public string? Notes { get; private set; }
    public Guid? LogoFileId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public Guid? CreatedById { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public Guid? UpdatedById { get; private set; }
    public DateTimeOffset? DeactivatedAt { get; private set; }
    public Guid? DeactivatedById { get; private set; }
    public string? DeactivateReason { get; private set; }

    public bool IsActive => DeactivatedAt is null;

    public static Supplier Create(NewSupplier data, Guid actorId, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(data);
        SupplierRules.Defined(data.Category, "supplier category");
        var supplier = new Supplier
        {
            Id = Guid.CreateVersion7(at),
            Category = data.Category,
            Name = SupplierRules.Required(data.Name, "Name", NameMaxLength),
            CreatedAt = at,
            CreatedById = actorId,
            UpdatedAt = at,
            UpdatedById = actorId,
        };
        supplier.NameKey = Htn.Domain.Common.NameKey.Of(supplier.Name) ?? throw new DomainException("Name must contain letters or numbers.");
        supplier.ApplyEditable(data.ContactName, data.ContactEmail, data.ContactPhone, data.IssuesOr, data.Tin, data.BankDetails, data.Notes, actorId, at);
        return supplier;
    }

    public void Update(UpdateSupplier data, Guid actorId, DateTimeOffset at)
    {
        EnsureActive();
        ArgumentNullException.ThrowIfNull(data);
        ApplyEditable(data.ContactName, data.ContactEmail, data.ContactPhone, data.IssuesOr, data.Tin, data.BankDetails, data.Notes, actorId, at);
    }

    public void SetLogo(Guid? logoFileId, Guid actorId, DateTimeOffset at)
    {
        EnsureActive();
        LogoFileId = logoFileId;
        Touch(actorId, at);
    }

    public void Deactivate(string reason, Guid actorId, DateTimeOffset at)
    {
        if (!IsActive) throw new DomainException("This supplier is already deactivated.");
        DeactivateReason = SupplierRules.Required(reason, "Reason", ReasonMaxLength);
        DeactivatedAt = at;
        DeactivatedById = actorId;
    }

    private void ApplyEditable(
        string? contactName, string? contactEmail, string? contactPhone, bool issuesOr,
        string? tin, string? bankDetails, string? notes, Guid actorId, DateTimeOffset at)
    {
        ContactName = SupplierRules.Optional(contactName, "Contact name", ContactNameMaxLength);
        ContactEmail = SupplierRules.Optional(contactEmail, "Contact email", ContactEmailMaxLength);
        ContactPhone = SupplierRules.Optional(contactPhone, "Contact phone", ContactPhoneMaxLength);
        IssuesOr = issuesOr;
        Tin = SupplierRules.Optional(tin, "TIN", TinMaxLength);
        BankDetails = SupplierRules.Optional(bankDetails, "Bank details", BankDetailsMaxLength);
        Notes = SupplierRules.Optional(notes, "Notes", NotesMaxLength);
        Touch(actorId, at);
    }

    private void Touch(Guid actorId, DateTimeOffset at)
    {
        UpdatedAt = at;
        UpdatedById = actorId;
    }

    private void EnsureActive()
    {
        if (!IsActive) throw new DomainException("This supplier has been deactivated.");
    }
}
