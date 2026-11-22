using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.Infrastructure.Persistence.Generated;

public partial class AipmsDbContext
{
    public virtual DbSet<AcademicBlueprint> AcademicBlueprints { get; set; } = null!;
    public virtual DbSet<AcademicBlueprintApproval> AcademicBlueprintApprovals { get; set; } = null!;
    public virtual DbSet<ProjectBlueprintSnapshot> ProjectBlueprintSnapshots { get; set; } = null!;
    public virtual DbSet<GateDeadlineChangeRequest> GateDeadlineChangeRequests { get; set; } = null!;

    private static void ConfigureAcademicBlueprints(ModelBuilder b)
    {
        b.Entity<AcademicBlueprint>(e =>
        {
            e.ToTable("academic_blueprints");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.AcademicSemesterId).HasColumnName("academic_semester_id");
            e.Property(x => x.TrackCode).HasColumnName("track_code").HasMaxLength(20).IsUnicode(false);
            e.Property(x => x.Code).HasColumnName("code").HasMaxLength(60).IsUnicode(false);
            e.Property(x => x.Name).HasColumnName("name").HasMaxLength(255);
            e.Property(x => x.Version).HasColumnName("version");
            e.Property(x => x.Status).HasColumnName("status").HasMaxLength(20).IsUnicode(false);
            e.Property(x => x.LeadDepartmentId).HasColumnName("lead_department_id");
            e.Property(x => x.ParticipatingDepartmentId).HasColumnName("participating_department_id");
            e.Property(x => x.PeriodPolicyVersionId).HasColumnName("period_policy_version_id");
            e.Property(x => x.GateTemplateId).HasColumnName("gate_template_id");
            e.Property(x => x.MilestoneTemplateId).HasColumnName("milestone_template_id");
            e.Property(x => x.RubricId).HasColumnName("rubric_id");
            e.Property(x => x.ConfigJson).HasColumnName("config_json");
            e.Property(x => x.ConcurrencyToken).HasColumnName("concurrency_token").IsConcurrencyToken();
            e.Property(x => x.CreatedBy).HasColumnName("created_by");
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasPrecision(0);
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasPrecision(0);
            e.Property(x => x.PublishedAt).HasColumnName("published_at").HasPrecision(0);
            e.HasIndex(x => new { x.AcademicSemesterId, x.Code, x.Version }).IsUnique();
            e.HasMany(x => x.Approvals).WithOne().HasForeignKey(x => x.BlueprintId).OnDelete(DeleteBehavior.NoAction);
        });

        b.Entity<AcademicBlueprintApproval>(e =>
        {
            e.ToTable("academic_blueprint_approvals");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.BlueprintId).HasColumnName("blueprint_id");
            e.Property(x => x.DepartmentId).HasColumnName("department_id");
            e.Property(x => x.ScopeRole).HasColumnName("scope_role").HasMaxLength(30).IsUnicode(false);
            e.Property(x => x.Decision).HasColumnName("decision").HasMaxLength(20).IsUnicode(false);
            e.Property(x => x.DecidedBy).HasColumnName("decided_by");
            e.Property(x => x.DecidedAt).HasColumnName("decided_at").HasPrecision(0);
            e.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(1000);
            e.HasIndex(x => new { x.BlueprintId, x.DepartmentId }).IsUnique();
        });

        b.Entity<ProjectBlueprintSnapshot>(e =>
        {
            e.ToTable("project_blueprint_snapshots");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.ProjectId).HasColumnName("project_id");
            e.Property(x => x.BlueprintId).HasColumnName("blueprint_id");
            e.Property(x => x.BlueprintVersion).HasColumnName("blueprint_version");
            e.Property(x => x.SnapshotJson).HasColumnName("snapshot_json");
            e.Property(x => x.AppliedBy).HasColumnName("applied_by");
            e.Property(x => x.AppliedAt).HasColumnName("applied_at").HasPrecision(0);
            e.HasIndex(x => x.ProjectId).IsUnique();
        });

        b.Entity<GateDeadlineChangeRequest>(e =>
        {
            e.ToTable("gate_deadline_change_requests");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.ProjectId).HasColumnName("project_id");
            e.Property(x => x.GateInstanceId).HasColumnName("gate_instance_id");
            e.Property(x => x.RequestedSubmissionDueAt).HasColumnName("requested_submission_due_at").HasPrecision(0);
            e.Property(x => x.RequestedReviewEndAt).HasColumnName("requested_review_end_at").HasPrecision(0);
            e.Property(x => x.RequestedRevisionDueAt).HasColumnName("requested_revision_due_at").HasPrecision(0);
            e.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(2000);
            e.Property(x => x.Status).HasColumnName("status").HasMaxLength(20).IsUnicode(false);
            e.Property(x => x.RequestedBy).HasColumnName("requested_by");
            e.Property(x => x.ReviewedBy).HasColumnName("reviewed_by");
            e.Property(x => x.ReviewNotes).HasColumnName("review_notes").HasMaxLength(2000);
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasPrecision(0);
            e.Property(x => x.ReviewedAt).HasColumnName("reviewed_at").HasPrecision(0);
            e.Property(x => x.ConcurrencyToken).HasColumnName("concurrency_token").IsConcurrencyToken();
        });

        b.Entity<ProjectGateInstance>(e =>
        {
            e.Property(x => x.SubmissionDueAt).HasColumnName("submission_due_at").HasPrecision(0);
            e.Property(x => x.ReviewWindowEndAt).HasColumnName("review_window_end_at").HasPrecision(0);
            e.Property(x => x.RevisionDueAt).HasColumnName("revision_due_at").HasPrecision(0);
            e.Property(x => x.OffsetRuleJson).HasColumnName("offset_rule_json").HasMaxLength(1000);
        });

        b.Entity<M.Milestone>(e =>
        {
            e.Property(x => x.TemplateOriginId).HasColumnName("template_origin_id").HasMaxLength(100);
            e.Property(x => x.IsUserCustomized).HasColumnName("is_user_customized").HasDefaultValue(false);
        });

        b.Entity<Sprint>(e =>
        {
            e.Property(x => x.TemplateOriginId).HasColumnName("template_origin_id").HasMaxLength(100);
            e.Property(x => x.IsUserCustomized).HasColumnName("is_user_customized").HasDefaultValue(false);
        });

        b.Entity<M.Task>(e =>
        {
            e.Property(x => x.TemplateOriginId).HasColumnName("template_origin_id").HasMaxLength(100);
            e.Property(x => x.IsUserCustomized).HasColumnName("is_user_customized").HasDefaultValue(false);
            e.Property(x => x.IsCoreGateTask).HasColumnName("is_core_gate_task").HasDefaultValue(false);
            e.Property(x => x.BlockedSinceAt).HasColumnName("blocked_since_at").HasPrecision(0);
            e.Property(x => x.BlockerReason).HasColumnName("blocker_reason").HasMaxLength(1000);
            e.Property(x => x.GateRequirementId).HasColumnName("gate_requirement_id");
            e.Property(x => x.DeletedAt).HasColumnName("deleted_at").HasPrecision(0);
            e.Property(x => x.DeletedBy).HasColumnName("deleted_by");
            e.Property(x => x.RestoreUntilAt).HasColumnName("restore_until_at").HasPrecision(0);
        });
    }
}
