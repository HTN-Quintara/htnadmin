using Htn.Domain.Events;
using Htn.Domain.Files;
using Htn.Domain.Leads;
using Htn.Domain.Organization;
using Htn.Domain.Speakers;
using Htn.Domain.Sponsors;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Htn.Infrastructure.Data.Configurations;

internal sealed class LeadConfiguration : IEntityTypeConfiguration<Lead>
{
    public const string BoardIndex = "ix_leads_board";
    public const string SponsorIndex = "ux_leads_sponsor";
    public const string SpeakerIndex = "ux_leads_speaker";

    public void Configure(EntityTypeBuilder<Lead> b)
    {
        b.ToTable("leads", t =>
        {
            t.HasCheckConstraint("ck_leads_kind", CheckConstraints.In("kind", Enum.GetNames<LeadKind>()));
            t.HasCheckConstraint("ck_leads_status", CheckConstraints.In("status", Enum.GetNames<LeadStatus>()));
            t.HasCheckConstraint("ck_leads_source", CheckConstraints.In("source", Enum.GetNames<LeadSource>()));
            // A speaker lead carries no sponsor ask.
            t.HasCheckConstraint("ck_leads_ask", "kind = 'Sponsor' OR (ask_tier_id IS NULL AND ask_amount IS NULL)");
            t.HasCheckConstraint("ck_leads_amount", "ask_amount IS NULL OR (ask_amount > 0 AND round(ask_amount, 2) = ask_amount)");
            t.HasCheckConstraint("ck_leads_confirmed",
                "(status = 'Confirmed') = (confirmed_at IS NOT NULL) AND (confirmed_at IS NULL) = (confirmed_by_id IS NULL)");
            t.HasCheckConstraint("ck_leads_declined",
                "(status = 'Declined') = (declined_at IS NOT NULL) AND (declined_at IS NULL) = (decline_reason IS NULL) " +
                "AND (declined_at IS NULL) = (declined_by_id IS NULL)");
            // A sponsor link only ever sits on a confirmed sponsor lead; a speaker link on a confirmed speaker lead.
            t.HasCheckConstraint("ck_leads_link",
                "(sponsor_id IS NULL OR (kind = 'Sponsor' AND status = 'Confirmed')) " +
                "AND (speaker_id IS NULL OR (kind = 'Speaker' AND status = 'Confirmed'))");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Kind).HasConversion<string>().HasMaxLength(16);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
        b.Property(x => x.Source).HasConversion<string>().HasMaxLength(16);
        b.Property(x => x.Subject).HasMaxLength(Lead.SubjectMaxLength).IsRequired();
        b.Property(x => x.NameKey).HasMaxLength(Htn.Domain.Common.NameKey.MaxLength).IsRequired();
        b.Property(x => x.ContactName).HasMaxLength(Lead.ContactNameMaxLength);
        b.Property(x => x.ContactRole).HasMaxLength(Lead.ContactRoleMaxLength);
        b.Property(x => x.ContactEmail).HasMaxLength(Lead.EmailMaxLength);
        b.Property(x => x.ContactMobile).HasMaxLength(Lead.MobileMaxLength);
        b.Property(x => x.AskNote).HasMaxLength(Lead.AskNoteMaxLength);
        b.Property(x => x.NextStep).HasMaxLength(Lead.NextStepMaxLength);
        b.Property(x => x.DeclineReason).HasMaxLength(Lead.ReasonMaxLength);
        b.Property<uint>(EventConfiguration.RowVersion).HasColumnName("xmin").HasColumnType("xid").IsRowVersion();
        b.Ignore(x => x.IsConfirmed);
        b.Ignore(x => x.IsDeclined);
        b.Ignore(x => x.IsLinked);
        // The whole kanban in one index: columns, their counts and the overdue sort.
        b.HasIndex(x => new { x.EventId, x.Kind, x.Status, x.NextStepDue }).HasDatabaseName(BoardIndex);
        b.HasIndex(x => x.SponsorId).IsUnique().HasDatabaseName(SponsorIndex).HasFilter("sponsor_id IS NOT NULL");
        b.HasIndex(x => x.SpeakerId).IsUnique().HasDatabaseName(SpeakerIndex).HasFilter("speaker_id IS NOT NULL");
        b.HasIndex(x => x.OwnerPersonId).HasDatabaseName("ix_leads_owner");
        // The Phase 7 Directory backfill joins leads to organizations by name_key.
        b.HasIndex(x => x.DirectoryOrgId).HasDatabaseName("ix_leads_directory_org");
        b.HasOne<Event>().WithMany().HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<SponsorTier>().WithMany().HasForeignKey(x => x.AskTierId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Person>().WithMany().HasForeignKey(x => x.OwnerPersonId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Sponsor>().WithMany().HasForeignKey(x => x.SponsorId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Speaker>().WithMany().HasForeignKey(x => x.SpeakerId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Htn.Domain.Directory.DirectoryOrg>().WithMany().HasForeignKey(x => x.DirectoryOrgId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class SpeakerConfiguration : IEntityTypeConfiguration<Speaker>
{
    public const string EventEmailIndex = "ux_speakers_event_email";

    public void Configure(EntityTypeBuilder<Speaker> b)
    {
        b.ToTable("speakers", t =>
        {
            t.HasCheckConstraint("ck_speakers_status", CheckConstraints.In("status", Enum.GetNames<SpeakerStatus>()));
            t.HasCheckConstraint("ck_speakers_void",
                "(voided_at IS NULL) = (void_reason IS NULL) AND (voided_at IS NULL) = (voided_by_id IS NULL)");
            t.HasCheckConstraint("ck_speakers_photo",
                "(photo_approved_at IS NULL) = (photo_approved_by_id IS NULL) AND (photo_approved_at IS NULL OR photo_file_id IS NOT NULL)");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
        b.Property(x => x.PersonName).HasMaxLength(Speaker.NameMaxLength).IsRequired();
        b.Property(x => x.Email).HasMaxLength(Speaker.EmailMaxLength).IsRequired();
        b.Property(x => x.Affiliation).HasMaxLength(Speaker.AffiliationMaxLength);
        b.Property(x => x.VoidReason).HasMaxLength(Speaker.ReasonMaxLength);
        b.Property<uint>(EventConfiguration.RowVersion).HasColumnName("xmin").HasColumnType("xid").IsRowVersion();
        b.Ignore(x => x.IsConfirmed);
        b.Ignore(x => x.IsVoided);
        b.Ignore(x => x.PhotoApproved);
        // One live speaker per email per event.
        b.HasIndex("EventId", "Email").IsUnique().HasDatabaseName(EventEmailIndex).HasFilter("voided_at IS NULL");
        b.HasIndex(x => new { x.EventId, x.Status }).HasDatabaseName("ix_speakers_event_status");
        b.HasOne<Event>().WithMany().HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Lead>().WithMany().HasForeignKey(x => x.LeadId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<StoredFile>().WithMany().HasForeignKey(x => x.PhotoFileId).OnDelete(DeleteBehavior.Restrict);
    }
}
