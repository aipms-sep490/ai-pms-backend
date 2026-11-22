using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Persistence.Generated;

public partial class AipmsDbContext
{
    public virtual DbSet<ReviewGateChecklistTemplate> ReviewGateChecklistTemplates { get; set; } = null!;
    public virtual DbSet<ProjectGateChecklist> ProjectGateChecklists { get; set; } = null!;
    public virtual DbSet<ProjectGateChecklistItem> ProjectGateChecklistItems { get; set; } = null!;

    private static void ConfigureProjectChecklists(ModelBuilder b)
    {
        b.Entity<ReviewGateRequirement>(e =>
        {
            e.Property(x => x.TargetCardinality).HasColumnName("target_cardinality").HasMaxLength(20).IsUnicode(false).HasDefaultValue("PROJECT");
            e.Property(x => x.DeliveryStage).HasColumnName("delivery_stage").HasMaxLength(20).IsUnicode(false);
            e.Property(x => x.FeedsCold).HasColumnName("feeds_cold").HasDefaultValue(false);
            e.Property(x => x.FeedsDefense).HasColumnName("feeds_defense").HasDefaultValue(false);
            e.Property(x => x.InputKind).HasColumnName("input_kind").HasMaxLength(30).IsUnicode(false);
            e.Property(x => x.TemplateDocumentId).HasColumnName("template_document_id");
            e.Property(x => x.CarryForwardFromCode).HasColumnName("carry_forward_from_code").HasMaxLength(50).IsUnicode(false);
            e.Property(x => x.PolicyTag).HasColumnName("policy_tag").HasMaxLength(60).IsUnicode(false);
            e.Property(x => x.RubricCriterionRef).HasColumnName("rubric_criterion_ref").HasMaxLength(50).IsUnicode(false);
        });

        b.Entity<ProjectGateRequirement>(e =>
        {
            e.Property(x => x.TargetCardinality).HasColumnName("target_cardinality").HasMaxLength(20).IsUnicode(false).HasDefaultValue("PROJECT");
            e.Property(x => x.DeliveryStage).HasColumnName("delivery_stage").HasMaxLength(20).IsUnicode(false);
            e.Property(x => x.FeedsCold).HasColumnName("feeds_cold").HasDefaultValue(false);
            e.Property(x => x.FeedsDefense).HasColumnName("feeds_defense").HasDefaultValue(false);
            e.Property(x => x.InputKind).HasColumnName("input_kind").HasMaxLength(30).IsUnicode(false);
            e.Property(x => x.TemplateDocumentId).HasColumnName("template_document_id");
            e.Property(x => x.CarryForwardFromCode).HasColumnName("carry_forward_from_code").HasMaxLength(50).IsUnicode(false);
            e.Property(x => x.PolicyTag).HasColumnName("policy_tag").HasMaxLength(60).IsUnicode(false);
            e.Property(x => x.RubricCriterionRef).HasColumnName("rubric_criterion_ref").HasMaxLength(50).IsUnicode(false);
        });

        b.Entity<DepartmentDocumentTemplate>(e =>
        {
            e.Property(x => x.Scope).HasColumnName("scope").HasMaxLength(30).IsUnicode(false).HasDefaultValue("MAJOR_SPECIFIC");
        });

        b.Entity<FinalSubmissionDraft>(e =>
        {
            e.Property(x => x.LockWorkflowStatus).HasColumnName("lock_workflow_status").HasMaxLength(30).IsUnicode(false).HasDefaultValue("OPEN");
            e.Property(x => x.LockRequestedBy).HasColumnName("lock_requested_by");
            e.Property(x => x.LockRequestedAt).HasColumnName("lock_requested_at").HasPrecision(0);
            e.Property(x => x.LockedBySupervisorId).HasColumnName("locked_by_supervisor_id");
            e.Property(x => x.ForceLockedBy).HasColumnName("force_locked_by");
            e.Property(x => x.ForceLockReason).HasColumnName("force_lock_reason").HasMaxLength(1000);
        });

        b.Entity<FinalSubmission>(e =>
        {
            e.Property(x => x.LockedBySupervisorId).HasColumnName("locked_by_supervisor_id");
            e.Property(x => x.ForceLockedBy).HasColumnName("force_locked_by");
            e.Property(x => x.ForceLockReason).HasColumnName("force_lock_reason").HasMaxLength(1000);
            e.Property(x => x.ColdManifestSha256).HasColumnName("cold_manifest_sha256").HasMaxLength(64).IsUnicode(false).IsFixedLength();
        });

        b.Entity<ColdChecklistItem>(e =>
        {
            e.Property(x => x.WaivedStatus).HasColumnName("waived_status").HasMaxLength(30).IsUnicode(false);
        });

        b.Entity<ReviewGateChecklistTemplate>(e =>
        {
            e.ToTable("review_gate_checklist_templates");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.AcademicSemesterId).HasColumnName("academic_semester_id");
            e.Property(x => x.TrackCode).HasColumnName("track_code").HasMaxLength(20).IsUnicode(false);
            e.Property(x => x.Code).HasColumnName("code").HasMaxLength(60).IsUnicode(false);
            e.Property(x => x.Name).HasColumnName("name").HasMaxLength(255);
            e.Property(x => x.Version).HasColumnName("version");
            e.Property(x => x.Status).HasColumnName("status").HasMaxLength(20).IsUnicode(false);
            e.Property(x => x.LeadDepartmentId).HasColumnName("lead_department_id");
            e.Property(x => x.ItemsJson).HasColumnName("items_json");
            e.Property(x => x.ConcurrencyToken).HasColumnName("concurrency_token").IsConcurrencyToken();
            e.Property(x => x.CreatedBy).HasColumnName("created_by");
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasPrecision(0);
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasPrecision(0);
            e.Property(x => x.PublishedAt).HasColumnName("published_at").HasPrecision(0);
            e.HasIndex(x => new { x.AcademicSemesterId, x.Code, x.Version }).IsUnique();
        });

