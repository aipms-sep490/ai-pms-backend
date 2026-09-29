using AIPMS.Infrastructure.Persistence.Models;
using AIPMS.Infrastructure.Persistence.Generated.Models;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Persistence.Generated;

public partial class AipmsDbContext
{
    public DbSet<TeamMajorResponsibility> TeamMajorResponsibilities => Set<TeamMajorResponsibility>();
    public DbSet<TaskDiscipline> TaskDisciplines => Set<TaskDiscipline>();
    public DbSet<ProjectEvidence> ProjectEvidence => Set<ProjectEvidence>();

    private static void ConfigureDisciplineEvidence(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TeamMajorResponsibility>(e =>
        {
            e.ToTable("team_major_responsibilities"); e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id"); e.Property(x => x.TeamId).HasColumnName("team_id");
            e.Property(x => x.MajorId).HasColumnName("major_id"); e.Property(x => x.Content).HasColumnName("content").HasMaxLength(2000);
            e.Property(x => x.SortOrder).HasColumnName("sort_order"); e.Property(x => x.ConcurrencyToken).HasColumnName("concurrency_token").HasDefaultValueSql("(newid())").IsConcurrencyToken();
            e.Property(x => x.CreatedBy).HasColumnName("created_by"); e.Property(x => x.CreatedAt).HasColumnName("created_at").HasPrecision(7).HasDefaultValueSql("(sysutcdatetime())");
            e.HasIndex(x => new { x.TeamId, x.MajorId, x.SortOrder }, "uq_responsibility_order").IsUnique();
            e.HasOne<TeamMajorRequirement>().WithMany().HasForeignKey(x => new { x.TeamId, x.MajorId }).OnDelete(DeleteBehavior.Cascade).HasConstraintName("fk_responsibility_requirement");
            e.HasOne<User>().WithMany().HasForeignKey(x => x.CreatedBy).OnDelete(DeleteBehavior.NoAction).HasConstraintName("fk_responsibility_actor");
        });
        modelBuilder.Entity<TaskDiscipline>(e =>
        {
            e.ToTable("task_disciplines"); e.HasKey(x => new { x.TaskId, x.MajorId }).HasName("pk_task_disciplines");
            e.Property(x => x.TaskId).HasColumnName("task_id"); e.Property(x => x.MajorId).HasColumnName("major_id");
            e.Property(x => x.Role).HasColumnName("role").HasMaxLength(20).IsUnicode(false);
            e.Property(x => x.CreatedBy).HasColumnName("created_by"); e.Property(x => x.CreatedAt).HasColumnName("created_at").HasPrecision(7).HasDefaultValueSql("(sysutcdatetime())");
            e.HasIndex(x => new { x.MajorId, x.TaskId }, "ix_task_discipline_major"); e.HasIndex(x => x.TaskId, "uq_task_discipline_primary").IsUnique().HasFilter("[role] = 'PRIMARY'");
            e.HasOne<Models.Task>().WithMany().HasForeignKey(x => x.TaskId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Major>().WithMany().HasForeignKey(x => x.MajorId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.CreatedBy).OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<ProjectEvidence>(e =>
        {
            e.ToTable("project_evidence"); e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id"); e.Property(x => x.ProjectId).HasColumnName("project_id"); e.Property(x => x.MajorId).HasColumnName("major_id");
            e.Property(x => x.SourceType).HasColumnName("source_type").HasMaxLength(30).IsUnicode(false); e.Property(x => x.SourceId).HasColumnName("source_id");
            e.Property(x => x.TaskId).HasColumnName("task_id"); e.Property(x => x.DeliverableId).HasColumnName("deliverable_id"); e.Property(x => x.MeetingId).HasColumnName("meeting_id");
            e.Property(x => x.ProgressReportId).HasColumnName("progress_report_id"); e.Property(x => x.FileId).HasColumnName("file_id");
            e.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(2000); e.Property(x => x.VerificationStatus).HasColumnName("verification_status").HasMaxLength(20).IsUnicode(false).HasDefaultValue("PENDING");
            e.Property(x => x.SubmittedBy).HasColumnName("submitted_by"); e.Property(x => x.SubmittedAt).HasColumnName("submitted_at").HasPrecision(7).HasDefaultValueSql("(sysutcdatetime())");
            e.HasIndex(x => new { x.ProjectId, x.SubmittedAt, x.Id }, "ix_evidence_project_time").IsDescending(false, true, true); e.HasIndex(x => new { x.ProjectId, x.MajorId, x.VerificationStatus }, "ix_evidence_project_major");
            e.HasIndex(x => new { x.ProjectId, x.SourceType, x.SourceId, x.MajorId }, "uq_evidence_source").IsUnique().HasFilter(null);
            e.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<Major>().WithMany().HasForeignKey(x => x.MajorId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.SubmittedBy).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<Models.Task>().WithMany().HasForeignKey(x => x.TaskId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<Deliverable>().WithMany().HasForeignKey(x => x.DeliverableId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<Meeting>().WithMany().HasForeignKey(x => x.MeetingId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<ProgressReport>().WithMany().HasForeignKey(x => x.ProgressReportId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<Models.File>().WithMany().HasForeignKey(x => x.FileId).OnDelete(DeleteBehavior.NoAction);
        });
    }
}
