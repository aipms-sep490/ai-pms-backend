using System;
using System.Collections.Generic;
using AIPMS.Infrastructure.Persistence.Generated.Models;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Persistence.Generated;

public partial class AipmsDbContext : DbContext
{
    public AipmsDbContext(DbContextOptions<AipmsDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<AcademicSemester> AcademicSemesters { get; set; }

    public virtual DbSet<Deliverable> Deliverables { get; set; }

    public virtual DbSet<DeliverableVersion> DeliverableVersions { get; set; }

    public virtual DbSet<Department> Departments { get; set; }

    public virtual DbSet<Evaluation> Evaluations { get; set; }

    public virtual DbSet<EvaluationCriterion> EvaluationCriteria { get; set; }

    public virtual DbSet<EvaluationDetail> EvaluationDetails { get; set; }

    public virtual DbSet<File> Files { get; set; }

    public virtual DbSet<Major> Majors { get; set; }

    public virtual DbSet<Meeting> Meetings { get; set; }

    public virtual DbSet<MeetingParticipant> MeetingParticipants { get; set; }

    public virtual DbSet<Milestone> Milestones { get; set; }

    public virtual DbSet<Notification> Notifications { get; set; }

    public virtual DbSet<NotificationRecipient> NotificationRecipients { get; set; }

    public virtual DbSet<Organization> Organizations { get; set; }

    public virtual DbSet<ProgressReport> ProgressReports { get; set; }

    public virtual DbSet<Project> Projects { get; set; }

    public virtual DbSet<ProjectMajor> ProjectMajors { get; set; }

    public virtual DbSet<ProjectPeriod> ProjectPeriods { get; set; }

    public virtual DbSet<ProjectStatusHistory> ProjectStatusHistories { get; set; }

    public virtual DbSet<Role> Roles { get; set; }

    public virtual DbSet<Rubric> Rubrics { get; set; }

    public virtual DbSet<RubricCriterion> RubricCriteria { get; set; }

    public virtual DbSet<SupervisorAssignment> SupervisorAssignments { get; set; }

    public virtual DbSet<SupervisorExpertise> SupervisorExpertises { get; set; }

    public virtual DbSet<SupervisorFeedback> SupervisorFeedbacks { get; set; }

    public virtual DbSet<SupervisorProfile> SupervisorProfiles { get; set; }

    public virtual DbSet<SupervisorRequest> SupervisorRequests { get; set; }

    public virtual DbSet<Task> Tasks { get; set; }

    public virtual DbSet<TaskAssignee> TaskAssignees { get; set; }

    public virtual DbSet<TaskDependency> TaskDependencies { get; set; }

    public virtual DbSet<TaskStatusHistory> TaskStatusHistories { get; set; }

    public virtual DbSet<Team> Teams { get; set; }

    public virtual DbSet<TeamInvitation> TeamInvitations { get; set; }

    public virtual DbSet<TeamMember> TeamMembers { get; set; }

    public virtual DbSet<User> Users { get; set; }

    public virtual DbSet<UserRole> UserRoles { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AcademicSemester>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("pk_academic_semesters");

            entity.ToTable("academic_semesters");

            entity.HasIndex(e => new { e.OrganizationId, e.Status }, "ix_academic_semesters_organization_status");

            entity.HasIndex(e => new { e.OrganizationId, e.Code }, "uq_academic_semesters_org_code").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Code)
                .HasMaxLength(50)
                .HasColumnName("code");
            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnName("created_at");
            entity.Property(e => e.EndDate).HasColumnName("end_date");
            entity.Property(e => e.Name)
                .HasMaxLength(255)
                .HasColumnName("name");
            entity.Property(e => e.OrganizationId).HasColumnName("organization_id");
            entity.Property(e => e.StartDate).HasColumnName("start_date");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValue("DRAFT")
                .HasColumnName("status");
            entity.Property(e => e.UpdatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.Organization).WithMany(p => p.AcademicSemesters)
                .HasForeignKey(d => d.OrganizationId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_academic_semesters_organization");
        });

        modelBuilder.Entity<Deliverable>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("pk_deliverables");

            entity.ToTable("deliverables");

            entity.HasIndex(e => e.MilestoneId, "ix_deliverables_milestone_id").HasFilter("([milestone_id] IS NOT NULL)");

