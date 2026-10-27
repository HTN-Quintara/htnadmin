using Htn.Domain.Agenda;
using Htn.Domain.Committees;
using Htn.Domain.Events;
using Htn.Domain.Speakers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Htn.Infrastructure.Data.Configurations;

internal sealed class EventRoomConfiguration : IEntityTypeConfiguration<EventRoom>
{
    public const string EventNameIndex = "ux_event_rooms_event_name";

    public void Configure(EntityTypeBuilder<EventRoom> b)
    {
        b.ToTable("event_rooms", t =>
        {
            t.HasCheckConstraint("ck_event_rooms_color", "color_hex ~ '^#[0-9A-F]{6}$'");
            t.HasCheckConstraint("ck_event_rooms_deactivated", "is_active = (deactivated_at IS NULL)");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Name).HasMaxLength(EventRoom.NameMaxLength).IsRequired();
        b.Property(x => x.ColorHex).HasMaxLength(7).IsFixedLength().IsRequired();
        b.HasIndex("EventId", "Name").IsUnique().HasDatabaseName(EventNameIndex).HasFilter("deactivated_at IS NULL");
        b.HasIndex(x => new { x.EventId, x.SortOrder }).HasDatabaseName("ix_event_rooms_event");
        b.HasOne<Event>().WithMany().HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class SessionConfiguration : IEntityTypeConfiguration<Session>
{
    public void Configure(EntityTypeBuilder<Session> b)
    {
        b.ToTable("sessions", t =>
        {
            t.HasCheckConstraint("ck_sessions_kind", CheckConstraints.In("kind", Enum.GetNames<SessionKind>()));
            t.HasCheckConstraint("ck_sessions_duration",
                $"duration_minutes BETWEEN {Session.MinDuration} AND {Session.MaxDuration}");
            t.HasCheckConstraint("ck_sessions_title", "char_length(title) > 0");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Kind).HasConversion<string>().HasMaxLength(16);
        b.Property(x => x.Title).HasMaxLength(Session.TitleMaxLength).IsRequired();
        b.Property(x => x.Notes).HasMaxLength(Session.NotesMaxLength);
        b.Property<uint>(EventConfiguration.RowVersion).HasColumnName("xmin").HasColumnType("xid").IsRowVersion();
        b.Ignore(x => x.EndsAt);
        // The whole timeline read: a day's sessions in start order.
        b.HasIndex(x => new { x.EventId, x.StartsAt }).HasDatabaseName("ix_sessions_event_start");
        b.HasOne<Event>().WithMany().HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Committee>().WithMany().HasForeignKey(x => x.OwnerCommitteeId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class SessionRoomConfiguration : IEntityTypeConfiguration<SessionRoom>
{
    public void Configure(EntityTypeBuilder<SessionRoom> b)
    {
        b.ToTable("session_rooms");
        b.HasKey(x => new { x.SessionId, x.RoomId });
        b.HasIndex(x => new { x.RoomId, x.SessionId }).HasDatabaseName("ix_session_rooms_room");
        b.HasOne<Session>().WithMany().HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<EventRoom>().WithMany().HasForeignKey(x => x.RoomId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class SessionSpeakerConfiguration : IEntityTypeConfiguration<SessionSpeaker>
{
    public void Configure(EntityTypeBuilder<SessionSpeaker> b)
    {
        b.ToTable("session_speakers", t =>
            t.HasCheckConstraint("ck_session_speakers_role", CheckConstraints.In("role", Enum.GetNames<SessionSpeakerRole>())));
        b.HasKey(x => new { x.SessionId, x.SpeakerId });
        b.Property(x => x.Role).HasConversion<string>().HasMaxLength(16);
        b.HasIndex(x => x.SpeakerId).HasDatabaseName("ix_session_speakers_speaker");
        b.HasOne<Session>().WithMany().HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<Speaker>().WithMany().HasForeignKey(x => x.SpeakerId).OnDelete(DeleteBehavior.Restrict);
    }
}
