using Htn.Domain.Events;
using Htn.Domain.Merch;
using Htn.Domain.Organization;
using Htn.Domain.Registrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Htn.Infrastructure.Data.Configurations;

/// <summary>
/// Phase 8A: the event-scoped catalog (<see cref="MerchItem"/>/<see cref="MerchVariant"/>), one POS transaction
/// (<see cref="MerchSale"/>/<see cref="MerchSaleLine"/>) and the per-event discount settings
/// (<see cref="EventMerchSettings"/>) — mirroring <c>SuppliersConfiguration</c>'s flat, all-in-one-file shape.
/// Append-only/freeze/stock-decrement enforcement lives in the <c>Merch_Guards</c> migration.
/// </summary>
internal sealed class MerchItemConfiguration : IEntityTypeConfiguration<MerchItem>
{
    public void Configure(EntityTypeBuilder<MerchItem> b)
    {
        b.ToTable("merch_items", t =>
        {
            t.HasCheckConstraint("ck_merch_items_price", "price >= 0");
            t.HasCheckConstraint("ck_merch_items_swatch", "swatch_color IS NULL OR swatch_color ~ '^#[0-9A-Fa-f]{6}$'");
            t.HasCheckConstraint("ck_merch_items_deactivated",
                "(deactivated_at IS NULL) = (deactivate_reason IS NULL) AND (deactivated_at IS NULL) = (deactivated_by_id IS NULL)");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Name).HasMaxLength(MerchItem.NameMaxLength).IsRequired();
        b.Property(x => x.SwatchColor).HasMaxLength(7).IsFixedLength();
        b.Property(x => x.DeactivateReason).HasMaxLength(MerchItem.ReasonMaxLength);
        b.Property<uint>(EventConfiguration.RowVersion).HasColumnName("xmin").HasColumnType("xid").IsRowVersion();
        b.Ignore(x => x.IsActive);
        b.HasIndex(x => x.EventId).HasDatabaseName("ix_merch_items_event");
        b.HasOne<Event>().WithMany().HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class MerchVariantConfiguration : IEntityTypeConfiguration<MerchVariant>
{
    public const string ItemLabelIndex = "ux_merch_variants_item_label";

    public void Configure(EntityTypeBuilder<MerchVariant> b)
    {
        b.ToTable("merch_variants", t =>
        {
            t.HasCheckConstraint("ck_merch_variants_stock", $"stock_on_hand BETWEEN 0 AND {MerchVariant.MaxStock}");
            t.HasCheckConstraint("ck_merch_variants_deactivated",
                "(deactivated_at IS NULL) = (deactivate_reason IS NULL) AND (deactivated_at IS NULL) = (deactivated_by_id IS NULL)");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Label).HasMaxLength(MerchVariant.LabelMaxLength).IsRequired();
        b.Property(x => x.DeactivateReason).HasMaxLength(MerchVariant.ReasonMaxLength);
        b.Property<uint>(EventConfiguration.RowVersion).HasColumnName("xmin").HasColumnType("xid").IsRowVersion();
        b.Ignore(x => x.IsActive);
        // One live label per item ("M" can be reused once the old variant for it is deactivated).
        b.HasIndex(x => new { x.ItemId, x.Label }).IsUnique().HasDatabaseName(ItemLabelIndex).HasFilter("deactivated_at IS NULL");
        b.HasOne<MerchItem>().WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class EventMerchSettingsConfiguration : IEntityTypeConfiguration<EventMerchSettings>
{
    public const string EventIndex = "ux_event_merch_settings_event";

    public void Configure(EntityTypeBuilder<EventMerchSettings> b)
    {
        b.ToTable("event_merch_settings", t =>
            t.HasCheckConstraint("ck_event_merch_settings_pct",
                "goon_pct BETWEEN 0 AND 100 AND apprentice_pct BETWEEN 0 AND 100 " +
                "AND member_pct BETWEEN 0 AND 100 AND board_pct BETWEEN 0 AND 100"));
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property<uint>(EventConfiguration.RowVersion).HasColumnName("xmin").HasColumnType("xid").IsRowVersion();
        b.HasIndex(x => x.EventId).IsUnique().HasDatabaseName(EventIndex);
        b.HasOne<Event>().WithMany().HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class MerchSaleConfiguration : IEntityTypeConfiguration<MerchSale>
{
    public void Configure(EntityTypeBuilder<MerchSale> b)
    {
        b.ToTable("merch_sales", t =>
        {
            t.HasCheckConstraint("ck_merch_sales_method", CheckConstraints.In("method", Enum.GetNames<MerchSaleMethod>()));
            t.HasCheckConstraint("ck_merch_sales_level",
                CheckConstraints.In("buyer_level_snapshot", Enum.GetNames<PersonLevel>()) + " OR buyer_level_snapshot IS NULL");
            t.HasCheckConstraint("ck_merch_sales_discount", "discount_pct BETWEEN 0 AND 100");
            // Phase 8B: a membership fee (undiscounted, never a merch item) rides on top of the discounted merch
            // subtotal; the pair of columns is all-or-nothing and total accounts for both.
            t.HasCheckConstraint("ck_merch_sales_money",
                "subtotal >= 0 AND discount_amount >= 0 AND total >= 0 " +
                "AND total = subtotal - discount_amount + COALESCE(membership_fee_amount, 0) " +
                "AND (membership_fee_included = FALSE) = (membership_fee_amount IS NULL) " +
                "AND (membership_fee_amount IS NULL OR membership_fee_amount >= 0)");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.BuyerNameSnapshot).HasMaxLength(MerchSale.BuyerNameMaxLength).IsRequired();
        b.Property(x => x.BuyerLevelSnapshot).HasConversion<string?>().HasMaxLength(10);
        b.Property(x => x.Method).HasConversion<string>().HasMaxLength(8);
        b.Property(x => x.Station).HasMaxLength(MerchSale.StationMaxLength);
        b.HasIndex(x => x.EventId).HasDatabaseName("ix_merch_sales_event");
        b.HasIndex(x => x.SoldAt).HasDatabaseName("ix_merch_sales_sold_at");
        // The POS client's idempotency key: a retry of the same sale attempt can never create a second row for
        // the same event, but the same key can be reused across different events.
        b.HasIndex(x => new { x.EventId, x.ClientRequestId }).IsUnique().HasDatabaseName("ux_merch_sales_event_client_request_id");
        b.HasOne<Event>().WithMany().HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Person>().WithMany().HasForeignKey(x => x.BuyerPersonId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Attendee>().WithMany().HasForeignKey(x => x.BuyerAttendeeId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class MerchSaleLineConfiguration : IEntityTypeConfiguration<MerchSaleLine>
{
    public void Configure(EntityTypeBuilder<MerchSaleLine> b)
    {
        b.ToTable("merch_sale_lines", t =>
        {
            t.HasCheckConstraint("ck_merch_sale_lines_qty", "qty > 0");
            t.HasCheckConstraint("ck_merch_sale_lines_money",
                "unit_price_snapshot >= 0 AND line_total >= 0 AND line_total = round(unit_price_snapshot * qty, 2)");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.HasIndex(x => x.SaleId).HasDatabaseName("ix_merch_sale_lines_sale");
        b.HasIndex(x => x.VariantId).HasDatabaseName("ix_merch_sale_lines_variant");
        b.HasOne<MerchSale>().WithMany().HasForeignKey(x => x.SaleId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<MerchItem>().WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<MerchVariant>().WithMany().HasForeignKey(x => x.VariantId).OnDelete(DeleteBehavior.Restrict);
    }
}
