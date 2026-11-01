using Htn.Domain.Committees;
using Htn.Domain.Directory;
using Htn.Domain.Merch;
using Htn.Domain.Organization;
using Htn.Domain.PopUps;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Htn.Infrastructure.Data.Configurations;

/// <summary>
/// Phase 8C: pop-up stores, their team and their stock allocations — mirrors <c>SuppliersConfiguration</c>'s flat,
/// all-in-one-file shape. Append-only/freeze/forward-only-status enforcement lives in the <c>PopUps_Guards</c>
/// migration.
/// </summary>
internal sealed class PopUpStoreConfiguration : IEntityTypeConfiguration<PopUpStore>
{
    public void Configure(EntityTypeBuilder<PopUpStore> b)
    {
        b.ToTable("pop_up_stores", t =>
        {
            t.HasCheckConstraint("ck_pop_up_stores_venue", "(venue_directory_org_id IS NOT NULL) <> (venue_text IS NOT NULL)");
            t.HasCheckConstraint("ck_pop_up_stores_dates", "ends_at > starts_at");
            t.HasCheckConstraint("ck_pop_up_stores_fee", "fee_amount >= 0");
            t.HasCheckConstraint("ck_pop_up_stores_status", CheckConstraints.In("status", Enum.GetNames<PopUpStatus>()));
            t.HasCheckConstraint("ck_pop_up_stores_discount_mode", CheckConstraints.In("discount_mode", Enum.GetNames<PopUpDiscountMode>()));
            t.HasCheckConstraint("ck_pop_up_stores_pct",
                "(goon_pct IS NULL OR goon_pct BETWEEN 0 AND 100) AND (apprentice_pct IS NULL OR apprentice_pct BETWEEN 0 AND 100) " +
                "AND (member_pct IS NULL OR member_pct BETWEEN 0 AND 100) AND (board_pct IS NULL OR board_pct BETWEEN 0 AND 100) " +
                "AND (discount_mode = 'Custom') = (goon_pct IS NOT NULL)");
            t.HasCheckConstraint("ck_pop_up_stores_written_off",
                "(written_off_at IS NULL) = (written_off_by_id IS NULL) AND (written_off_at IS NULL) = (write_off_reason IS NULL)");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Name).HasMaxLength(PopUpStore.NameMaxLength).IsRequired();
        b.Property(x => x.VenueText).HasMaxLength(PopUpStore.VenueTextMaxLength);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
        b.Property(x => x.DiscountMode).HasConversion<string>().HasMaxLength(14);
        b.Property(x => x.WriteOffReason).HasMaxLength(PopUpStore.ReasonMaxLength);
        b.Property<uint>(EventConfiguration.RowVersion).HasColumnName("xmin").HasColumnType("xid").IsRowVersion();
        b.Ignore(x => x.IsWrittenOff);
        b.HasIndex(x => x.OwnerCommitteeId).HasDatabaseName("ix_pop_up_stores_owner_committee");
        b.HasIndex(x => x.Status).HasDatabaseName("ix_pop_up_stores_status");
        b.HasOne<Committee>().WithMany().HasForeignKey(x => x.OwnerCommitteeId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<DirectoryOrg>().WithMany().HasForeignKey(x => x.VenueDirectoryOrgId).OnDelete(DeleteBehavior.Restrict).IsRequired(false);
    }
}

internal sealed class PopUpTeamMemberConfiguration : IEntityTypeConfiguration<PopUpTeamMember>
{
    public void Configure(EntityTypeBuilder<PopUpTeamMember> b)
    {
        b.ToTable("pop_up_team_members");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.HasIndex(x => new { x.PopUpId, x.PersonId }).IsUnique().HasDatabaseName("ux_pop_up_team_members_pop_up_person");
        b.HasOne<PopUpStore>().WithMany().HasForeignKey(x => x.PopUpId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Person>().WithMany().HasForeignKey(x => x.PersonId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class PopUpAllocationConfiguration : IEntityTypeConfiguration<PopUpAllocation>
{
    public void Configure(EntityTypeBuilder<PopUpAllocation> b)
    {
        b.ToTable("pop_up_allocations", t =>
        {
            t.HasCheckConstraint("ck_pop_up_allocations_qty", "qty_brought > 0 AND (qty_returned IS NULL OR qty_returned BETWEEN 0 AND qty_brought)");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.HasIndex(x => new { x.PopUpId, x.VariantId }).IsUnique().HasDatabaseName("ux_pop_up_allocations_pop_up_variant");
        b.HasOne<PopUpStore>().WithMany().HasForeignKey(x => x.PopUpId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<MerchVariant>().WithMany().HasForeignKey(x => x.VariantId).OnDelete(DeleteBehavior.Restrict);
    }
}