            entity.HasIndex(e => new { e.ProjectId, e.Status }, "ix_deliverables_project_status");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnName("created_at");
            entity.Property(e => e.CreatedBy).HasColumnName("created_by");
            entity.Property(e => e.DeliverableType)
                .HasMaxLength(50)
                .HasColumnName("deliverable_type");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.DueAt)
                .HasPrecision(0)
                .HasColumnName("due_at");
            entity.Property(e => e.MilestoneId).HasColumnName("milestone_id");
            entity.Property(e => e.ProjectId).HasColumnName("project_id");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValue("DRAFT")
                .HasColumnName("status");
            entity.Property(e => e.Title)
                .HasMaxLength(255)
                .HasColumnName("title");
            entity.Property(e => e.UpdatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.CreatedByNavigation).WithMany(p => p.Deliverables)
                .HasForeignKey(d => d.CreatedBy)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_deliverables_created_by");

            entity.HasOne(d => d.Milestone).WithMany(p => p.Deliverables)
                .HasForeignKey(d => d.MilestoneId)
                .HasConstraintName("fk_deliverables_milestone");

            entity.HasOne(d => d.Project).WithMany(p => p.Deliverables)
                .HasForeignKey(d => d.ProjectId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_deliverables_project");
        });

        modelBuilder.Entity<DeliverableVersion>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("pk_deliverable_versions");

            entity.ToTable("deliverable_versions");

            entity.HasIndex(e => e.DeliverableId, "ix_deliverable_versions_deliverable_id");

            entity.HasIndex(e => new { e.DeliverableId, e.VersionNumber }, "uq_deliverable_versions_number").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnName("created_at");
            entity.Property(e => e.DeliverableId).HasColumnName("deliverable_id");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValue("SUBMITTED")
                .HasColumnName("status");
            entity.Property(e => e.SubmissionNote).HasColumnName("submission_note");
            entity.Property(e => e.SubmittedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnName("submitted_at");
            entity.Property(e => e.SubmittedBy).HasColumnName("submitted_by");
            entity.Property(e => e.UpdatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnName("updated_at");
            entity.Property(e => e.VersionNumber).HasColumnName("version_number");

            entity.HasOne(d => d.Deliverable).WithMany(p => p.DeliverableVersions)
                .HasForeignKey(d => d.DeliverableId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_deliverable_versions_deliverable");

            entity.HasOne(d => d.SubmittedByNavigation).WithMany(p => p.DeliverableVersions)
                .HasForeignKey(d => d.SubmittedBy)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_deliverable_versions_submitted_by");
        });

        modelBuilder.Entity<Department>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("pk_departments");

            entity.ToTable("departments");

            entity.HasIndex(e => e.OrganizationId, "ix_departments_organization_id");

            entity.HasIndex(e => new { e.OrganizationId, e.Code }, "uq_departments_org_code").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Code)
                .HasMaxLength(50)
                .HasColumnName("code");
            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnName("created_at");
            entity.Property(e => e.Description)
                .HasMaxLength(1000)
                .HasColumnName("description");
            entity.Property(e => e.IsActive)
                .HasDefaultValue(true)
                .HasColumnName("is_active");
            entity.Property(e => e.Name)
                .HasMaxLength(255)
                .HasColumnName("name");
            entity.Property(e => e.OrganizationId).HasColumnName("organization_id");
            entity.Property(e => e.UpdatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.Organization).WithMany(p => p.Departments)
                .HasForeignKey(d => d.OrganizationId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_departments_organization");
        });

        modelBuilder.Entity<Evaluation>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("pk_evaluations");

            entity.ToTable("evaluations");

            entity.HasIndex(e => e.EvaluatorId, "ix_evaluations_evaluator_id");

            entity.HasIndex(e => new { e.ProjectId, e.EvaluationType, e.Status }, "ix_evaluations_project_type_status");

            entity.HasIndex(e => e.RubricId, "ix_evaluations_rubric_id");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Comments).HasColumnName("comments");
            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnName("created_at");
            entity.Property(e => e.EvaluatedAt)
                .HasPrecision(0)
                .HasColumnName("evaluated_at");
            entity.Property(e => e.EvaluationType)
                .HasMaxLength(30)
                .HasColumnName("evaluation_type");
            entity.Property(e => e.EvaluatorId).HasColumnName("evaluator_id");
            entity.Property(e => e.ProjectId).HasColumnName("project_id");
            entity.Property(e => e.RubricId).HasColumnName("rubric_id");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValue("DRAFT")
                .HasColumnName("status");
            entity.Property(e => e.TotalScore)
                .HasColumnType("decimal(10, 2)")
                .HasColumnName("total_score");
            entity.Property(e => e.UpdatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.Evaluator).WithMany(p => p.Evaluations)
                .HasForeignKey(d => d.EvaluatorId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_evaluations_evaluator");

            entity.HasOne(d => d.Project).WithMany(p => p.Evaluations)
                .HasForeignKey(d => d.ProjectId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_evaluations_project");

            entity.HasOne(d => d.Rubric).WithMany(p => p.Evaluations)
                .HasForeignKey(d => d.RubricId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_evaluations_rubric");
        });

        modelBuilder.Entity<EvaluationCriterion>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("pk_evaluation_criteria");

            entity.ToTable("evaluation_criteria");

            entity.HasIndex(e => e.Code, "uq_evaluation_criteria_code").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Code)
                .HasMaxLength(50)
                .HasColumnName("code");
            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnName("created_at");
            entity.Property(e => e.Description)
                .HasMaxLength(1000)
                .HasColumnName("description");
            entity.Property(e => e.IsActive)
                .HasDefaultValue(true)
                .HasColumnName("is_active");
            entity.Property(e => e.Name)
                .HasMaxLength(255)
                .HasColumnName("name");
            entity.Property(e => e.UpdatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnName("updated_at");
        });

        modelBuilder.Entity<EvaluationDetail>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("pk_evaluation_details");

            entity.ToTable("evaluation_details");

            entity.HasIndex(e => e.RubricCriterionId, "ix_evaluation_details_rubric_criterion_id");

            entity.HasIndex(e => new { e.EvaluationId, e.RubricCriterionId }, "uq_evaluation_details_eval_rubric_criterion").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Comments)
                .HasMaxLength(2000)
                .HasColumnName("comments");
            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnName("created_at");
            entity.Property(e => e.EvaluationId).HasColumnName("evaluation_id");
            entity.Property(e => e.RubricCriterionId).HasColumnName("rubric_criterion_id");
            entity.Property(e => e.Score)
                .HasColumnType("decimal(8, 2)")
                .HasColumnName("score");
            entity.Property(e => e.UpdatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.Evaluation).WithMany(p => p.EvaluationDetails)
                .HasForeignKey(d => d.EvaluationId)
                .HasConstraintName("fk_evaluation_details_evaluation");

            entity.HasOne(d => d.RubricCriterion).WithMany(p => p.EvaluationDetails)
                .HasForeignKey(d => d.RubricCriterionId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_evaluation_details_rubric_criterion");
        });

        modelBuilder.Entity<File>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("pk_files");

            entity.ToTable("files");

            entity.HasIndex(e => e.DeliverableVersionId, "ix_files_deliverable_version_id").HasFilter("([deliverable_version_id] IS NOT NULL)");

            entity.HasIndex(e => e.MeetingId, "ix_files_meeting_id").HasFilter("([meeting_id] IS NOT NULL)");

            entity.HasIndex(e => e.ProgressReportId, "ix_files_progress_report_id").HasFilter("([progress_report_id] IS NOT NULL)");

            entity.HasIndex(e => e.UploadedBy, "ix_files_uploaded_by");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.ChecksumSha256)
                .HasMaxLength(64)
                .IsUnicode(false)
                .IsFixedLength()
                .HasColumnName("checksum_sha256");
            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnName("created_at");
            entity.Property(e => e.DeliverableVersionId).HasColumnName("deliverable_version_id");
            entity.Property(e => e.FileSizeBytes).HasColumnName("file_size_bytes");
            entity.Property(e => e.FileUrl)
                .HasMaxLength(2000)
                .HasColumnName("file_url");
            entity.Property(e => e.MeetingId).HasColumnName("meeting_id");
            entity.Property(e => e.MimeType)
                .HasMaxLength(255)
                .HasColumnName("mime_type");
            entity.Property(e => e.OriginalFileName)
                .HasMaxLength(500)
                .HasColumnName("original_file_name");
            entity.Property(e => e.ProgressReportId).HasColumnName("progress_report_id");
            entity.Property(e => e.StoragePath)
                .HasMaxLength(2000)
                .HasColumnName("storage_path");
            entity.Property(e => e.StoredFileName)
                .HasMaxLength(500)
                .HasColumnName("stored_file_name");
            entity.Property(e => e.SupervisorFeedbackId).HasColumnName("supervisor_feedback_id");
            entity.Property(e => e.UpdatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnName("updated_at");
            entity.Property(e => e.UploadedBy).HasColumnName("uploaded_by");

            entity.HasOne(d => d.DeliverableVersion).WithMany(p => p.Files)
                .HasForeignKey(d => d.DeliverableVersionId)
                .HasConstraintName("fk_files_deliverable_version");

            entity.HasOne(d => d.Meeting).WithMany(p => p.Files)
                .HasForeignKey(d => d.MeetingId)
                .HasConstraintName("fk_files_meeting");

            entity.HasOne(d => d.ProgressReport).WithMany(p => p.Files)
                .HasForeignKey(d => d.ProgressReportId)
                .HasConstraintName("fk_files_progress_report");

            entity.HasOne(d => d.SupervisorFeedback).WithMany(p => p.Files)
                .HasForeignKey(d => d.SupervisorFeedbackId)
                .HasConstraintName("fk_files_supervisor_feedback");

            entity.HasOne(d => d.UploadedByNavigation).WithMany(p => p.Files)
                .HasForeignKey(d => d.UploadedBy)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_files_uploaded_by");
        });

        modelBuilder.Entity<Major>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("pk_majors");

            entity.ToTable("majors");

            entity.HasIndex(e => e.DepartmentId, "ix_majors_department_id");

            entity.HasIndex(e => new { e.DepartmentId, e.Code }, "uq_majors_department_code").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Code)
                .HasMaxLength(50)
                .HasColumnName("code");
            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnName("created_at");
            entity.Property(e => e.DepartmentId).HasColumnName("department_id");
            entity.Property(e => e.Description)
                .HasMaxLength(1000)
                .HasColumnName("description");
            entity.Property(e => e.IsActive)
                .HasDefaultValue(true)
                .HasColumnName("is_active");
            entity.Property(e => e.Name)
                .HasMaxLength(255)
                .HasColumnName("name");
            entity.Property(e => e.UpdatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.Department).WithMany(p => p.Majors)
                .HasForeignKey(d => d.DepartmentId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_majors_department");
        });

        modelBuilder.Entity<Meeting>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("pk_meetings");

            entity.ToTable("meetings");

            entity.HasIndex(e => new { e.ProjectId, e.StartAt }, "ix_meetings_project_start_at");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Agenda).HasColumnName("agenda");
            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnName("created_at");
            entity.Property(e => e.CreatedBy).HasColumnName("created_by");
            entity.Property(e => e.EndAt)
                .HasPrecision(0)
                .HasColumnName("end_at");
            entity.Property(e => e.Location)
                .HasMaxLength(500)
                .HasColumnName("location");
            entity.Property(e => e.MeetingNotes).HasColumnName("meeting_notes");
            entity.Property(e => e.OnlineUrl)
                .HasMaxLength(1000)
                .HasColumnName("online_url");
            entity.Property(e => e.ProjectId).HasColumnName("project_id");
            entity.Property(e => e.StartAt)
                .HasPrecision(0)
                .HasColumnName("start_at");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValue("SCHEDULED")
                .HasColumnName("status");
            entity.Property(e => e.Title)
                .HasMaxLength(255)
                .HasColumnName("title");
            entity.Property(e => e.UpdatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.CreatedByNavigation).WithMany(p => p.Meetings)
                .HasForeignKey(d => d.CreatedBy)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_meetings_created_by");

            entity.HasOne(d => d.Project).WithMany(p => p.Meetings)
                .HasForeignKey(d => d.ProjectId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_meetings_project");
        });

        modelBuilder.Entity<MeetingParticipant>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("pk_meeting_participants");

            entity.ToTable("meeting_participants");

            entity.HasIndex(e => e.UserId, "ix_meeting_participants_user_id");

            entity.HasIndex(e => new { e.MeetingId, e.UserId }, "uq_meeting_participants_meeting_user").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.AttendanceStatus)
                .HasMaxLength(20)
                .HasColumnName("attendance_status");
            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnName("created_at");
            entity.Property(e => e.MeetingId).HasColumnName("meeting_id");
            entity.Property(e => e.UpdatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnName("updated_at");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.Meeting).WithMany(p => p.MeetingParticipants)
                .HasForeignKey(d => d.MeetingId)
                .HasConstraintName("fk_meeting_participants_meeting");

            entity.HasOne(d => d.User).WithMany(p => p.MeetingParticipants)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_meeting_participants_user");
        });

        modelBuilder.Entity<Milestone>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("pk_milestones");

            entity.ToTable("milestones");

            entity.HasIndex(e => new { e.ProjectId, e.Status }, "ix_milestones_project_status");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnName("created_at");
            entity.Property(e => e.CreatedBy).HasColumnName("created_by");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.DueDate).HasColumnName("due_date");
            entity.Property(e => e.ProjectId).HasColumnName("project_id");
            entity.Property(e => e.SortOrder).HasColumnName("sort_order");
            entity.Property(e => e.StartDate).HasColumnName("start_date");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValue("PLANNED")
                .HasColumnName("status");
            entity.Property(e => e.Title)
                .HasMaxLength(255)
                .HasColumnName("title");
            entity.Property(e => e.UpdatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.CreatedByNavigation).WithMany(p => p.Milestones)
                .HasForeignKey(d => d.CreatedBy)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_milestones_created_by");

            entity.HasOne(d => d.Project).WithMany(p => p.Milestones)
                .HasForeignKey(d => d.ProjectId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_milestones_project");
        });

        modelBuilder.Entity<Notification>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("pk_notifications");

            entity.ToTable("notifications");

            entity.HasIndex(e => new { e.RelatedEntityType, e.RelatedEntityId }, "ix_notifications_related_entity");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Content).HasColumnName("content");
            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnName("created_at");
            entity.Property(e => e.CreatedBy).HasColumnName("created_by");
            entity.Property(e => e.NotificationType)
                .HasMaxLength(50)
                .HasColumnName("notification_type");
            entity.Property(e => e.RelatedEntityId).HasColumnName("related_entity_id");
            entity.Property(e => e.RelatedEntityType)
                .HasMaxLength(50)
                .HasColumnName("related_entity_type");
            entity.Property(e => e.Title)
                .HasMaxLength(255)
                .HasColumnName("title");
            entity.Property(e => e.UpdatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.CreatedByNavigation).WithMany(p => p.Notifications)
                .HasForeignKey(d => d.CreatedBy)
                .HasConstraintName("fk_notifications_created_by");
        });

        modelBuilder.Entity<NotificationRecipient>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("pk_notification_recipients");

            entity.ToTable("notification_recipients");

            entity.HasIndex(e => new { e.UserId, e.IsRead, e.CreatedAt }, "ix_notification_recipients_user_read").IsDescending(false, false, true);

            entity.HasIndex(e => new { e.NotificationId, e.UserId }, "uq_notification_recipients_notification_user").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnName("created_at");
            entity.Property(e => e.DeliveredAt)
                .HasPrecision(0)
                .HasColumnName("delivered_at");
            entity.Property(e => e.IsRead).HasColumnName("is_read");
            entity.Property(e => e.NotificationId).HasColumnName("notification_id");
            entity.Property(e => e.ReadAt)
                .HasPrecision(0)
                .HasColumnName("read_at");
            entity.Property(e => e.UpdatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnName("updated_at");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.Notification).WithMany(p => p.NotificationRecipients)
                .HasForeignKey(d => d.NotificationId)
                .HasConstraintName("fk_notification_recipients_notification");

            entity.HasOne(d => d.User).WithMany(p => p.NotificationRecipients)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_notification_recipients_user");
        });

        modelBuilder.Entity<Organization>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("pk_organizations");

            entity.ToTable("organizations");

            entity.HasIndex(e => e.Code, "uq_organizations_code").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Code)
                .HasMaxLength(50)
                .HasColumnName("code");
            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnName("created_at");
            entity.Property(e => e.Description)
                .HasMaxLength(1000)
                .HasColumnName("description");
            entity.Property(e => e.IsActive)
                .HasDefaultValue(true)
                .HasColumnName("is_active");
            entity.Property(e => e.Name)
                .HasMaxLength(255)
                .HasColumnName("name");
            entity.Property(e => e.UpdatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnName("updated_at");
        });

        modelBuilder.Entity<ProgressReport>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("pk_progress_reports");

            entity.ToTable("progress_reports");

            entity.HasIndex(e => new { e.ProjectId, e.PeriodStart, e.PeriodEnd }, "ix_progress_reports_project_period");

            entity.HasIndex(e => new { e.ProjectId, e.ReportType, e.PeriodStart, e.PeriodEnd }, "uq_progress_reports_period").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CompletedWork).HasColumnName("completed_work");
            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnName("created_at");
            entity.Property(e => e.IssuesAndRisks).HasColumnName("issues_and_risks");
            entity.Property(e => e.PeriodEnd).HasColumnName("period_end");
            entity.Property(e => e.PeriodStart).HasColumnName("period_start");
            entity.Property(e => e.PlannedWork).HasColumnName("planned_work");
            entity.Property(e => e.ProjectId).HasColumnName("project_id");
            entity.Property(e => e.ReportType)
                .HasMaxLength(20)
                .HasColumnName("report_type");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValue("DRAFT")
                .HasColumnName("status");
            entity.Property(e => e.SubmittedAt)
                .HasPrecision(0)
                .HasColumnName("submitted_at");
            entity.Property(e => e.SubmittedBy).HasColumnName("submitted_by");
            entity.Property(e => e.Summary).HasColumnName("summary");
            entity.Property(e => e.UpdatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.Project).WithMany(p => p.ProgressReports)
                .HasForeignKey(d => d.ProjectId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_progress_reports_project");

            entity.HasOne(d => d.SubmittedByNavigation).WithMany(p => p.ProgressReports)
                .HasForeignKey(d => d.SubmittedBy)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_progress_reports_submitted_by");
        });

        modelBuilder.Entity<Project>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("pk_projects");

            entity.ToTable("projects");

            entity.HasIndex(e => e.Status, "ix_projects_status");

            entity.HasIndex(e => e.Code, "uq_projects_code").IsUnique();

            entity.HasIndex(e => e.TeamId, "uq_projects_team").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.ApprovedAt)
                .HasPrecision(0)
                .HasColumnName("approved_at");
            entity.Property(e => e.Code)
                .HasMaxLength(50)
                .HasColumnName("code");
            entity.Property(e => e.CompletedAt)
                .HasPrecision(0)
                .HasColumnName("completed_at");
            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnName("created_at");
            entity.Property(e => e.CreatedBy).HasColumnName("created_by");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.Objectives).HasColumnName("objectives");
            entity.Property(e => e.RegisteredAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnName("registered_at");
            entity.Property(e => e.Status)
                .HasMaxLength(30)
                .HasDefaultValue("DRAFT")
                .HasColumnName("status");
            entity.Property(e => e.SubmittedAt)
                .HasPrecision(0)
                .HasColumnName("submitted_at");
            entity.Property(e => e.TeamId).HasColumnName("team_id");
            entity.Property(e => e.Title)
                .HasMaxLength(500)
                .HasColumnName("title");
            entity.Property(e => e.UpdatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.CreatedByNavigation).WithMany(p => p.Projects)
                .HasForeignKey(d => d.CreatedBy)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_projects_created_by");

            entity.HasOne(d => d.Team).WithOne(p => p.Project)
                .HasForeignKey<Project>(d => d.TeamId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_projects_team");
        });

        modelBuilder.Entity<ProjectMajor>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("pk_project_majors");

            entity.ToTable("project_majors");

            entity.HasIndex(e => e.MajorId, "ix_project_majors_major_id");

            entity.HasIndex(e => new { e.ProjectId, e.MajorId }, "uq_project_majors_project_major").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnName("created_at");
            entity.Property(e => e.MajorId).HasColumnName("major_id");
            entity.Property(e => e.ProjectId).HasColumnName("project_id");

            entity.HasOne(d => d.Major).WithMany(p => p.ProjectMajors)
                .HasForeignKey(d => d.MajorId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_project_majors_major");

            entity.HasOne(d => d.Project).WithMany(p => p.ProjectMajors)
                .HasForeignKey(d => d.ProjectId)
                .HasConstraintName("fk_project_majors_project");
        });

        modelBuilder.Entity<ProjectPeriod>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("pk_project_periods");

            entity.ToTable("project_periods");

            entity.HasIndex(e => new { e.AcademicSemesterId, e.Status }, "ix_project_periods_semester_status");

            entity.HasIndex(e => new { e.AcademicSemesterId, e.Code }, "uq_project_periods_semester_code").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.AcademicSemesterId).HasColumnName("academic_semester_id");
            entity.Property(e => e.Code)
                .HasMaxLength(50)
                .HasColumnName("code");
            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnName("created_at");
            entity.Property(e => e.EndAt)
                .HasPrecision(0)
                .HasColumnName("end_at");
            entity.Property(e => e.Name)
                .HasMaxLength(255)
                .HasColumnName("name");
            entity.Property(e => e.PeriodType)
                .HasMaxLength(50)
                .HasColumnName("period_type");
            entity.Property(e => e.StartAt)
                .HasPrecision(0)
                .HasColumnName("start_at");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValue("DRAFT")
                .HasColumnName("status");
            entity.Property(e => e.UpdatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.AcademicSemester).WithMany(p => p.ProjectPeriods)
                .HasForeignKey(d => d.AcademicSemesterId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_project_periods_semester");
        });

        modelBuilder.Entity<ProjectStatusHistory>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("pk_project_status_history");

            entity.ToTable("project_status_history");

            entity.HasIndex(e => new { e.ProjectId, e.ChangedAt }, "ix_project_status_history_project_changed_at").IsDescending(false, true);

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.ChangedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnName("changed_at");
            entity.Property(e => e.ChangedBy).HasColumnName("changed_by");
            entity.Property(e => e.NewStatus)
                .HasMaxLength(30)
                .HasColumnName("new_status");
            entity.Property(e => e.OldStatus)
                .HasMaxLength(30)
                .HasColumnName("old_status");
            entity.Property(e => e.ProjectId).HasColumnName("project_id");
            entity.Property(e => e.Reason)
                .HasMaxLength(1000)
                .HasColumnName("reason");

            entity.HasOne(d => d.ChangedByNavigation).WithMany(p => p.ProjectStatusHistories)
                .HasForeignKey(d => d.ChangedBy)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_project_status_history_changed_by");

            entity.HasOne(d => d.Project).WithMany(p => p.ProjectStatusHistories)
                .HasForeignKey(d => d.ProjectId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_project_status_history_project");
        });

        modelBuilder.Entity<Role>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("pk_roles");

            entity.ToTable("roles");

            entity.HasIndex(e => e.Code, "uq_roles_code").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Code)
                .HasMaxLength(50)
                .HasColumnName("code");
            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnName("created_at");
            entity.Property(e => e.Description)
                .HasMaxLength(500)
                .HasColumnName("description");
            entity.Property(e => e.IsSystemRole)
                .HasDefaultValue(true)
                .HasColumnName("is_system_role");
            entity.Property(e => e.Name)
                .HasMaxLength(100)
                .HasColumnName("name");
            entity.Property(e => e.UpdatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnName("updated_at");
        });

        modelBuilder.Entity<Rubric>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("pk_rubrics");

            entity.ToTable("rubrics");

            entity.HasIndex(e => new { e.DepartmentId, e.IsActive }, "ix_rubrics_department_active").HasFilter("([department_id] IS NOT NULL)");

            entity.HasIndex(e => new { e.AcademicSemesterId, e.IsActive }, "ix_rubrics_semester_active").HasFilter("([academic_semester_id] IS NOT NULL)");

            entity.HasIndex(e => e.Code, "uq_rubrics_code").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.AcademicSemesterId).HasColumnName("academic_semester_id");
            entity.Property(e => e.Code)
                .HasMaxLength(50)
                .HasColumnName("code");
            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnName("created_at");
            entity.Property(e => e.CreatedBy).HasColumnName("created_by");
            entity.Property(e => e.DepartmentId).HasColumnName("department_id");
            entity.Property(e => e.Description)
                .HasMaxLength(1000)
                .HasColumnName("description");
            entity.Property(e => e.IsActive)
                .HasDefaultValue(true)
                .HasColumnName("is_active");
            entity.Property(e => e.Name)
                .HasMaxLength(255)
                .HasColumnName("name");
            entity.Property(e => e.UpdatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.AcademicSemester).WithMany(p => p.Rubrics)
                .HasForeignKey(d => d.AcademicSemesterId)
                .HasConstraintName("fk_rubrics_semester");

            entity.HasOne(d => d.CreatedByNavigation).WithMany(p => p.Rubrics)
                .HasForeignKey(d => d.CreatedBy)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_rubrics_created_by");

            entity.HasOne(d => d.Department).WithMany(p => p.Rubrics)
                .HasForeignKey(d => d.DepartmentId)
                .HasConstraintName("fk_rubrics_department");
        });

        modelBuilder.Entity<RubricCriterion>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("pk_rubric_criteria");

            entity.ToTable("rubric_criteria");

            entity.HasIndex(e => e.CriterionId, "ix_rubric_criteria_criterion_id");

            entity.HasIndex(e => new { e.RubricId, e.SortOrder }, "ix_rubric_criteria_rubric_sort");

            entity.HasIndex(e => new { e.RubricId, e.CriterionId }, "uq_rubric_criteria_rubric_criterion").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnName("created_at");
            entity.Property(e => e.CriterionId).HasColumnName("criterion_id");
            entity.Property(e => e.IsRequired)
                .HasDefaultValue(true)
                .HasColumnName("is_required");
            entity.Property(e => e.MaxScore)
                .HasColumnType("decimal(8, 2)")
                .HasColumnName("max_score");
            entity.Property(e => e.RubricId).HasColumnName("rubric_id");
            entity.Property(e => e.SortOrder).HasColumnName("sort_order");
            entity.Property(e => e.UpdatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnName("updated_at");
            entity.Property(e => e.WeightPercent)
                .HasColumnType("decimal(5, 2)")
                .HasColumnName("weight_percent");

            entity.HasOne(d => d.Criterion).WithMany(p => p.RubricCriteria)
                .HasForeignKey(d => d.CriterionId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_rubric_criteria_criterion");

            entity.HasOne(d => d.Rubric).WithMany(p => p.RubricCriteria)
                .HasForeignKey(d => d.RubricId)
                .HasConstraintName("fk_rubric_criteria_rubric");
        });

        modelBuilder.Entity<SupervisorAssignment>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("pk_supervisor_assignments");

            entity.ToTable("supervisor_assignments");

            entity.HasIndex(e => e.ProjectId, "ix_supervisor_assignments_project");

            entity.HasIndex(e => e.SupervisorProfileId, "ix_supervisor_assignments_supervisor");

            entity.HasIndex(e => new { e.ProjectId, e.SupervisorProfileId }, "uq_supervisor_assignments_project_supervisor").IsUnique();

            entity.HasIndex(e => e.SupervisorRequestId, "uq_supervisor_assignments_request").IsUnique();

            entity.HasIndex(e => e.ProjectId, "ux_supervisor_assignments_one_primary_active")
                .IsUnique()
                .HasFilter("([is_primary]=(1) AND [ended_at] IS NULL)");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.AssignedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnName("assigned_at");
            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnName("created_at");
            entity.Property(e => e.EndedAt)
                .HasPrecision(0)
                .HasColumnName("ended_at");
            entity.Property(e => e.IsPrimary).HasColumnName("is_primary");
            entity.Property(e => e.ProjectId).HasColumnName("project_id");
            entity.Property(e => e.SupervisorProfileId).HasColumnName("supervisor_profile_id");
            entity.Property(e => e.SupervisorRequestId).HasColumnName("supervisor_request_id");
            entity.Property(e => e.UpdatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.Project).WithOne(p => p.SupervisorAssignment)
                .HasForeignKey<SupervisorAssignment>(d => d.ProjectId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_supervisor_assignments_project");

            entity.HasOne(d => d.SupervisorProfile).WithMany(p => p.SupervisorAssignments)
                .HasForeignKey(d => d.SupervisorProfileId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_supervisor_assignments_profile");

            entity.HasOne(d => d.SupervisorRequest).WithOne(p => p.SupervisorAssignment)
                .HasForeignKey<SupervisorAssignment>(d => d.SupervisorRequestId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_supervisor_assignments_request");
        });

        modelBuilder.Entity<SupervisorExpertise>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("pk_supervisor_expertise");

            entity.ToTable("supervisor_expertise");

            entity.HasIndex(e => e.SupervisorProfileId, "ix_supervisor_expertise_profile_id");

            entity.HasIndex(e => new { e.SupervisorProfileId, e.ExpertiseName }, "uq_supervisor_expertise_name").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnName("created_at");
            entity.Property(e => e.ExpertiseName)
                .HasMaxLength(255)
                .HasColumnName("expertise_name");
            entity.Property(e => e.ProficiencyLevel)
                .HasMaxLength(50)
                .HasColumnName("proficiency_level");
            entity.Property(e => e.SupervisorProfileId).HasColumnName("supervisor_profile_id");
            entity.Property(e => e.UpdatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.SupervisorProfile).WithMany(p => p.SupervisorExpertises)
                .HasForeignKey(d => d.SupervisorProfileId)
                .HasConstraintName("fk_supervisor_expertise_profile");
        });

        modelBuilder.Entity<SupervisorFeedback>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("pk_supervisor_feedback");

            entity.ToTable("supervisor_feedback");

            entity.HasIndex(e => e.ProjectId, "ix_supervisor_feedback_project_id");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnName("created_at");
            entity.Property(e => e.DeliverableVersionId).HasColumnName("deliverable_version_id");
            entity.Property(e => e.FeedbackText).HasColumnName("feedback_text");
            entity.Property(e => e.MeetingId).HasColumnName("meeting_id");
            entity.Property(e => e.ProgressReportId).HasColumnName("progress_report_id");
            entity.Property(e => e.ProjectId).HasColumnName("project_id");
            entity.Property(e => e.SupervisorAssignmentId).HasColumnName("supervisor_assignment_id");
            entity.Property(e => e.UpdatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.DeliverableVersion).WithMany(p => p.SupervisorFeedbacks)
                .HasForeignKey(d => d.DeliverableVersionId)
                .HasConstraintName("fk_supervisor_feedback_deliverable_version");

            entity.HasOne(d => d.Meeting).WithMany(p => p.SupervisorFeedbacks)
                .HasForeignKey(d => d.MeetingId)
                .HasConstraintName("fk_supervisor_feedback_meeting");

            entity.HasOne(d => d.ProgressReport).WithMany(p => p.SupervisorFeedbacks)
                .HasForeignKey(d => d.ProgressReportId)
                .HasConstraintName("fk_supervisor_feedback_progress_report");

            entity.HasOne(d => d.Project).WithMany(p => p.SupervisorFeedbacks)
                .HasForeignKey(d => d.ProjectId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_supervisor_feedback_project");

            entity.HasOne(d => d.SupervisorAssignment).WithMany(p => p.SupervisorFeedbacks)
                .HasForeignKey(d => d.SupervisorAssignmentId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_supervisor_feedback_assignment");
        });

        modelBuilder.Entity<SupervisorProfile>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("pk_supervisor_profiles");

            entity.ToTable("supervisor_profiles");

            entity.HasIndex(e => e.UserId, "uq_supervisor_profiles_user").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Bio).HasColumnName("bio");
            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnName("created_at");
            entity.Property(e => e.IsAvailable)
                .HasDefaultValue(true)
                .HasColumnName("is_available");
            entity.Property(e => e.MaxActiveProjects).HasColumnName("max_active_projects");
            entity.Property(e => e.UpdatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnName("updated_at");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.User).WithOne(p => p.SupervisorProfile)
                .HasForeignKey<SupervisorProfile>(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_supervisor_profiles_user");
        });

        modelBuilder.Entity<SupervisorRequest>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("pk_supervisor_requests");

            entity.ToTable("supervisor_requests");

            entity.HasIndex(e => new { e.ProjectId, e.Status }, "ix_supervisor_requests_project_status");

            entity.HasIndex(e => new { e.SupervisorProfileId, e.Status }, "ix_supervisor_requests_supervisor_status");

            entity.HasIndex(e => new { e.ProjectId, e.SupervisorProfileId }, "ux_supervisor_requests_pending")
                .IsUnique()
                .HasFilter("([status]=N'PENDING')");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnName("created_at");
            entity.Property(e => e.ProjectId).HasColumnName("project_id");
            entity.Property(e => e.RequestMessage)
                .HasMaxLength(2000)
                .HasColumnName("request_message");
            entity.Property(e => e.RequestedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnName("requested_at");
            entity.Property(e => e.RequestedBy).HasColumnName("requested_by");
            entity.Property(e => e.RespondedAt)
                .HasPrecision(0)
                .HasColumnName("responded_at");
            entity.Property(e => e.ResponseMessage)
                .HasMaxLength(2000)
                .HasColumnName("response_message");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValue("PENDING")
                .HasColumnName("status");
            entity.Property(e => e.SupervisorProfileId).HasColumnName("supervisor_profile_id");
            entity.Property(e => e.UpdatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.Project).WithMany(p => p.SupervisorRequests)
                .HasForeignKey(d => d.ProjectId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_supervisor_requests_project");

            entity.HasOne(d => d.RequestedByNavigation).WithMany(p => p.SupervisorRequests)
                .HasForeignKey(d => d.RequestedBy)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_supervisor_requests_requested_by");

            entity.HasOne(d => d.SupervisorProfile).WithMany(p => p.SupervisorRequests)
                .HasForeignKey(d => d.SupervisorProfileId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_supervisor_requests_profile");
        });

        modelBuilder.Entity<Task>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("pk_tasks");

            entity.ToTable("tasks");

            entity.HasIndex(e => new { e.MilestoneId, e.Status }, "ix_tasks_milestone_status");

            entity.HasIndex(e => e.ParentTaskId, "ix_tasks_parent_task_id").HasFilter("([parent_task_id] IS NOT NULL)");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CompletedAt)
                .HasPrecision(0)
                .HasColumnName("completed_at");
            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnName("created_at");
            entity.Property(e => e.CreatedBy).HasColumnName("created_by");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.DueAt)
                .HasPrecision(0)
                .HasColumnName("due_at");
            entity.Property(e => e.MilestoneId).HasColumnName("milestone_id");
            entity.Property(e => e.ParentTaskId).HasColumnName("parent_task_id");
            entity.Property(e => e.Priority)
                .HasMaxLength(20)
                .HasColumnName("priority");
            entity.Property(e => e.StartAt)
                .HasPrecision(0)
                .HasColumnName("start_at");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValue("TODO")
                .HasColumnName("status");
            entity.Property(e => e.Title)
                .HasMaxLength(255)
                .HasColumnName("title");
            entity.Property(e => e.UpdatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.CreatedByNavigation).WithMany(p => p.Tasks)
                .HasForeignKey(d => d.CreatedBy)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_tasks_created_by");

            entity.HasOne(d => d.Milestone).WithMany(p => p.Tasks)
                .HasForeignKey(d => d.MilestoneId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_tasks_milestone");

            entity.HasOne(d => d.ParentTask).WithMany(p => p.InverseParentTask)
                .HasForeignKey(d => d.ParentTaskId)
                .HasConstraintName("fk_tasks_parent");
        });

        modelBuilder.Entity<TaskAssignee>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("pk_task_assignees");

            entity.ToTable("task_assignees");

            entity.HasIndex(e => e.UserId, "ix_task_assignees_user_id");

            entity.HasIndex(e => new { e.TaskId, e.UserId }, "uq_task_assignees_task_user").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.AssignedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnName("assigned_at");
            entity.Property(e => e.AssignedBy).HasColumnName("assigned_by");
            entity.Property(e => e.TaskId).HasColumnName("task_id");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.AssignedByNavigation).WithMany(p => p.TaskAssigneeAssignedByNavigations)
                .HasForeignKey(d => d.AssignedBy)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_task_assignees_assigned_by");

            entity.HasOne(d => d.Task).WithMany(p => p.TaskAssignees)
                .HasForeignKey(d => d.TaskId)
                .HasConstraintName("fk_task_assignees_task");

            entity.HasOne(d => d.User).WithMany(p => p.TaskAssigneeUsers)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_task_assignees_user");
        });

        modelBuilder.Entity<TaskDependency>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("pk_task_dependencies");

            entity.ToTable("task_dependencies");

            entity.HasIndex(e => e.DependsOnTaskId, "ix_task_dependencies_depends_on");

            entity.HasIndex(e => new { e.TaskId, e.DependsOnTaskId }, "uq_task_dependencies_pair").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnName("created_at");
            entity.Property(e => e.DependencyType)
                .HasMaxLength(30)
                .HasDefaultValue("FINISH_TO_START")
                .HasColumnName("dependency_type");
            entity.Property(e => e.DependsOnTaskId).HasColumnName("depends_on_task_id");
            entity.Property(e => e.TaskId).HasColumnName("task_id");

            entity.HasOne(d => d.DependsOnTask).WithMany(p => p.TaskDependencyDependsOnTasks)
                .HasForeignKey(d => d.DependsOnTaskId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_task_dependencies_depends_on");

            entity.HasOne(d => d.Task).WithMany(p => p.TaskDependencyTasks)
                .HasForeignKey(d => d.TaskId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_task_dependencies_task");
        });

        modelBuilder.Entity<TaskStatusHistory>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("pk_task_status_history");

            entity.ToTable("task_status_history");

            entity.HasIndex(e => new { e.TaskId, e.ChangedAt }, "ix_task_status_history_task_changed_at").IsDescending(false, true);

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.ChangedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnName("changed_at");
            entity.Property(e => e.ChangedBy).HasColumnName("changed_by");
            entity.Property(e => e.NewStatus)
                .HasMaxLength(20)
                .HasColumnName("new_status");
            entity.Property(e => e.OldStatus)
                .HasMaxLength(20)
                .HasColumnName("old_status");
            entity.Property(e => e.Reason)
                .HasMaxLength(1000)
                .HasColumnName("reason");
            entity.Property(e => e.TaskId).HasColumnName("task_id");

            entity.HasOne(d => d.ChangedByNavigation).WithMany(p => p.TaskStatusHistories)
                .HasForeignKey(d => d.ChangedBy)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_task_status_history_changed_by");

            entity.HasOne(d => d.Task).WithMany(p => p.TaskStatusHistories)
                .HasForeignKey(d => d.TaskId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_task_status_history_task");
        });

        modelBuilder.Entity<Team>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("pk_teams");

            entity.ToTable("teams");

            entity.HasIndex(e => new { e.AcademicSemesterId, e.Status }, "ix_teams_semester_status");

            entity.HasIndex(e => new { e.Id, e.AcademicSemesterId }, "uq_teams_id_semester").IsUnique();

            entity.HasIndex(e => new { e.AcademicSemesterId, e.Code }, "uq_teams_semester_code").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.AcademicSemesterId).HasColumnName("academic_semester_id");
            entity.Property(e => e.Code)
                .HasMaxLength(50)
                .HasColumnName("code");
            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnName("created_at");
            entity.Property(e => e.CreatedBy).HasColumnName("created_by");
            entity.Property(e => e.Description)
                .HasMaxLength(1000)
                .HasColumnName("description");
            entity.Property(e => e.Name)
                .HasMaxLength(255)
                .HasColumnName("name");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValue("FORMING")
                .HasColumnName("status");
            entity.Property(e => e.UpdatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.AcademicSemester).WithMany(p => p.Teams)
                .HasForeignKey(d => d.AcademicSemesterId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_teams_semester");

            entity.HasOne(d => d.CreatedByNavigation).WithMany(p => p.Teams)
                .HasForeignKey(d => d.CreatedBy)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_teams_created_by");
        });

        modelBuilder.Entity<TeamInvitation>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("pk_team_invitations");

            entity.ToTable("team_invitations");

            entity.HasIndex(e => new { e.InvitedUserId, e.Status }, "ix_team_invitations_invited_user_status");

            entity.HasIndex(e => new { e.TeamId, e.InvitedUserId }, "ux_team_invitations_pending")
                .IsUnique()
                .HasFilter("([status]=N'PENDING')");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnName("created_at");
            entity.Property(e => e.ExpiresAt)
                .HasPrecision(0)
                .HasColumnName("expires_at");
            entity.Property(e => e.InvitedBy).HasColumnName("invited_by");
            entity.Property(e => e.InvitedUserId).HasColumnName("invited_user_id");
            entity.Property(e => e.Message)
                .HasMaxLength(1000)
                .HasColumnName("message");
            entity.Property(e => e.RespondedAt)
                .HasPrecision(0)
                .HasColumnName("responded_at");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValue("PENDING")
                .HasColumnName("status");
            entity.Property(e => e.TeamId).HasColumnName("team_id");
            entity.Property(e => e.UpdatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.InvitedByNavigation).WithMany(p => p.TeamInvitationInvitedByNavigations)
                .HasForeignKey(d => d.InvitedBy)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_team_invitations_invited_by");

            entity.HasOne(d => d.InvitedUser).WithMany(p => p.TeamInvitationInvitedUsers)
                .HasForeignKey(d => d.InvitedUserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_team_invitations_invited_user");

            entity.HasOne(d => d.Team).WithMany(p => p.TeamInvitations)
                .HasForeignKey(d => d.TeamId)
                .HasConstraintName("fk_team_invitations_team");
        });

        modelBuilder.Entity<TeamMember>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("pk_team_members");

            entity.ToTable("team_members");

            entity.HasIndex(e => new { e.AcademicSemesterId, e.UserId }, "ix_team_members_semester_user");

            entity.HasIndex(e => e.UserId, "ix_team_members_user_id");

            entity.HasIndex(e => new { e.TeamId, e.UserId }, "uq_team_members_team_user").IsUnique();

            entity.HasIndex(e => new { e.AcademicSemesterId, e.UserId }, "ux_team_members_one_active_team_per_semester")
                .IsUnique()
                .HasFilter("([left_at] IS NULL)");

            entity.HasIndex(e => e.TeamId, "ux_team_members_one_leader_per_team")
                .IsUnique()
                .HasFilter("([is_leader]=(1) AND [left_at] IS NULL)");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.AcademicSemesterId).HasColumnName("academic_semester_id");
            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnName("created_at");
            entity.Property(e => e.IsLeader).HasColumnName("is_leader");
            entity.Property(e => e.JoinedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnName("joined_at");
            entity.Property(e => e.LeftAt)
                .HasPrecision(0)
                .HasColumnName("left_at");
            entity.Property(e => e.TeamId).HasColumnName("team_id");
            entity.Property(e => e.UpdatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnName("updated_at");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.User).WithMany(p => p.TeamMembers)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_team_members_user");

            entity.HasOne(d => d.Team).WithMany(p => p.TeamMembers)
                .HasPrincipalKey(p => new { p.Id, p.AcademicSemesterId })
                .HasForeignKey(d => new { d.TeamId, d.AcademicSemesterId })
                .HasConstraintName("fk_team_members_team");
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("pk_users");

            entity.ToTable("users");

            entity.HasIndex(e => e.DepartmentId, "ix_users_department_id").HasFilter("([department_id] IS NOT NULL)");

            entity.HasIndex(e => e.MajorId, "ix_users_major_id").HasFilter("([major_id] IS NOT NULL)");

            entity.HasIndex(e => e.Email, "uq_users_email").IsUnique();

            entity.HasIndex(e => e.EmployeeCode, "ux_users_employee_code_not_null")
                .IsUnique()
                .HasFilter("([employee_code] IS NOT NULL)");

            entity.HasIndex(e => e.StudentCode, "ux_users_student_code_not_null")
                .IsUnique()
                .HasFilter("([student_code] IS NOT NULL)");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnName("created_at");
            entity.Property(e => e.DepartmentId).HasColumnName("department_id");
            entity.Property(e => e.Email)
                .HasMaxLength(320)
                .HasColumnName("email");
            entity.Property(e => e.EmployeeCode)
                .HasMaxLength(50)
                .HasColumnName("employee_code");
            entity.Property(e => e.FullName)
                .HasMaxLength(255)
                .HasColumnName("full_name");
            entity.Property(e => e.LastLoginAt)
                .HasPrecision(0)
                .HasColumnName("last_login_at");
            entity.Property(e => e.MajorId).HasColumnName("major_id");
            entity.Property(e => e.PasswordHash)
                .HasMaxLength(500)
                .HasColumnName("password_hash");
            entity.Property(e => e.Phone)
                .HasMaxLength(30)
                .HasColumnName("phone");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValue("ACTIVE")
                .HasColumnName("status");
            entity.Property(e => e.StudentCode)
                .HasMaxLength(50)
                .HasColumnName("student_code");
            entity.Property(e => e.Title)
                .HasMaxLength(100)
                .HasColumnName("title");
            entity.Property(e => e.UpdatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.Department).WithMany(p => p.Users)
                .HasForeignKey(d => d.DepartmentId)
                .HasConstraintName("fk_users_department");

            entity.HasOne(d => d.Major).WithMany(p => p.Users)
                .HasForeignKey(d => d.MajorId)
                .HasConstraintName("fk_users_major");
        });

        modelBuilder.Entity<UserRole>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("pk_user_roles");

            entity.ToTable("user_roles");

            entity.HasIndex(e => e.RoleId, "ix_user_roles_role_id");

            entity.HasIndex(e => new { e.UserId, e.RoleId }, "uq_user_roles_user_role").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.AssignedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnName("assigned_at");
            entity.Property(e => e.RoleId).HasColumnName("role_id");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.Role).WithMany(p => p.UserRoles)
                .HasForeignKey(d => d.RoleId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_user_roles_role");

            entity.HasOne(d => d.User).WithMany(p => p.UserRoles)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("fk_user_roles_user");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
