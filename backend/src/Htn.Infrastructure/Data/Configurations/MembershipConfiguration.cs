using Htn.Domain.Events;
using Htn.Domain.Organization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Htn.Infrastructure.Data.Configurations;

/// <summary>
/// Phase 8B: one row per <c>(PersonId, MembershipYear)</c>, fully append-only (no <c>xmin</c> needed — there is no
/// update path at all). Enforcement against a hard delete or any update lives in the <c>Membership_Guards</c>
/// migration's unconditional reject-all trigger, mirroring <c>merch_sales_guard</c>.
/// </summary>
internal sealed class MembershipConfiguration : IEntityTypeConfiguration<Membership>
{
    public const string PersonYearIndex = "ux_memberships_person_year";

    public void Configure(EntityTypeBuilder<Membership> b)
    {
        b.ToTable("memberships", t =>
        {
            t.HasCheckConstraint("ck_memberships_year",
                $"membership_year BETWEEN {Event.MinMembershipYear} AND {Event.MaxMembershipYear}");
            t.HasCheckConstraint("ck_memberships_fee", "fee_paid >= 0");
            t.HasCheckConstraint("ck_memberships_paid_via", CheckConstraints.In("paid_via", Enum.GetNames<MembershipPaidVia>()));
            t.HasCheckConstraint("ck_memberships_valid_until", "valid_until > paid_at");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.PaidVia).HasConversion<string>().HasMaxLength(20);
        // One live row per person per membership year, forever — a renewal for the next year is a new row.
        b.HasIndex(x => new { x.PersonId, x.MembershipYear }).IsUnique().HasDatabaseName(PersonYearIndex);
        b.HasOne<Person>().WithMany().HasForeignKey(x => x.PersonId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Event>().WithMany().HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Restrict);
    }
}
