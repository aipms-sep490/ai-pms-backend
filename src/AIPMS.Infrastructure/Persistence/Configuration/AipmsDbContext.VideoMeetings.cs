using AIPMS.Infrastructure.Persistence.Generated.Models;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Persistence.Generated;

public partial class AipmsDbContext
{
    public DbSet<MeetingVideoSession> MeetingVideoSessions => Set<MeetingVideoSession>();
    public DbSet<MeetingVideoParticipantBinding> MeetingVideoParticipantBindings => Set<MeetingVideoParticipantBinding>();
    public DbSet<MeetingVideoPresenceSession> MeetingVideoPresenceSessions => Set<MeetingVideoPresenceSession>();
    public DbSet<VideoProviderEvent> VideoProviderEvents => Set<VideoProviderEvent>();
    public DbSet<VideoProviderCleanupJob> VideoProviderCleanupJobs => Set<VideoProviderCleanupJob>();

    private static void ConfigureVideoMeetings(ModelBuilder b)
    {
        b.Entity<Meeting>().Property(x => x.MeetingDeliveryMode).HasColumnName("meeting_delivery_mode").HasMaxLength(20).IsUnicode(false).HasDefaultValue("ONSITE");
        b.Entity<Meeting>().Property(x => x.VideoChannel).HasColumnName("video_channel").HasMaxLength(30).IsUnicode(false).HasDefaultValue("NONE");
        b.Entity<MeetingVideoSession>(e =>
        {
            e.ToTable("meeting_video_sessions"); e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id"); e.Property(x => x.MeetingId).HasColumnName("meeting_id"); e.Property(x => x.Provider).HasColumnName("provider").HasMaxLength(30).IsUnicode(false); e.Property(x => x.ProviderRoomKey).HasColumnName("provider_room_key").HasMaxLength(255).IsUnicode(false); e.Property(x => x.Status).HasColumnName("status").HasMaxLength(20).IsUnicode(false); e.Property(x => x.StartedBy).HasColumnName("started_by"); e.Property(x => x.StartedAt).HasColumnName("started_at"); e.Property(x => x.EndedAt).HasColumnName("ended_at"); e.Property(x => x.FailureCode).HasColumnName("failure_code").HasMaxLength(80).IsUnicode(false); e.Property(x => x.ConcurrencyToken).HasColumnName("concurrency_token").HasDefaultValueSql("NEWSEQUENTIALID()").IsConcurrencyToken(); e.Property(x => x.CreatedAt).HasColumnName("created_at"); e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            e.HasIndex(x => x.ProviderRoomKey).IsUnique(); e.HasIndex(x => new { x.MeetingId, x.Status }).HasDatabaseName("ix_meeting_video_sessions_meeting_status"); e.HasIndex(x => x.MeetingId).HasFilter("[status] IN ('CREATED','LIVE')").IsUnique().HasDatabaseName("ux_meeting_video_sessions_one_active"); e.HasOne(x => x.Meeting).WithMany(x => x.MeetingVideoSessions).HasForeignKey(x => x.MeetingId).OnDelete(DeleteBehavior.Cascade); e.HasOne<User>().WithMany().HasForeignKey(x => x.StartedBy).OnDelete(DeleteBehavior.NoAction);
        });
        b.Entity<MeetingVideoParticipantBinding>(e =>
        {
            e.ToTable("meeting_video_participant_bindings"); e.HasKey(x => x.Id); e.Property(x => x.Id).HasColumnName("id"); e.Property(x => x.MeetingVideoSessionId).HasColumnName("meeting_video_session_id"); e.Property(x => x.UserId).HasColumnName("user_id"); e.Property(x => x.ProviderParticipantIdentity).HasColumnName("provider_participant_identity").HasMaxLength(255).IsUnicode(false); e.Property(x => x.CreatedAt).HasColumnName("created_at"); e.HasIndex(x => new { x.MeetingVideoSessionId, x.UserId }).IsUnique(); e.HasIndex(x => x.ProviderParticipantIdentity).IsUnique(); e.HasOne(x => x.MeetingVideoSession).WithMany(x => x.ParticipantBindings).HasForeignKey(x => x.MeetingVideoSessionId).OnDelete(DeleteBehavior.Cascade); e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.NoAction);
        });
        b.Entity<MeetingVideoPresenceSession>(e =>
        {
            e.ToTable("meeting_video_presence_sessions"); e.HasKey(x => x.Id); e.Property(x => x.Id).HasColumnName("id"); e.Property(x => x.MeetingVideoSessionId).HasColumnName("meeting_video_session_id"); e.Property(x => x.UserId).HasColumnName("user_id"); e.Property(x => x.ProviderParticipantIdentity).HasColumnName("provider_participant_identity").HasMaxLength(255).IsUnicode(false); e.Property(x => x.ProviderConnectionId).HasColumnName("provider_connection_id").HasMaxLength(255).IsUnicode(false); e.Property(x => x.JoinedAt).HasColumnName("joined_at"); e.Property(x => x.LeftAt).HasColumnName("left_at"); e.Property(x => x.DisconnectReason).HasColumnName("disconnect_reason").HasMaxLength(255); e.Property(x => x.CreatedAt).HasColumnName("created_at"); e.Property(x => x.UpdatedAt).HasColumnName("updated_at"); e.HasIndex(x => new { x.MeetingVideoSessionId, x.UserId, x.JoinedAt }); e.HasOne(x => x.MeetingVideoSession).WithMany(x => x.PresenceSessions).HasForeignKey(x => x.MeetingVideoSessionId).OnDelete(DeleteBehavior.Cascade); e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.NoAction);
        });
        b.Entity<VideoProviderEvent>(e =>
        {
            e.ToTable("video_provider_events"); e.HasKey(x => x.Id); e.Property(x => x.Id).HasColumnName("id"); e.Property(x => x.Provider).HasColumnName("provider").HasMaxLength(30).IsUnicode(false); e.Property(x => x.ProviderEventId).HasColumnName("provider_event_id").HasMaxLength(255).IsUnicode(false); e.Property(x => x.EventType).HasColumnName("event_type").HasMaxLength(80).IsUnicode(false); e.Property(x => x.MeetingVideoSessionId).HasColumnName("meeting_video_session_id"); e.Property(x => x.ReceivedAt).HasColumnName("received_at"); e.Property(x => x.ProcessedAt).HasColumnName("processed_at"); e.Property(x => x.PayloadHash).HasColumnName("payload_hash").HasMaxLength(64).IsFixedLength(); e.Property(x => x.ProcessingStatus).HasColumnName("processing_status").HasMaxLength(20).IsUnicode(false); e.Property(x => x.ErrorCode).HasColumnName("error_code").HasMaxLength(80).IsUnicode(false); e.HasIndex(x => new { x.Provider, x.ProviderEventId }).IsUnique(); e.HasOne(x => x.MeetingVideoSession).WithMany().HasForeignKey(x => x.MeetingVideoSessionId).OnDelete(DeleteBehavior.NoAction);
        });
        b.Entity<VideoProviderCleanupJob>(e =>
        {
            e.ToTable("video_provider_cleanup_jobs"); e.HasKey(x => x.Id); e.Property(x => x.Id).HasColumnName("id"); e.Property(x => x.MeetingVideoSessionId).HasColumnName("meeting_video_session_id"); e.Property(x => x.ProviderRoomKey).HasColumnName("provider_room_key").HasMaxLength(255).IsUnicode(false); e.Property(x => x.Status).HasColumnName("status").HasMaxLength(20).IsUnicode(false); e.Property(x => x.AttemptCount).HasColumnName("attempt_count"); e.Property(x => x.NextAttemptAt).HasColumnName("next_attempt_at"); e.Property(x => x.LeaseToken).HasColumnName("lease_token"); e.Property(x => x.LeaseUntil).HasColumnName("lease_until"); e.Property(x => x.LastErrorCode).HasColumnName("last_error_code").HasMaxLength(80).IsUnicode(false); e.Property(x => x.CreatedAt).HasColumnName("created_at"); e.Property(x => x.CompletedAt).HasColumnName("completed_at"); e.HasIndex(x => new { x.Status, x.NextAttemptAt, x.LeaseUntil }).HasDatabaseName("ix_video_cleanup_claim"); e.HasOne(x => x.MeetingVideoSession).WithMany().HasForeignKey(x => x.MeetingVideoSessionId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}


