using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.Infrastructure.Persistence.Generated;

public partial class AipmsDbContext
{
    public DbSet<MeetingTranscriptSegment> MeetingTranscriptSegments => Set<MeetingTranscriptSegment>();
    public DbSet<MeetingAiInsight> MeetingAiInsights => Set<MeetingAiInsight>();

    private static void ConfigureMeetingVideoAi(ModelBuilder builder)
    {
        builder.Entity<MeetingTranscriptSegment>(e =>
        {
            e.ToTable("meeting_transcript_segments", t =>
            {
                t.HasCheckConstraint("ck_meeting_transcript_segments_content", "LEN(LTRIM(RTRIM([content]))) > 0");
                t.HasCheckConstraint("ck_meeting_transcript_segments_speaker_name", "LEN(LTRIM(RTRIM([speaker_name]))) > 0");
                t.HasCheckConstraint("ck_meeting_transcript_segments_source", "[source] IN ('WEB_SPEECH','AUDIO_STT','MANUAL_NOTE')");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.MeetingId).HasColumnName("meeting_id");
            e.Property(x => x.MeetingVideoSessionId).HasColumnName("meeting_video_session_id");
            e.Property(x => x.SpeakerUserId).HasColumnName("speaker_user_id");
            e.Property(x => x.SpeakerName).HasColumnName("speaker_name").HasMaxLength(200);
            e.Property(x => x.Language).HasColumnName("language").HasMaxLength(20).IsUnicode(false).HasDefaultValue("vi-VN");
            e.Property(x => x.Source).HasColumnName("source").HasMaxLength(24).IsUnicode(false).HasDefaultValue("WEB_SPEECH");
            e.Property(x => x.Content).HasColumnName("content").HasMaxLength(2000);
            e.Property(x => x.IsHighlighted).HasColumnName("is_highlighted");
            e.Property(x => x.ClientSegmentId).HasColumnName("client_segment_id").HasMaxLength(64).IsUnicode(false);
            e.Property(x => x.SpokenAt).HasColumnName("spoken_at").HasPrecision(7);
            e.Property(x => x.CreatedBy).HasColumnName("created_by");
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasPrecision(7);

            e.HasIndex(x => new { x.MeetingId, x.SpokenAt, x.Id }).HasDatabaseName("ix_meeting_transcript_segments_meeting_spoken");
            e.HasIndex(x => new { x.MeetingId, x.ClientSegmentId })
                .HasFilter("[client_segment_id] IS NOT NULL")
                .IsUnique()
                .HasDatabaseName("uq_meeting_transcript_segments_client_id");

            e.HasOne(x => x.Meeting).WithMany().HasForeignKey(x => x.MeetingId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<M.MeetingVideoSession>().WithMany().HasForeignKey(x => x.MeetingVideoSessionId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<M.User>().WithMany().HasForeignKey(x => x.SpeakerUserId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<M.User>().WithMany().HasForeignKey(x => x.CreatedBy).OnDelete(DeleteBehavior.NoAction);
        });

        builder.Entity<MeetingAiInsight>(e =>
        {
            e.ToTable("meeting_ai_insights", t =>
            {
                t.HasCheckConstraint("ck_meeting_ai_insights_type", "[insight_type] IN ('LIVE_PULSE','FULL_MINUTES','COACH_CHECK')");
                t.HasCheckConstraint("ck_meeting_ai_insights_status", "[status] IN ('COMPLETED','FALLBACK')");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.MeetingId).HasColumnName("meeting_id");
            e.Property(x => x.MeetingVideoSessionId).HasColumnName("meeting_video_session_id");
            e.Property(x => x.InsightType).HasColumnName("insight_type").HasMaxLength(32).IsUnicode(false);
            e.Property(x => x.Status).HasColumnName("status").HasMaxLength(20).IsUnicode(false);
            e.Property(x => x.ModelVersion).HasColumnName("model_version").HasMaxLength(120);
            e.Property(x => x.TranscriptSegmentCount).HasColumnName("transcript_segment_count");
            e.Property(x => x.LastSegmentId).HasColumnName("last_segment_id");
            e.Property(x => x.SummaryText).HasColumnName("summary_text");
            e.Property(x => x.PayloadJson).HasColumnName("payload_json");
            e.Property(x => x.CreatedBy).HasColumnName("created_by");
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasPrecision(7);

            e.HasIndex(x => new { x.MeetingId, x.CreatedAt, x.Id }).HasDatabaseName("ix_meeting_ai_insights_meeting_created");
            e.HasOne(x => x.Meeting).WithMany().HasForeignKey(x => x.MeetingId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<M.MeetingVideoSession>().WithMany().HasForeignKey(x => x.MeetingVideoSessionId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<M.User>().WithMany().HasForeignKey(x => x.CreatedBy).OnDelete(DeleteBehavior.NoAction);
        });
    }
}
