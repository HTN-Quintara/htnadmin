using Htn.Domain.Common;

namespace Htn.Domain.Sponsors;

/// <summary>The editable details of a sponsor, shared by create and update.</summary>
public sealed record NewSponsor(
    SponsorKind Kind,
    string Name,
    string? DisplayName,
    bool IsAnonymous,
    string? ContactName,
    string? ContactRole,
    string? ContactEmail,
    string? ContactMobile,
    Guid? TierId,
    decimal? CashAmount,
    string? InKindDescription,
    int? InKindItemCount,
    int CompedSeats,
    string? WebsiteUrl);

/// <summary>
/// A sponsor of one event: an organization or a credited individual. Its contribution is a cash amount (in PHP), an
/// in-kind description, or both — never free-text money. Confirming records the signed contract; marking the cash
/// received is what posts it to the ledger (the endpoint does the posting, in its own transaction). Voided with a
/// reason, never deleted, and never once its money is in the ledger.
/// </summary>
public sealed class Sponsor
{
    public const int NameMaxLength = 160;
    public const int ContactNameMaxLength = 120;
    public const int ContactRoleMaxLength = 80;
    public const int EmailMaxLength = 254;
    public const int MobileMaxLength = 13;
    public const int InKindMaxLength = 300;
    public const int WebsiteMaxLength = 300;
    public const int ReferenceMaxLength = 60;
    public const int ReasonMaxLength = 500;
    public const int MaxCompedSeats = 200;

    private Sponsor() { }

    public Guid Id { get; private set; }
    public Guid EventId { get; private set; }
    public SponsorKind Kind { get; private set; }
    public string Name { get; private set; } = default!;

    /// <summary>Normalized match key (<see cref="NameKey"/>), for grouping and the Phase 7 Directory match.</summary>
    public string NameKey { get; private set; } = default!;

    /// <summary>How an individual is credited on materials; null for an organization.</summary>
    public string? DisplayName { get; private set; }

    public bool IsAnonymous { get; private set; }
    public string? ContactName { get; private set; }
    public string? ContactRole { get; private set; }
    public string? ContactEmail { get; private set; }
    public string? ContactMobile { get; private set; }
    public Guid? TierId { get; private set; }
    public decimal? CashAmount { get; private set; }
    public string? InKindDescription { get; private set; }
    public int? InKindItemCount { get; private set; }
    public SponsorStatus Status { get; private set; }
    public DateTimeOffset? ConfirmedAt { get; private set; }
    public Guid? ConfirmedById { get; private set; }

    /// <summary>When the cash was recorded received (and so posted to the ledger), the account and the reference.</summary>
    public DateTimeOffset? CashReceivedAt { get; private set; }

    public Guid? CashPaymentAccountId { get; private set; }
    public string? CashReference { get; private set; }

    /// <summary>Entitlement: how many comped seats this sponsor gets. Seeded from the tier, overridable.</summary>
    public int CompedSeats { get; private set; }

    public Guid? LogoFileId { get; private set; }
    public string? WebsiteUrl { get; private set; }