        b.Entity<ProjectGateChecklist>(e =>
        {
            e.ToTable("project_gate_checklists");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.ProjectId).HasColumnName("project_id");
            e.Property(x => x.GateInstanceId).HasColumnName("gate_instance_id");
            e.Property(x => x.TemplateId).HasColumnName("template_id");
            e.Property(x => x.Version).HasColumnName("version");
            e.Property(x => x.Status).HasColumnName("status").HasMaxLength(20).IsUnicode(false);
            e.Property(x => x.AdvisorRecommendation).HasColumnName("advisor_recommendation").HasMaxLength(30).IsUnicode(false);
            e.Property(x => x.ReviewerRecommendation).HasColumnName("reviewer_recommendation").HasMaxLength(30).IsUnicode(false);
            e.Property(x => x.Review3ConsensusStatus).HasColumnName("review3_consensus_status").HasMaxLength(30).IsUnicode(false);
            e.Property(x => x.PublishedBy).HasColumnName("published_by");
            e.Property(x => x.PublishedAt).HasColumnName("published_at").HasPrecision(0);
            e.Property(x => x.ConcurrencyToken).HasColumnName("concurrency_token").IsConcurrencyToken();
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasPrecision(0);
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasPrecision(0);
            e.HasIndex(x => new { x.GateInstanceId, x.Version }).IsUnique();
            e.HasMany(x => x.Items).WithOne().HasForeignKey(x => x.ChecklistId).OnDelete(DeleteBehavior.NoAction);
        });

        b.Entity<ProjectGateChecklistItem>(e =>
        {
            e.ToTable("project_gate_checklist_items");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.ChecklistId).HasColumnName("checklist_id");
            e.Property(x => x.ItemCode).HasColumnName("item_code").HasMaxLength(50).IsUnicode(false);
            e.Property(x => x.Section).HasColumnName("section").HasMaxLength(150);
            e.Property(x => x.TrackScope).HasColumnName("track_scope").HasMaxLength(20).IsUnicode(false);
            e.Property(x => x.MajorId).HasColumnName("major_id");
            e.Property(x => x.ConditionTag).HasColumnName("condition_tag").HasMaxLength(40).IsUnicode(false);
            e.Property(x => x.CoreQuestion).HasColumnName("core_question").HasMaxLength(1000);
            e.Property(x => x.Priority).HasColumnName("priority").HasMaxLength(20).IsUnicode(false);
            e.Property(x => x.Weight).HasColumnName("weight").HasPrecision(5, 2);
            e.Property(x => x.IsApplicable).HasColumnName("is_applicable").HasDefaultValue(true);
            e.Property(x => x.NaReason).HasColumnName("na_reason").HasMaxLength(500);
            e.Property(x => x.TailoredCheckpoints).HasColumnName("tailored_checkpoints").HasMaxLength(2000);
            e.Property(x => x.TailoredEvidence).HasColumnName("tailored_evidence").HasMaxLength(2000);
            e.Property(x => x.ReviewerFocusPrompt).HasColumnName("reviewer_focus_prompt").HasMaxLength(2000);
            e.Property(x => x.SourceRef).HasColumnName("source_ref").HasMaxLength(500);
            e.Property(x => x.AdvisoryStatus).HasColumnName("advisory_status").HasMaxLength(30).IsUnicode(false);
            e.Property(x => x.AdvisoryNotes).HasColumnName("advisory_notes").HasMaxLength(2000);
            e.Property(x => x.AdvisoryBy).HasColumnName("advisory_by");
            e.Property(x => x.ReviewerScore).HasColumnName("reviewer_score").HasPrecision(3, 1);
            e.Property(x => x.ReviewerNotes).HasColumnName("reviewer_notes").HasMaxLength(2000);
            e.Property(x => x.ReviewerBy).HasColumnName("reviewer_by");
            e.HasIndex(x => new { x.ChecklistId, x.ItemCode }).IsUnique();
        });
    }
}
