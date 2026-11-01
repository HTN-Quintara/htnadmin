using Htn.Domain.Committees;
using Htn.Domain.Events;
using Htn.Domain.Files;
using Htn.Domain.Finance;
using Htn.Domain.Suppliers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Htn.Infrastructure.Data.Configurations;

internal sealed class SupplierConfiguration : IEntityTypeConfiguration<Supplier>
{
    public const string NameIndex = "ux_suppliers_name_key";

    public void Configure(EntityTypeBuilder<Supplier> b)
    {
        b.ToTable("suppliers", t =>
        {
            t.HasCheckConstraint("ck_suppliers_category", CheckConstraints.In("category", Enum.GetNames<SupplierCategory>()));
            t.HasCheckConstraint("ck_suppliers_deactivated",
                "(deactivated_at IS NULL) = (deactivate_reason IS NULL) AND (deactivated_at IS NULL) = (deactivated_by_id IS NULL)");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Category).HasConversion<string>().HasMaxLength(16);
        b.Property(x => x.Name).HasMaxLength(Supplier.NameMaxLength).IsRequired();
        b.Property(x => x.NameKey).HasMaxLength(Htn.Domain.Common.NameKey.MaxLength).IsRequired();
        b.Property(x => x.ContactName).HasMaxLength(Supplier.ContactNameMaxLength);
        b.Property(x => x.ContactEmail).HasMaxLength(Supplier.ContactEmailMaxLength);
        b.Property(x => x.ContactPhone).HasMaxLength(Supplier.ContactPhoneMaxLength);
        b.Property(x => x.Tin).HasMaxLength(Supplier.TinMaxLength);
        b.Property(x => x.BankDetails).HasMaxLength(Supplier.BankDetailsMaxLength);
        b.Property(x => x.Notes).HasMaxLength(Supplier.NotesMaxLength);
        b.Property(x => x.DeactivateReason).HasMaxLength(Supplier.ReasonMaxLength);
        b.Property<uint>(EventConfiguration.RowVersion).HasColumnName("xmin").HasColumnType("xid").IsRowVersion();
        b.Ignore(x => x.IsActive);
        // One live supplier per normalized name globally (this list is organization-wide, not per event).
        b.HasIndex(x => x.NameKey).IsUnique().HasDatabaseName(NameIndex).HasFilter("deactivated_at IS NULL");
        b.HasIndex(x => x.Category).HasDatabaseName("ix_suppliers_category");
        b.HasOne<StoredFile>().WithMany().HasForeignKey(x => x.LogoFileId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class SupplierDocumentConfiguration : IEntityTypeConfiguration<SupplierDocument>
{
    public void Configure(EntityTypeBuilder<SupplierDocument> b)
    {
        b.ToTable("supplier_documents", t =>
        {
            t.HasCheckConstraint("ck_supplier_documents_kind", CheckConstraints.In("kind", Enum.GetNames<SupplierDocumentKind>()));
            t.HasCheckConstraint("ck_supplier_documents_void",
                "(voided_at IS NULL) = (void_reason IS NULL) AND (voided_at IS NULL) = (voided_by_id IS NULL)");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Kind).HasConversion<string>().HasMaxLength(16);
        b.Property(x => x.VoidReason).HasMaxLength(SupplierDocument.ReasonMaxLength);
        b.Property<uint>(EventConfiguration.RowVersion).HasColumnName("xmin").HasColumnType("xid").IsRowVersion();
        b.Ignore(x => x.IsVoided);
        b.HasIndex(x => x.SupplierId).HasDatabaseName("ix_supplier_documents_supplier");
        b.HasOne<Supplier>().WithMany().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<StoredFile>().WithMany().HasForeignKey(x => x.FileId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class EventSupplierConfiguration : IEntityTypeConfiguration<EventSupplier>
{
    public const string EventSupplierIndex = "ux_event_suppliers_event_supplier";

    public void Configure(EntityTypeBuilder<EventSupplier> b)
    {
        b.ToTable("event_suppliers", t =>
        {
            t.HasCheckConstraint("ck_event_suppliers_status", CheckConstraints.In("status", Enum.GetNames<SupplierHireStatus>()));
            t.HasCheckConstraint("ck_event_suppliers_void",
                "(voided_at IS NULL) = (void_reason IS NULL) AND (voided_at IS NULL) = (voided_by_id IS NULL)");
            t.HasCheckConstraint("ck_event_suppliers_money",
                "(contract_amount IS NULL OR contract_amount >= 0) AND (paid_amount IS NULL OR paid_amount >= 0) " +
                "AND (next_payment_amount IS NULL OR next_payment_amount >= 0)");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
        b.Property(x => x.Note).HasMaxLength(EventSupplier.NoteMaxLength);
        b.Property(x => x.VoidReason).HasMaxLength(EventSupplier.ReasonMaxLength);
        b.Property<uint>(EventConfiguration.RowVersion).HasColumnName("xmin").HasColumnType("xid").IsRowVersion();
        b.Ignore(x => x.IsVoided);
        b.Ignore(x => x.IsQuotation);
        // Re-hiring the same supplier for the same event is allowed once the prior hire is voided, but there is
        // only ever one *live* hire of a given supplier per event at a time.
        b.HasIndex(x => new { x.EventId, x.SupplierId }).IsUnique().HasDatabaseName(EventSupplierIndex).HasFilter("voided_at IS NULL");
        b.HasIndex(x => x.EventId).HasDatabaseName("ix_event_suppliers_event");
        b.HasIndex(x => x.OwnerCommitteeId).HasDatabaseName("ix_event_suppliers_owner_committee");
        b.HasOne<Event>().WithMany().HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Supplier>().WithMany().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict);
        // The budget's stable category (never an EventBudgetLine.Id, which is replaced wholesale on every save).
        b.HasOne<LedgerCategory>().WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Committee>().WithMany().HasForeignKey(x => x.OwnerCommitteeId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class SupplierRatingConfiguration : IEntityTypeConfiguration<SupplierRating>
{
    public const string HireRaterIndex = "ux_supplier_ratings_hire_rater";

    public void Configure(EntityTypeBuilder<SupplierRating> b)
    {
        b.ToTable("supplier_ratings", t =>
        {
            t.HasCheckConstraint("ck_supplier_ratings_stars", "stars BETWEEN 1 AND 5");
            t.HasCheckConstraint("ck_supplier_ratings_hire_again", CheckConstraints.In("hire_again", Enum.GetNames<HireAgain>()));
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Tags).HasMaxLength(SupplierRating.TagsMaxLength);
        b.Property(x => x.HireAgain).HasConversion<string>().HasMaxLength(8);
        b.Property(x => x.Note).HasMaxLength(SupplierRating.NoteMaxLength);
        // One rating per (hire, rater): ratings are never edited, so this is the only guard against a double submit.
        b.HasIndex(x => new { x.EventSupplierId, x.RaterId }).IsUnique().HasDatabaseName(HireRaterIndex);
        b.HasOne<EventSupplier>().WithMany().HasForeignKey(x => x.EventSupplierId).OnDelete(DeleteBehavior.Restrict);
    }
}
