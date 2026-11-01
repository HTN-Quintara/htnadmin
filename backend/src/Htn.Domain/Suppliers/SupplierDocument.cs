using Htn.Domain.Common;

namespace Htn.Domain.Suppliers;

/// <summary>
/// One uploaded document on a <see cref="Supplier"/> (BIR 2303, business permit, or other) — Phase 7C. Deactivate-not-
/// delete, matching the no-hard-delete rule: a document is never updated in place either, because the file it
/// points at is immutable (<see cref="Htn.Domain.Files.StoredFile"/>). Judgment call, documented here since the brief
/// asked for it explicitly: when staff upload a new BIR 2303 to replace a stale one, the endpoint voids the old row
/// with a reason and creates a brand-new row for the new file, rather than "updating" this one in place — so the old
/// file and who/when it was uploaded are never lost, and the entity needs no mutation method at all beyond
/// <see cref="Void"/>. This mirrors how <see cref="Htn.Domain.Directory.DirectoryContactHistory"/> is an immutable
/// trail rather than an editable row, closer than <c>DirectoryOrg.SetLogo</c> (which does overwrite in place,
/// because a logo has no compliance/audit value worth keeping a history of — a BIR 2303 does).
/// </summary>
public sealed class SupplierDocument
{
    public const int ReasonMaxLength = 500;

    private SupplierDocument() { }

    public Guid Id { get; private set; }
    public Guid SupplierId { get; private set; }
    public SupplierDocumentKind Kind { get; private set; }
    public Guid FileId { get; private set; }
    public DateTimeOffset UploadedAt { get; private set; }
    public Guid? UploadedById { get; private set; }
    public DateTimeOffset? VoidedAt { get; private set; }
    public Guid? VoidedById { get; private set; }
    public string? VoidReason { get; private set; }

    public bool IsVoided => VoidedAt is not null;

    public static SupplierDocument Create(Guid supplierId, SupplierDocumentKind kind, Guid fileId, Guid? actorId, DateTimeOffset at)
    {
        if (supplierId == Guid.Empty) throw new DomainException("A supplier is required.");
        SupplierRules.Defined(kind, "document kind");
        if (fileId == Guid.Empty) throw new DomainException("A file is required.");
        return new SupplierDocument
        {
            Id = Guid.CreateVersion7(at),
            SupplierId = supplierId,
            Kind = kind,
            FileId = fileId,
            UploadedAt = at,
            UploadedById = actorId,
        };
    }

    public void Void(string reason, Guid actorId, DateTimeOffset at)
    {
        if (IsVoided) throw new DomainException("This document is already removed.");
        VoidReason = SupplierRules.Required(reason, "Reason", ReasonMaxLength);
        VoidedAt = at;
        VoidedById = actorId;
    }
}
