using Htn.Domain.Directory;
using Htn.Domain.Events;
using Htn.Domain.Invitations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Htn.Infrastructure.Data.Configurations;

internal sealed class EventInvitationConfiguration : IEntityTypeConfiguration<EventInvitation>
{
    public const string EventOrgIndex = "ux_event_invitations_event_org";

    public void Configure(EntityTypeBuilder<EventInvitation> b)
    {
        b.ToTable("event_invitations", t =>
        {
            t.HasCheckConstraint("ck_event_invitations_status", CheckConstraints.In("status", Enum.GetNames<InvitationStatus>()));
            // Sent or later carries the snapshot and who/when; NotSent carries none of it.
            t.HasCheckConstraint("ck_event_invitations_sent",
                "(status = 'NotSent') = (sent_at IS NULL) AND (status = 'NotSent') = (sent_by_id IS NULL) " +
                "AND (status = 'NotSent') = (contact_id IS NULL) AND (status = 'NotSent') = (letter_subject_snapshot IS NULL) " +
                "AND (status = 'NotSent') = (letter_body_snapshot IS NULL) AND (status = 'NotSent') = (recipient_email IS NULL) " +
                "AND (status = 'NotSent') = (recipient_name IS NULL)");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
        b.Property(x => x.LetterSubjectSnapshot).HasMaxLength(EventInvitation.SubjectMaxLength);
        b.Property(x => x.LetterBodySnapshot).HasMaxLength(EventInvitation.BodyMaxLength);
        b.Property(x => x.RecipientEmail).HasMaxLength(DirectoryContact.EmailMaxLength);
        b.Property(x => x.RecipientName).HasMaxLength(DirectoryContact.HolderNameMaxLength);
        b.Property<uint>(EventConfiguration.RowVersion).HasColumnName("xmin").HasColumnType("xid").IsRowVersion();
        b.Ignore(x => x.IsNotSent);
        b.HasIndex(x => new { x.EventId, x.DirectoryOrgId }).IsUnique().HasDatabaseName(EventOrgIndex);
        b.HasIndex(x => x.EventId).HasDatabaseName("ix_event_invitations_event");
        b.HasOne<Event>().WithMany().HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<DirectoryOrg>().WithMany().HasForeignKey(x => x.DirectoryOrgId).OnDelete(DeleteBehavior.Restrict);
        // ContactId is a point-in-time snapshot of who was emailed, not a live FK the contact must keep satisfying
        // (the contact can later be voided without breaking this row) — so no foreign key constraint here.
    }
}

internal sealed class InvitationEventConfiguration : IEntityTypeConfiguration<InvitationEvent>
{
    public void Configure(EntityTypeBuilder<InvitationEvent> b)
    {
        b.ToTable("invitation_events", t =>
            t.HasCheckConstraint("ck_invitation_events_kind", CheckConstraints.In("kind", Enum.GetNames<InvitationStatus>())));
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Kind).HasConversion<string>().HasMaxLength(16);
        b.Property(x => x.Note).HasMaxLength(InvitationEvent.NoteMaxLength);
        b.HasIndex(x => new { x.InvitationId, x.At }).HasDatabaseName("ix_invitation_events_invitation");
        b.HasOne<EventInvitation>().WithMany().HasForeignKey(x => x.InvitationId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class EventInvitationTemplateConfiguration : IEntityTypeConfiguration<EventInvitationTemplate>
{
    public const string EventIndex = "ux_event_invitation_templates_event";

    public void Configure(EntityTypeBuilder<EventInvitationTemplate> b)
    {
        b.ToTable("event_invitation_templates");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Subject).HasMaxLength(EventInvitationTemplate.SubjectMaxLength).IsRequired();
        b.Property(x => x.Body).HasMaxLength(EventInvitationTemplate.BodyMaxLength).IsRequired();
        // One template per event — a mutable settings row, like EventPartnerSettings; no append-only guard.
        b.HasIndex(x => x.EventId).IsUnique().HasDatabaseName(EventIndex);
        b.HasOne<Event>().WithMany().HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Restrict);
    }
}
