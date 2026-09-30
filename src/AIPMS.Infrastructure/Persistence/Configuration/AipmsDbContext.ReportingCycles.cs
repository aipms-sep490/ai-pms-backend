using AIPMS.Infrastructure.Persistence.Generated.Models;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.Infrastructure.Persistence.Generated;

public partial class AipmsDbContext
{
    public virtual DbSet<ProgressReportPeriod> ProgressReportPeriods { get; set; } = null!;
    public virtual DbSet<ProjectActionItem> ProjectActionItems { get; set; } = null!;

    private static void ConfigureReportingCycles(ModelBuilder b)
    {
        b.Entity<ProgressReportPeriod>(e =>
        {
            e.ToTable("progress_report_periods");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.ProjectId).HasColumnName("project_id");
            e.Property(x => x.ProjectPeriodId).HasColumnName("project_period_id");
            e.Property(x => x.ReportType).HasColumnName("report_type").HasMaxLength(20);
            e.Property(x => x.PeriodStart).HasColumnName("period_start").HasPrecision(0);
            e.Property(x => x.PeriodEnd).HasColumnName("period_end").HasPrecision(0);
            e.Property(x => x.Deadline).HasColumnName("deadline").HasPrecision(0);
            e.Property(x => x.LatePolicy).HasColumnName("late_policy").HasMaxLength(20);
            e.Property(x => x.ConcurrencyToken).HasColumnName("concurrency_token").IsConcurrencyToken();
            e.Property(x => x.CreatedBy).HasColumnName("created_by");
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasPrecision(0);
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasPrecision(0);

            e.HasIndex(x => new { x.ProjectId, x.ReportType, x.PeriodStart, x.PeriodEnd })
                .HasDatabaseName("ix_progress_report_periods_lookup");

            e.HasOne(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne(x => x.ProjectPeriod).WithMany().HasForeignKey(x => x.ProjectPeriodId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne(x => x.Creator).WithMany().HasForeignKey(x => x.CreatedBy).OnDelete(DeleteBehavior.NoAction);
        });

        b.Entity<ProjectActionItem>(e =>
        {
            e.ToTable("project_action_items");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.ProjectId).HasColumnName("project_id");
            e.Property(x => x.SourceType).HasColumnName("source_type").HasMaxLength(30);
            e.Property(x => x.MeetingId).HasColumnName("meeting_id");
            e.Property(x => x.ProgressReportId).HasColumnName("progress_report_id");
            e.Property(x => x.Title).HasColumnName("title").HasMaxLength(500);
            e.Property(x => x.Description).HasColumnName("description");
            e.Property(x => x.OwnerId).HasColumnName("owner_id");
            e.Property(x => x.TaskId).HasColumnName("task_id");
            e.Property(x => x.MilestoneId).HasColumnName("milestone_id");
            e.Property(x => x.DueAt).HasColumnName("due_at").HasPrecision(0);
            e.Property(x => x.Status).HasColumnName("status").HasMaxLength(20);
            e.Property(x => x.ConcurrencyToken).HasColumnName("concurrency_token").IsConcurrencyToken();
            e.Property(x => x.CreatedBy).HasColumnName("created_by");
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasPrecision(0);
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasPrecision(0);

            e.HasIndex(x => new { x.ProjectId, x.Status, x.DueAt })
                .HasDatabaseName("ix_project_action_items_project");
            e.HasIndex(x => new { x.OwnerId, x.Status })
                .HasDatabaseName("ix_project_action_items_owner");
            e.HasIndex(x => x.MeetingId)
                .HasDatabaseName("ix_project_action_items_meeting")
                .HasFilter("[meeting_id] IS NOT NULL");
            e.HasIndex(x => x.ProgressReportId)
                .HasDatabaseName("ix_project_action_items_report")
                .HasFilter("[progress_report_id] IS NOT NULL");

            e.HasOne(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne(x => x.Meeting).WithMany().HasForeignKey(x => x.MeetingId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne(x => x.ProgressReport).WithMany().HasForeignKey(x => x.ProgressReportId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne(x => x.Owner).WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne(x => x.Task).WithMany().HasForeignKey(x => x.TaskId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne(x => x.Milestone).WithMany().HasForeignKey(x => x.MilestoneId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne(x => x.Creator).WithMany().HasForeignKey(x => x.CreatedBy).OnDelete(DeleteBehavior.NoAction);
        });

        b.Entity<M.ProgressReport>(e =>
        {
            e.Property(x => x.ProgressReportPeriodId).HasColumnName("progress_report_period_id");
            e.Property(x => x.IsLate).HasColumnName("is_late");
            e.Property(x => x.InProgressWork).HasColumnName("in_progress_work");
            e.Property(x => x.Blockers).HasColumnName("blockers");
            e.Property(x => x.Risks).HasColumnName("risks");
            e.Property(x => x.NextActions).HasColumnName("next_actions");

            e.HasOne(x => x.ProgressReportPeriod)
                .WithMany()
                .HasForeignKey(x => x.ProgressReportPeriodId)
                .OnDelete(DeleteBehavior.NoAction);

            e.HasIndex(x => x.ProgressReportPeriodId)
                .IsUnique()
                .HasDatabaseName("uq_progress_reports_period_id")
                .HasFilter("[progress_report_period_id] IS NOT NULL");
        });

        b.Entity<M.Meeting>(e =>
        {
            e.Property(x => x.Minutes).HasColumnName("minutes");
            e.Property(x => x.Decisions).HasColumnName("decisions");
            e.Property(x => x.Blockers).HasColumnName("blockers");
        });
    }
}
