using Htn.Domain.Events;
using Htn.Domain.Files;
using Htn.Domain.Registrations;
using Htn.Domain.Settings;
using Htn.Domain.Sponsors;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Htn.Infrastructure.Data.Configurations;

internal sealed class SponsorTierConfiguration : IEntityTypeConfiguration<SponsorTier>
{
    public const string EventNameIndex = "ux_sponsor_tiers_event_name";

    public void Configure(EntityTypeBuilder<SponsorTier> b)
    {
        b.ToTable("sponsor_tiers", t =>
        {
            t.HasCheckConstraint("ck_sponsor_tiers_class", CheckConstraints.In("class", Enum.GetNames<TierClass>()));
            t.HasCheckConstraint("ck_sponsor_tiers_slots", $"slots BETWEEN 1 AND {SponsorTier.MaxSlots}");
            t.HasCheckConstraint("ck_sponsor_tiers_comped", $"comped_seats BETWEEN 0 AND {SponsorTier.MaxCompedSeats}");
            t.HasCheckConstraint("ck_sponsor_tiers_cash",
                "cash_amount IS NULL OR (cash_amount > 0 AND round(cash_amount, 2) = cash_amount)");
            t.HasCheckConstraint("ck_sponsor_tiers_in_kind", "in_kind_item_count IS NULL OR in_kind_text IS NOT NULL");
            // A tier costs something: cash, in-kind, or both.
            t.HasCheckConstraint("ck_sponsor_tiers_contribution", "cash_amount IS NOT NULL OR in_kind_text IS NOT NULL");
            t.HasCheckConstraint("ck_sponsor_tiers_deactivated", "is_active = (deactivated_at IS NULL)");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Class).HasConversion<string>().HasMaxLength(16);
        b.Property(x => x.Name).HasMaxLength(SponsorTier.NameMaxLength).IsRequired();
        b.Property(x => x.PriceNote).HasMaxLength(SponsorTier.PriceNoteMaxLength);
        b.Property(x => x.InKindText).HasMaxLength(SponsorTier.InKindMaxLength);
        b.Property(x => x.Perks).HasColumnType("text[]");
        // One tier name per event (case-insensitive) among the live ones.
        b.HasIndex(x => x.EventId).HasDatabaseName("ix_sponsor_tiers_event");
        b.HasIndex("EventId", "Name").IsUnique().HasDatabaseName(EventNameIndex).HasFilter("deactivated_at IS NULL");
        b.HasOne<Event>().WithMany().HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class SponsorConfiguration : IEntityTypeConfiguration<Sponsor>
{
    public const string EventNameIndex = "ux_sponsors_event_name";

    public void Configure(EntityTypeBuilder<Sponsor> b)
    {
        b.ToTable("sponsors", t =>
        {
            t.HasCheckConstraint("ck_sponsors_kind", CheckConstraints.In("kind", Enum.GetNames<SponsorKind>()));
            t.HasCheckConstraint("ck_sponsors_status", CheckConstraints.In("status", Enum.GetNames<SponsorStatus>()));
            t.HasCheckConstraint("ck_sponsors_comped", $"comped_seats BETWEEN 0 AND {Sponsor.MaxCompedSeats}");
            t.HasCheckConstraint("ck_sponsors_cash",
                "cash_amount IS NULL OR (cash_amount > 0 AND round(cash_amount, 2) = cash_amount)");
            t.HasCheckConstraint("ck_sponsors_in_kind", "in_kind_item_count IS NULL OR in_kind_description IS NOT NULL");
            t.HasCheckConstraint("ck_sponsors_contribution",
                "cash_amount IS NOT NULL OR in_kind_description IS NOT NULL");
            // An individual is credited by a display name or as Anonymous; an organization needs neither.
            t.HasCheckConstraint("ck_sponsors_credit",
                "kind = 'Organization' OR is_anonymous OR display_name IS NOT NULL");
            t.HasCheckConstraint("ck_sponsors_anonymous", "NOT is_anonymous OR display_name IS NULL");
            t.HasCheckConstraint("ck_sponsors_org_not_anonymous", "kind = 'Individual' OR NOT is_anonymous");
            t.HasCheckConstraint("ck_sponsors_confirmed",
                "(status = 'Confirmed') = (confirmed_at IS NOT NULL) AND (confirmed_at IS NULL) = (confirmed_by_id IS NULL)");
            // Cash is only ever recorded received on a confirmed sponsor that has a cash amount and an account.
            t.HasCheckConstraint("ck_sponsors_cash_received",
                "(cash_received_at IS NULL) = (cash_payment_account_id IS NULL) " +
                "AND (cash_received_at IS NULL OR (cash_amount IS NOT NULL AND status = 'Confirmed'))");
            t.HasCheckConstraint("ck_sponsors_website", "website_url IS NULL OR website_url ~ '^https?://'");
            // Removed with a reason, and never while its money sits in the ledger.
            t.HasCheckConstraint("ck_sponsors_void",
                "(voided_at IS NULL) = (void_reason IS NULL) AND (voided_at IS NULL) = (voided_by_id IS NULL) " +
                "AND (voided_at IS NULL OR cash_received_at IS NULL)");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Kind).HasConversion<string>().HasMaxLength(16);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Name).HasMaxLength(Sponsor.NameMaxLength).IsRequired();
        b.Property(x => x.NameKey).HasMaxLength(Htn.Domain.Common.NameKey.MaxLength).IsRequired();
        b.Property(x => x.DisplayName).HasMaxLength(Sponsor.NameMaxLength);
        b.Property(x => x.ContactName).HasMaxLength(Sponsor.ContactNameMaxLength);
        b.Property(x => x.ContactRole).HasMaxLength(Sponsor.ContactRoleMaxLength);
        b.Property(x => x.ContactEmail).HasMaxLength(Sponsor.EmailMaxLength);
        b.Property(x => x.ContactMobile).HasMaxLength(Sponsor.MobileMaxLength);
        b.Property(x => x.InKindDescription).HasMaxLength(Sponsor.InKindMaxLength);
        b.Property(x => x.CashReference).HasMaxLength(Sponsor.ReferenceMaxLength);
        b.Property(x => x.WebsiteUrl).HasMaxLength(Sponsor.WebsiteMaxLength);
        b.Property(x => x.VoidReason).HasMaxLength(Sponsor.ReasonMaxLength);
        b.Property<uint>(EventConfiguration.RowVersion).HasColumnName("xmin").HasColumnType("xid").IsRowVersion();
        b.Ignore(x => x.IsConfirmed);
        b.Ignore(x => x.IsVoided);
        b.Ignore(x => x.CashPosted);
        b.HasIndex(x => new { x.EventId, x.Status }).HasDatabaseName("ix_sponsors_event_status");
        // One live sponsor per normalized name per event.
        b.HasIndex(x => new { x.EventId, x.NameKey }).IsUnique().HasDatabaseName(EventNameIndex)
            .HasFilter("voided_at IS NULL");
        // The Phase 7 Directory backfill joins sponsors to organizations by this key.
        b.HasIndex(x => x.NameKey).HasDatabaseName("ix_sponsors_name_key");
        b.HasIndex(x => x.DirectoryOrgId).HasDatabaseName("ix_sponsors_directory_org");
        b.HasOne<Event>().WithMany().HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<SponsorTier>().WithMany().HasForeignKey(x => x.TierId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<PaymentAccount>().WithMany().HasForeignKey(x => x.CashPaymentAccountId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<StoredFile>().WithMany().HasForeignKey(x => x.LogoFileId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Htn.Domain.Directory.DirectoryOrg>().WithMany().HasForeignKey(x => x.DirectoryOrgId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class SponsorPerkConfiguration : IEntityTypeConfiguration<SponsorPerk>
{
    public void Configure(EntityTypeBuilder<SponsorPerk> b)
    {
        b.ToTable("sponsor_perks", t =>
        {
            t.HasCheckConstraint("ck_sponsor_perks_removed", "(removed_at IS NULL) = (remove_reason IS NULL)");
            // A removed perk keeps whatever delivered stamp it had; it is simply final.
            t.HasCheckConstraint("ck_sponsor_perks_delivered",
                "(delivered_at IS NULL) = (delivered_by_id IS NULL)");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Text).HasMaxLength(SponsorPerk.TextMaxLength).IsRequired();
        b.Property(x => x.RemoveReason).HasMaxLength(SponsorPerk.ReasonMaxLength);
        b.Ignore(x => x.IsDelivered);
        b.Ignore(x => x.IsRemoved);
        b.HasIndex(x => new { x.SponsorId, x.SortOrder }).HasDatabaseName("ix_sponsor_perks_sponsor");
        b.HasOne<Sponsor>().WithMany().HasForeignKey(x => x.SponsorId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<SponsorTier>().WithMany().HasForeignKey(x => x.SourceTierId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class SponsorCompRegistrationConfiguration : IEntityTypeConfiguration<SponsorCompRegistration>
{
    public const string RegistrationIndex = "ux_sponsor_comp_registrations_registration";

    public void Configure(EntityTypeBuilder<SponsorCompRegistration> b)
    {
        b.ToTable("sponsor_comp_registrations");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        // A registration is one sponsor's comp at most.
        b.HasIndex(x => x.RegistrationId).IsUnique().HasDatabaseName(RegistrationIndex);
        b.HasIndex(x => x.SponsorId).HasDatabaseName("ix_sponsor_comp_registrations_sponsor");
        b.HasOne<Sponsor>().WithMany().HasForeignKey(x => x.SponsorId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Registration>().WithMany().HasForeignKey(x => x.RegistrationId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class EventPartnerSettingsConfiguration : IEntityTypeConfiguration<EventPartnerSettings>
{
    public const string EventIndex = "ux_event_partner_settings_event";

    public void Configure(EntityTypeBuilder<EventPartnerSettings> b)
    {
        b.ToTable("event_partner_settings", t =>
        {
            t.HasCheckConstraint("ck_event_partner_settings_basis",
                CheckConstraints.In("count_basis", Enum.GetNames<PartnerCountBasis>()));
            t.HasCheckConstraint("ck_event_partner_settings_threshold",
                $"threshold BETWEEN {EventPartnerSettings.MinThreshold} AND {EventPartnerSettings.MaxThreshold} " +
                $"AND threshold % {EventPartnerSettings.ThresholdStep} = 0");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.CountBasis).HasConversion<string>().HasMaxLength(20);
        b.Property<uint>(EventConfiguration.RowVersion).HasColumnName("xmin").HasColumnType("xid").IsRowVersion();
        b.HasIndex(x => x.EventId).IsUnique().HasDatabaseName(EventIndex);
        b.HasOne<Event>().WithMany().HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class EventPartnerConfiguration : IEntityTypeConfiguration<EventPartner>
{
    public const string EventNameIndex = "ux_event_partners_event_name";

    public void Configure(EntityTypeBuilder<EventPartner> b)
    {
        b.ToTable("event_partners", t =>
            t.HasCheckConstraint("ck_event_partners_untagged",
                "(untagged_at IS NULL) = (untag_reason IS NULL) " +
                "AND (website_url IS NULL OR website_url ~ '^https?://')"));
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.NameKey).HasMaxLength(Htn.Domain.Common.NameKey.MaxLength).IsRequired();
        b.Property(x => x.DisplayName).HasMaxLength(EventPartner.NameMaxLength).IsRequired();
        b.Property(x => x.OrgType).HasMaxLength(EventPartner.OrgTypeMaxLength);
        b.Property(x => x.WebsiteUrl).HasMaxLength(EventPartner.WebsiteMaxLength);
        b.Property(x => x.UntagReason).HasMaxLength(EventPartner.ReasonMaxLength);
        b.Ignore(x => x.IsTagged);
        // One partner row per normalized org name per event (a re-tag reuses the row).
        b.HasIndex(x => new { x.EventId, x.NameKey }).IsUnique().HasDatabaseName(EventNameIndex);
        // The Phase 7 Directory backfill joins partners to organizations by name_key.
        b.HasIndex(x => x.DirectoryOrgId).HasDatabaseName("ix_event_partners_directory_org");
        b.HasOne<Event>().WithMany().HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<StoredFile>().WithMany().HasForeignKey(x => x.LogoFileId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Htn.Domain.Directory.DirectoryOrg>().WithMany().HasForeignKey(x => x.DirectoryOrgId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class PartnerGroupLinkConfiguration : IEntityTypeConfiguration<PartnerGroupLink>
{
    public const string TokenIndex = "ux_partner_group_links_token";
    public const string OpenIndex = "ux_partner_group_links_open";

    public void Configure(EntityTypeBuilder<PartnerGroupLink> b)
    {
        b.ToTable("partner_group_links", t =>
        {
            t.HasCheckConstraint("ck_partner_group_links_token", "token_hash ~ '^[0-9a-f]{64}$'");
            t.HasCheckConstraint("ck_partner_group_links_revoke",
                "(revoked_at IS NULL) = (revoke_reason IS NULL) AND (revoked_at IS NULL) = (revoked_by_id IS NULL)");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.TokenHash).HasMaxLength(PartnerGroupLink.TokenHashLength).IsFixedLength().IsRequired();
        b.Property(x => x.RevokeReason).HasMaxLength(PartnerGroupLink.ReasonMaxLength);
        b.Ignore(x => x.IsRevoked);
        b.HasIndex(x => x.TokenHash).IsUnique().HasDatabaseName(TokenIndex);
        // One live link per partner.
        b.HasIndex(x => x.EventPartnerId).IsUnique().HasDatabaseName(OpenIndex).HasFilter("revoked_at IS NULL");
        b.HasOne<Event>().WithMany().HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<EventPartner>().WithMany().HasForeignKey(x => x.EventPartnerId).OnDelete(DeleteBehavior.Restrict);
    }
}
