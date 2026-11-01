using Htn.Domain.Events;
using Htn.Domain.Speakers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Htn.Infrastructure.Data.Configurations;

internal sealed class SpeakerTravelConfiguration : IEntityTypeConfiguration<SpeakerTravel>
{
    public void Configure(EntityTypeBuilder<SpeakerTravel> b)
    {
        b.ToTable("speaker_travel", t =>
        {
            t.HasCheckConstraint("ck_speaker_travel_dates",
                "arrival_date IS NULL OR departure_date IS NULL OR departure_date >= arrival_date");
            t.HasCheckConstraint("ck_speaker_travel_consent",
                "(submitted_at IS NULL) OR (consent_at IS NOT NULL AND consent_version IS NOT NULL)");
            // The purge is irreversible by construction: once set, the three sensitive fields must all be null.
            t.HasCheckConstraint("ck_speaker_travel_purged",
                "purged_at IS NULL OR (flying_from IS NULL AND preferred_arrival_time IS NULL AND id_full_name IS NULL)");
        });
        b.HasKey(x => x.SpeakerId);
        b.Property(x => x.FlyingFrom).HasMaxLength(SpeakerTravel.PlaceMaxLength);
        b.Property(x => x.PreferredArrivalTime).HasMaxLength(SpeakerTravel.TimeTextMaxLength);
        b.Property(x => x.IdFullName).HasMaxLength(SpeakerTravel.IdNameMaxLength);
        b.Property(x => x.BusFrom).HasMaxLength(SpeakerTravel.PlaceMaxLength);
        b.Property(x => x.BusEta).HasMaxLength(SpeakerTravel.TimeTextMaxLength);
        b.Property(x => x.RoomNote).HasMaxLength(SpeakerTravel.RoomNoteMaxLength);
        b.Property(x => x.NeedsNotes).HasMaxLength(SpeakerTravel.NeedsNotesMaxLength);
        b.Property(x => x.ConsentVersion).HasMaxLength(SpeakerTravel.ConsentVersionMaxLength);
        b.Ignore(x => x.IsPurged);
        b.Ignore(x => x.HasSubmitted);
        b.HasOne<Speaker>().WithOne().HasForeignKey<SpeakerTravel>(x => x.SpeakerId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Event>().WithMany().HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class SpeakerLinkConfiguration : IEntityTypeConfiguration<SpeakerLink>
{
    public const string TokenIndex = "ux_speaker_links_token";
    public const string OpenIndex = "ux_speaker_links_open";

    public void Configure(EntityTypeBuilder<SpeakerLink> b)
    {
        b.ToTable("speaker_links", t =>
        {
            t.HasCheckConstraint("ck_speaker_links_token", "token_hash ~ '^[0-9a-f]{64}$'");
            t.HasCheckConstraint("ck_speaker_links_expiry", "expires_at > created_at");
            t.HasCheckConstraint("ck_speaker_links_sent", "send_count >= 0 AND (sent_at IS NULL) = (send_count = 0)");
            t.HasCheckConstraint("ck_speaker_links_otp",
                "(otp_hash IS NULL) = (otp_expires_at IS NULL) AND otp_attempts BETWEEN 0 AND 5");
            t.HasCheckConstraint("ck_speaker_links_session",
                "(session_token_hash IS NULL) = (session_expires_at IS NULL)");
            t.HasCheckConstraint("ck_speaker_links_revoke",
                "(revoked_at IS NULL) = (revoke_reason IS NULL) AND (revoked_at IS NULL) = (revoked_by_id IS NULL)");
            t.HasCheckConstraint("ck_speaker_links_submitted", "last_submitted_at IS NULL OR submitted_at IS NOT NULL");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.TokenHash).HasMaxLength(SpeakerLink.TokenHashLength).IsFixedLength().IsRequired();
        b.Property(x => x.RevokeReason).HasMaxLength(SpeakerLink.ReasonMaxLength);
        b.Property(x => x.OtpHash).HasMaxLength(SpeakerLink.TokenHashLength).IsFixedLength();
        b.Property(x => x.SessionTokenHash).HasMaxLength(SpeakerLink.TokenHashLength).IsFixedLength();
        b.Property<uint>(EventConfiguration.RowVersion).HasColumnName("xmin").HasColumnType("xid").IsRowVersion();
        b.Ignore(x => x.IsRevoked);
        b.Ignore(x => x.HasSubmitted);
        b.HasIndex(x => x.TokenHash).IsUnique().HasDatabaseName(TokenIndex);
        // One live link per speaker. Deliberately NOT filtered on submitted_at: submitting must not close the link.
        b.HasIndex(x => x.SpeakerId).IsUnique().HasDatabaseName(OpenIndex).HasFilter("revoked_at IS NULL");
        b.HasIndex(x => new { x.EventId, x.SpeakerId }).HasDatabaseName("ix_speaker_links_event");
        b.HasOne<Speaker>().WithMany().HasForeignKey(x => x.SpeakerId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Event>().WithMany().HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class SpeakerLinkAccessConfiguration : IEntityTypeConfiguration<SpeakerLinkAccess>
{
    public void Configure(EntityTypeBuilder<SpeakerLinkAccess> b)
    {
        b.ToTable("speaker_link_access", t =>
            t.HasCheckConstraint("ck_speaker_link_access_kind", CheckConstraints.In("kind", Enum.GetNames<SpeakerLinkAccessKind>())));
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.ClientHint).HasMaxLength(SpeakerLinkAccess.ClientHintMaxLength);
        b.Property(x => x.IpHint).HasMaxLength(SpeakerLinkAccess.IpHintMaxLength);
        b.Property(x => x.Note).HasMaxLength(SpeakerLinkAccess.NoteMaxLength);
        b.HasIndex(x => new { x.LinkId, x.At }).HasDatabaseName("ix_speaker_link_access_link");
        b.HasOne<SpeakerLink>().WithMany().HasForeignKey(x => x.LinkId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class SpeakerPlusOneConfiguration : IEntityTypeConfiguration<SpeakerPlusOne>
{
    public const string ActiveIndex = "ux_speaker_plus_ones_active";

    public void Configure(EntityTypeBuilder<SpeakerPlusOne> b)
    {
        b.ToTable("speaker_plus_ones", t =>
            t.HasCheckConstraint("ck_speaker_plus_ones_removed", "(removed_at IS NULL) = (remove_reason IS NULL)"));
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Name).HasMaxLength(SpeakerPlusOne.NameMaxLength).IsRequired();
        b.Property(x => x.NameSegment).HasMaxLength(SpeakerPlusOne.NameSegmentMaxLength).IsRequired();
        b.Property(x => x.Email).HasMaxLength(SpeakerPlusOne.EmailMaxLength).IsRequired();
        b.Property(x => x.RemoveReason).HasMaxLength(SpeakerPlusOne.ReasonMaxLength);
        b.Ignore(x => x.IsRemoved);
        b.HasIndex(x => x.SpeakerId).IsUnique().HasDatabaseName(ActiveIndex).HasFilter("removed_at IS NULL");
        b.HasOne<Speaker>().WithMany().HasForeignKey(x => x.SpeakerId).OnDelete(DeleteBehavior.Restrict);
    }
}
