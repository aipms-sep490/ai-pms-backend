using Microsoft.EntityFrameworkCore;
using AIPMS.Infrastructure.Persistence.Models;

namespace AIPMS.Infrastructure.Persistence.Generated;

public partial class AipmsDbContext
{
    partial void OnModelCreatingPartial(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Models.SupervisorAssignment>()
            .ToTable(table => table.HasTrigger("tr_supervisor_assignments_require_accepted_request"));
        modelBuilder.Entity<Models.SupervisorRequest>()
            .ToTable(table => table.HasTrigger("tr_supervisor_requests_protect_active_assignment"));
        modelBuilder.Entity<Models.SupervisorFeedback>()
            .ToTable(table => table.HasTrigger("tr_supervisor_feedback_validate_deliverable_project"));

        ConfigureProgressMeetings(modelBuilder);
    }

    private static void ConfigureProgressMeetings(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProgressReportPeriod>(e => { e.ToTable("progress_report_periods"); e.HasKey(x => x.Id); e.Property(x => x.Id).HasColumnName("id"); e.Property(x => x.ProjectId).HasColumnName("project_id"); e.Property(x => x.ReportType).HasColumnName("report_type"); e.Property(x => x.PeriodStart).HasColumnName("period_start"); e.Property(x => x.PeriodEnd).HasColumnName("period_end"); e.Property(x => x.DeadlineAt).HasColumnName("deadline_at"); e.Property(x => x.LatePolicy).HasColumnName("late_policy"); e.Property(x => x.Status).HasColumnName("status"); e.Property(x => x.CreatedAt).HasColumnName("created_at"); e.Property(x => x.UpdatedAt).HasColumnName("updated_at"); });
        modelBuilder.Entity<ProgressReportMetadata>(e => { e.ToTable("progress_report_metadata"); e.HasKey(x => x.ReportId); e.Property(x => x.ReportId).HasColumnName("report_id"); e.Property(x => x.ReportPeriodId).HasColumnName("report_period_id"); e.Property(x => x.IsLate).HasColumnName("is_late"); e.Property(x => x.CreatedAt).HasColumnName("created_at"); e.Property(x => x.UpdatedAt).HasColumnName("updated_at"); });
        modelBuilder.Entity<ProgressReportSection>(e => { e.ToTable("progress_report_sections"); e.HasKey(x => x.Id); e.Property(x => x.Id).HasColumnName("id"); e.Property(x => x.ReportId).HasColumnName("report_id"); e.Property(x => x.SectionType).HasColumnName("section_type"); e.Property(x => x.Content).HasColumnName("content"); e.Property(x => x.CreatedAt).HasColumnName("created_at"); e.Property(x => x.UpdatedAt).HasColumnName("updated_at"); });
        modelBuilder.Entity<ProgressReportContribution>(e => { e.ToTable("progress_report_contributions"); e.HasKey(x => x.Id); e.Property(x => x.Id).HasColumnName("id"); e.Property(x => x.ReportId).HasColumnName("report_id"); e.Property(x => x.ContributorId).HasColumnName("contributor_id"); e.Property(x => x.SectionType).HasColumnName("section_type"); e.Property(x => x.Content).HasColumnName("content"); e.Property(x => x.CreatedAt).HasColumnName("created_at"); e.Property(x => x.UpdatedAt).HasColumnName("updated_at"); });
        modelBuilder.Entity<MeetingDecision>(e => { e.ToTable("meeting_decisions"); e.HasKey(x => x.Id); e.Property(x => x.Id).HasColumnName("id"); e.Property(x => x.MeetingId).HasColumnName("meeting_id"); e.Property(x => x.Content).HasColumnName("content"); e.Property(x => x.CreatedBy).HasColumnName("created_by"); e.Property(x => x.CreatedAt).HasColumnName("created_at"); e.Property(x => x.UpdatedAt).HasColumnName("updated_at"); });
        modelBuilder.Entity<MeetingBlocker>(e => { e.ToTable("meeting_blockers"); e.HasKey(x => x.Id); e.Property(x => x.Id).HasColumnName("id"); e.Property(x => x.MeetingId).HasColumnName("meeting_id"); e.Property(x => x.Content).HasColumnName("content"); e.Property(x => x.CreatedBy).HasColumnName("created_by"); e.Property(x => x.CreatedAt).HasColumnName("created_at"); e.Property(x => x.UpdatedAt).HasColumnName("updated_at"); });
        modelBuilder.Entity<MeetingActionItem>(e => { e.ToTable("meeting_action_items"); e.HasKey(x => x.Id); e.Property(x => x.Id).HasColumnName("id"); e.Property(x => x.MeetingId).HasColumnName("meeting_id"); e.Property(x => x.Title).HasColumnName("title"); e.Property(x => x.Description).HasColumnName("description"); e.Property(x => x.OwnerUserId).HasColumnName("owner_user_id"); e.Property(x => x.DueDate).HasColumnName("due_date"); e.Property(x => x.Status).HasColumnName("status"); e.Property(x => x.TaskId).HasColumnName("task_id"); e.Property(x => x.MilestoneId).HasColumnName("milestone_id"); e.Property(x => x.CreatedBy).HasColumnName("created_by"); e.Property(x => x.CreatedAt).HasColumnName("created_at"); e.Property(x => x.UpdatedAt).HasColumnName("updated_at"); });
    }
}