    /// <summary>Phase 7 Directory join, populated by <c>DirectoryOps.BackfillAsync</c> matching on <see cref="NameKey"/>.</summary>
    public Guid? DirectoryOrgId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public Guid? CreatedById { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public Guid? UpdatedById { get; private set; }
    public DateTimeOffset? VoidedAt { get; private set; }
    public Guid? VoidedById { get; private set; }
    public string? VoidReason { get; private set; }

    public bool IsConfirmed => Status == SponsorStatus.Confirmed;
    public bool IsVoided => VoidedAt is not null;
    public bool CashPosted => CashReceivedAt is not null;

    public static Sponsor Create(Guid eventId, NewSponsor data, Guid actorId, DateTimeOffset at)
    {
        if (eventId == Guid.Empty) throw new DomainException("An event is required.");
        var sponsor = new Sponsor
        {
            Id = Guid.CreateVersion7(at),
            EventId = eventId,
            Status = SponsorStatus.AwaitingContract,
            CreatedAt = at,
            CreatedById = actorId,
            UpdatedAt = at,
            UpdatedById = actorId,
        };
        sponsor.Apply(data, actorId, at);
        return sponsor;
    }

    public void Update(NewSponsor data, Guid actorId, DateTimeOffset at)
    {
        EnsureLive();
        // The cash figure is frozen once it has posted; correct it by reversing the receipt first.
        if (CashPosted && data.CashAmount != CashAmount)
            throw new DomainException("This sponsor's cash is already in the ledger. Reverse the receipt before changing the amount.");
        Apply(data, actorId, at);
    }

    public void Confirm(Guid actorId, DateTimeOffset at)
    {
        EnsureLive();
        if (IsConfirmed) throw new DomainException("This sponsor is already confirmed.");
        Status = SponsorStatus.Confirmed;
        ConfirmedAt = at;
        ConfirmedById = actorId;
        Touch(actorId, at);
    }

    public void Unconfirm(Guid actorId, DateTimeOffset at)
    {
        EnsureLive();
        if (!IsConfirmed) throw new DomainException("This sponsor is not confirmed.");
        if (CashPosted) throw new DomainException("This sponsor's cash is already in the ledger. Reverse the receipt first.");
        Status = SponsorStatus.AwaitingContract;
        ConfirmedAt = null;
        ConfirmedById = null;
        Touch(actorId, at);
    }

    /// <summary>Records that the cash came in. The caller posts the matching ledger entry in the same transaction.</summary>
    public void MarkCashReceived(Guid paymentAccountId, string? reference, Guid actorId, DateTimeOffset at)
    {
        EnsureLive();
        if (!IsConfirmed) throw new DomainException("Confirm the sponsor before recording its cash.");
        if (CashAmount is null) throw new DomainException("This sponsor has no cash contribution to record.");
        if (CashPosted) throw new DomainException("This sponsor's cash was already recorded.");
        if (paymentAccountId == Guid.Empty) throw new DomainException("Pick the account the money landed in.");
        CashPaymentAccountId = paymentAccountId;
        CashReference = SponsorRules.Optional(reference, "Reference", ReferenceMaxLength);
        CashReceivedAt = at;
        Touch(actorId, at);
    }

    /// <summary>Undoes <see cref="MarkCashReceived"/> after the ledger entry has been reversed, so it can be recorded again.</summary>
    public void ClearCashReceipt(Guid actorId, DateTimeOffset at)
    {
        EnsureLive();
        if (!CashPosted) throw new DomainException("This sponsor's cash was not recorded as received.");
        CashReceivedAt = null;
        CashPaymentAccountId = null;
        CashReference = null;
        Touch(actorId, at);
    }

    public void SetLogo(Guid? logoFileId, Guid actorId, DateTimeOffset at)
    {
        EnsureLive();
        LogoFileId = logoFileId;
        Touch(actorId, at);
    }

    public void Void(string reason, Guid actorId, DateTimeOffset at)
    {
        if (IsVoided) throw new DomainException("This sponsor is already removed.");
        if (CashPosted) throw new DomainException("This sponsor's cash is in the ledger. Reverse the receipt before removing it.");
        VoidReason = SponsorRules.Required(reason, "Reason", ReasonMaxLength);
        VoidedAt = at;
        VoidedById = actorId;
    }

    private void Apply(NewSponsor data, Guid actorId, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(data);
        SponsorRules.Defined(data.Kind, "sponsor kind");
        Kind = data.Kind;
        Name = SponsorRules.Required(data.Name, "Name", NameMaxLength);
        NameKey = Htn.Domain.Common.NameKey.Of(Name) ?? throw new DomainException("Name must contain letters or numbers.");
        IsAnonymous = data.IsAnonymous && Kind == SponsorKind.Individual;
        DisplayName = Kind == SponsorKind.Individual && !IsAnonymous
            ? SponsorRules.Optional(data.DisplayName, "Display name", NameMaxLength)
            : null;
        if (Kind == SponsorKind.Individual && !IsAnonymous && DisplayName is null) DisplayName = Name;
        ContactName = SponsorRules.Optional(data.ContactName, "Contact name", ContactNameMaxLength);
        ContactRole = SponsorRules.Optional(data.ContactRole, "Contact role", ContactRoleMaxLength);
        ContactEmail = SponsorRules.Optional(data.ContactEmail, "Contact email", EmailMaxLength);
        if (ContactEmail is not null && !RegistrationEmail(ContactEmail))
            throw new DomainException("Contact email is not valid.");
        ContactMobile = SponsorRules.Optional(data.ContactMobile, "Contact mobile", MobileMaxLength);
        TierId = data.TierId == Guid.Empty ? null : data.TierId;
        CashAmount = data.CashAmount is { } cash ? SponsorRules.Money(cash, "Cash amount") : null;
        InKindDescription = SponsorRules.Optional(data.InKindDescription, "In-kind", InKindMaxLength);
        InKindItemCount = data.InKindItemCount;
        if (InKindItemCount is not null && InKindDescription is null)
            throw new DomainException("Give the in-kind description before its item count.");
        if (InKindItemCount is < 1 or > 100_000) throw new DomainException("In-kind item count is out of range.");
        if (CashAmount is null && InKindDescription is null)
            throw new DomainException("A sponsor needs a cash amount, an in-kind contribution, or both.");
        WebsiteUrl = SponsorRules.Website(data.WebsiteUrl, WebsiteMaxLength);
        CompedSeats = SponsorRules.Count(data.CompedSeats, "Comped seats", 0, MaxCompedSeats);
        Touch(actorId, at);
    }

    private void Touch(Guid actorId, DateTimeOffset at)
    {
        UpdatedAt = at;
        UpdatedById = actorId;
    }

    private void EnsureLive()
    {
        if (IsVoided) throw new DomainException("This sponsor has been removed.");
    }

    // A light RFC-ish check: the real validation lives in the endpoint validator; this only guards the invariant.
    private static bool RegistrationEmail(string value) =>
        value.Contains('@', StringComparison.Ordinal) && value.Length <= EmailMaxLength && !value.Contains(' ');
}
