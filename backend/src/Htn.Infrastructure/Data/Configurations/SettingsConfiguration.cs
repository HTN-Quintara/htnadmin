using Htn.Domain.Files;
using Htn.Domain.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Htn.Infrastructure.Data.Configurations;

internal sealed class OrgSettingsConfiguration : IEntityTypeConfiguration<OrgSettings>
{
    public void Configure(EntityTypeBuilder<OrgSettings> b)
    {
        b.ToTable("org_settings", t =>
        {
            t.HasCheckConstraint("ck_org_settings_singleton", $"id = {OrgSettings.SingletonId}");
            t.HasCheckConstraint("ck_org_settings_multipliers",
                "lead_multiplier BETWEEN 0 AND 2 AND collab_multiplier BETWEEN 0 AND 2 AND assistant_multiplier BETWEEN 0 AND 2");
            t.HasCheckConstraint("ck_org_settings_presets",
                $"preset_small >= {OrgSettings.MinPreset} AND preset_small < preset_medium AND preset_medium < preset_large " +
                $"AND preset_large < preset_xl AND preset_xl <= {OrgSettings.MaxPreset}");
            t.HasCheckConstraint("ck_org_settings_brand_color", "brand_color ~ '^#[0-9A-Fa-f]{6}$'");
            t.HasCheckConstraint("ck_org_settings_payment_deadline",
                $"payment_deadline_hours BETWEEN {OrgSettings.MinPaymentDeadlineHours} AND {OrgSettings.MaxPaymentDeadlineHours}");
            t.HasCheckConstraint("ck_org_settings_merch_discounts",
                "goon_merch_discount_pct BETWEEN 0 AND 100 AND apprentice_merch_discount_pct BETWEEN 0 AND 100 " +
                "AND member_merch_discount_pct BETWEEN 0 AND 100 AND board_merch_discount_pct BETWEEN 0 AND 100");
            t.HasCheckConstraint("ck_org_settings_email_mode", CheckConstraints.In("email_mode", Enum.GetNames<EmailDeliveryMode>()));
            // The unsaved seed row is Sandbox without an inbox (env applies); once saved, Sandbox needs one.
            t.HasCheckConstraint("ck_org_settings_email_sandbox_inbox",
                "email_updated_at IS NULL OR email_mode <> 'Sandbox' OR email_inbox_id IS NOT NULL");
            t.HasCheckConstraint("ck_org_settings_email_token",
                "(email_protected_api_token IS NULL) = (email_token_last4 IS NULL)");
        });
        b.HasKey(s => s.Id);
        b.Property(s => s.Id).ValueGeneratedNever();
        // Multipliers are not money: 0.0–2.0 in steps of 0.1.
        b.Property(s => s.LeadMultiplier).HasPrecision(3, 1);
        b.Property(s => s.CollabMultiplier).HasPrecision(3, 1);
        b.Property(s => s.AssistantMultiplier).HasPrecision(3, 1);
        b.Property(s => s.BrandColor).HasMaxLength(7).IsFixedLength().IsRequired();
        // Phase 8A: added to the existing table, so each gets the agreed default as its column default too, backfilling
        // the already-seeded singleton row instead of leaving it at 0.
        b.Property(s => s.GoonMerchDiscountPct).HasDefaultValue(20);
        b.Property(s => s.ApprenticeMerchDiscountPct).HasDefaultValue(15);
        b.Property(s => s.MemberMerchDiscountPct).HasDefaultValue(10);
        b.Property(s => s.BoardMerchDiscountPct).HasDefaultValue(0);
        // Mailtrap delivery settings, in the same row. Added to the seeded table, so required columns carry defaults.
        b.OwnsOne(s => s.Email, e =>
        {
            e.Property(x => x.Mode).HasColumnName("email_mode").HasConversion<string>().HasMaxLength(16)
                .HasDefaultValue(EmailDeliveryMode.Sandbox).HasSentinel((EmailDeliveryMode)(-1));
            e.Property(x => x.InboxId).HasColumnName("email_inbox_id").HasMaxLength(EmailDeliverySettings.InboxIdMaxLength);
            e.Property(x => x.FromEmail).HasColumnName("email_from_email").HasMaxLength(EmailDeliverySettings.EmailMaxLength)
                .IsRequired().HasDefaultValue(EmailDeliverySettings.DefaultFromEmail);
            e.Property(x => x.FromName).HasColumnName("email_from_name").HasMaxLength(EmailDeliverySettings.FromNameMaxLength)
                .IsRequired().HasDefaultValue(EmailDeliverySettings.DefaultFromName);
            e.Property(x => x.ReplyTo).HasColumnName("email_reply_to").HasMaxLength(EmailDeliverySettings.EmailMaxLength);
            e.Property(x => x.ProtectedApiToken).HasColumnName("email_protected_api_token").HasMaxLength(EmailDeliverySettings.ProtectedTokenMaxLength);
            e.Property(x => x.TokenLast4).HasColumnName("email_token_last4").HasMaxLength(4);
            e.Property(x => x.UpdatedAt).HasColumnName("email_updated_at");
            e.Ignore(x => x.HasToken);
            e.Ignore(x => x.IsSaved);
        });
        b.Navigation(s => s.Email).IsRequired();
        b.HasOne<StoredFile>().WithMany().HasForeignKey(s => s.LogoLightFileId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<StoredFile>().WithMany().HasForeignKey(s => s.LogoDarkFileId).OnDelete(DeleteBehavior.Restrict);

        // The single row is seeded by raw SQL in the Organization_Settings migration (values = OrgSettings.Defaults()).
    }
}

internal sealed class PaymentAccountConfiguration : IEntityTypeConfiguration<PaymentAccount>
{
    public void Configure(EntityTypeBuilder<PaymentAccount> b)
    {
        b.ToTable("payment_accounts", t =>
        {
            t.HasCheckConstraint("ck_payment_accounts_kind", CheckConstraints.In("kind", Enum.GetNames<PaymentAccountKind>()));
            t.HasCheckConstraint("ck_payment_accounts_bank_name", "kind <> 'Bank' OR bank_name IS NOT NULL");
            t.HasCheckConstraint("ck_payment_accounts_number", "kind = 'Cash' OR account_number IS NOT NULL");
            t.HasCheckConstraint("ck_payment_accounts_default_active", "NOT is_default OR is_active");
            t.HasCheckConstraint("ck_payment_accounts_deactivated",
                "(is_active AND deactivated_at IS NULL AND deactivation_reason IS NULL) OR " +
                "(NOT is_active AND deactivated_at IS NOT NULL AND deactivation_reason IS NOT NULL)");
            t.HasCheckConstraint("ck_payment_accounts_opening_balance", "round(opening_balance, 2) = opening_balance");
        });
        b.HasKey(a => a.Id);
        b.Property(a => a.Id).ValueGeneratedNever();
        b.Property(a => a.Kind).HasConversion<string>().HasMaxLength(16);
        b.Property(a => a.Label).HasMaxLength(PaymentAccount.LabelMaxLength).IsRequired();
        b.Property(a => a.BankName).HasMaxLength(PaymentAccount.BankNameMaxLength);
        b.Property(a => a.AccountName).HasMaxLength(PaymentAccount.AccountNameMaxLength).IsRequired();
        b.Property(a => a.AccountNumber).HasMaxLength(PaymentAccount.AccountNumberMaxLength);
        b.Property(a => a.DeactivationReason).HasMaxLength(PaymentAccount.ReasonMaxLength);
        b.Property(a => a.IsReconcilable).HasDefaultValue(true);
        b.Ignore(a => a.AccountNumberMasked);
        b.HasIndex(a => a.IsActive);
        // At most one default account.
        b.HasIndex(a => a.IsDefault).IsUnique().HasDatabaseName("ux_payment_accounts_single_default").HasFilter("is_default");
    }
}

internal sealed class StoredFileConfiguration : IEntityTypeConfiguration<StoredFile>
{
    public void Configure(EntityTypeBuilder<StoredFile> b)
    {
        b.ToTable("stored_files", t =>
        {
            // Per-purpose limits are enforced by StoredFile.Create; the database keeps a hard ceiling.
            t.HasCheckConstraint("ck_stored_files_size", $"size_bytes > 0 AND size_bytes <= {StoredFile.AbsoluteMaxBytes}");
            t.HasCheckConstraint("ck_stored_files_sha256", "sha256 ~ '^[0-9a-f]{64}$'");
        });
        b.HasKey(f => f.Id);
        b.Property(f => f.Id).ValueGeneratedNever();
        b.Property(f => f.ContentType).HasMaxLength(StoredFile.ContentTypeMaxLength).IsRequired();
        b.Property(f => f.Sha256).HasMaxLength(64).IsFixedLength().IsRequired();
        b.Property(f => f.Purpose).HasMaxLength(StoredFile.PurposeMaxLength).IsRequired();
        b.Property(f => f.OriginalName).HasMaxLength(StoredFile.FileNameMaxLength);
        b.HasIndex(f => f.Purpose);
    }
}
