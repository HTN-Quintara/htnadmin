using Htn.Domain.Directory;
using Htn.Domain.Events;
using Htn.Domain.Files;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Htn.Infrastructure.Data.Configurations;

internal sealed class DirectoryOrgConfiguration : IEntityTypeConfiguration<DirectoryOrg>
{
    public const string NameIndex = "ux_directory_orgs_name_key";

    public void Configure(EntityTypeBuilder<DirectoryOrg> b)
    {
        b.ToTable("directory_orgs", t =>
        {
            t.HasCheckConstraint("ck_directory_orgs_kind", CheckConstraints.In("kind", Enum.GetNames<DirectoryOrgKind>()));
            t.HasCheckConstraint("ck_directory_orgs_website", "website_url IS NULL OR website_url ~ '^https?://'");
            // Deactivated with a reason, never deleted.
            t.HasCheckConstraint("ck_directory_orgs_deactivated",
                "(deactivated_at IS NULL) = (deactivate_reason IS NULL) AND (deactivated_at IS NULL) = (deactivated_by_id IS NULL)");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Kind).HasConversion<string>().HasMaxLength(16);
        b.Property(x => x.Name).HasMaxLength(DirectoryOrg.NameMaxLength).IsRequired();
        b.Property(x => x.NameKey).HasMaxLength(Htn.Domain.Common.NameKey.MaxLength).IsRequired();
        b.Property(x => x.AddresseeSalutation).HasMaxLength(DirectoryOrg.SalutationMaxLength);
        b.Property(x => x.WebsiteUrl).HasMaxLength(DirectoryOrg.WebsiteMaxLength);
        b.Property(x => x.Notes).HasMaxLength(DirectoryOrg.NotesMaxLength);
        b.Property(x => x.DeactivateReason).HasMaxLength(DirectoryOrg.ReasonMaxLength);
        b.Property<uint>(EventConfiguration.RowVersion).HasColumnName("xmin").HasColumnType("xid").IsRowVersion();
        b.Ignore(x => x.IsActive);
        // One live organization per normalized name globally (this list is organization-wide, not per event).
        b.HasIndex(x => x.NameKey).IsUnique().HasDatabaseName(NameIndex).HasFilter("deactivated_at IS NULL");
        b.HasIndex(x => x.Kind).HasDatabaseName("ix_directory_orgs_kind");
        b.HasOne<StoredFile>().WithMany().HasForeignKey(x => x.LogoFileId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class DirectoryContactConfiguration : IEntityTypeConfiguration<DirectoryContact>
{
    public const string PrimaryIndex = "ux_directory_contacts_primary";

    public void Configure(EntityTypeBuilder<DirectoryContact> b)
    {
        b.ToTable("directory_contacts", t =>
        {
            t.HasCheckConstraint("ck_directory_contacts_void",
                "(voided_at IS NULL) = (void_reason IS NULL) AND (voided_at IS NULL) = (voided_by_id IS NULL)");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Position).HasMaxLength(DirectoryContact.PositionMaxLength).IsRequired();
        b.Property(x => x.HolderName).HasMaxLength(DirectoryContact.HolderNameMaxLength).IsRequired();
        b.Property(x => x.Email).HasMaxLength(DirectoryContact.EmailMaxLength).IsRequired();
        b.Property(x => x.Phone).HasMaxLength(DirectoryContact.PhoneMaxLength);
        b.Property(x => x.CcEmails).HasMaxLength(DirectoryContact.CcEmailsMaxLength);
        b.Property(x => x.VoidReason).HasMaxLength(DirectoryContact.ReasonMaxLength);
        b.Property<uint>(EventConfiguration.RowVersion).HasColumnName("xmin").HasColumnType("xid").IsRowVersion();
        b.Ignore(x => x.IsVoided);
        b.HasIndex(x => x.OrgId).HasDatabaseName("ix_directory_contacts_org");
        // One live primary contact per organization.
        b.HasIndex(x => x.OrgId).IsUnique().HasDatabaseName(PrimaryIndex).HasFilter("is_primary AND voided_at IS NULL");
        b.HasOne<DirectoryOrg>().WithMany().HasForeignKey(x => x.OrgId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class DirectoryContactHistoryConfiguration : IEntityTypeConfiguration<DirectoryContactHistory>
{
    public void Configure(EntityTypeBuilder<DirectoryContactHistory> b)
    {
        b.ToTable("directory_contact_history", t =>
        {
            t.HasCheckConstraint("ck_directory_contact_history_kind",
                CheckConstraints.In("kind", Enum.GetNames<DirectoryContactHistoryKind>()));
            // A holder-changed row is self-contained: it snapshots who held the position before.
            t.HasCheckConstraint("ck_directory_contact_history_holder_changed",
                "kind <> 'HolderChanged' OR (prior_position IS NOT NULL AND prior_holder_name IS NOT NULL)");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Kind).HasConversion<string>().HasMaxLength(16);
        b.Property(x => x.Note).HasMaxLength(DirectoryContactHistory.NoteMaxLength);
        b.Property(x => x.PriorPosition).HasMaxLength(DirectoryContactHistory.PositionMaxLength);
        b.Property(x => x.PriorHolderName).HasMaxLength(DirectoryContactHistory.HolderNameMaxLength);
        b.HasIndex(x => new { x.OrgId, x.At }).HasDatabaseName("ix_directory_contact_history_org");
        b.HasIndex(x => x.ContactId).HasDatabaseName("ix_directory_contact_history_contact");
        b.HasOne<DirectoryOrg>().WithMany().HasForeignKey(x => x.OrgId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<DirectoryContact>().WithMany().HasForeignKey(x => x.ContactId).OnDelete(DeleteBehavior.Restrict);
    }
}
