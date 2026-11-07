using AIPMS.Infrastructure.Persistence.Generated.Models;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Persistence.Generated;

public partial class AipmsDbContext
{
    public virtual DbSet<RubricCriterionLevel> RubricCriterionLevels { get; set; } = null!;
    public virtual DbSet<RubricApproval> RubricApprovals { get; set; } = null!;

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder)
    {
        ConfigureChat(modelBuilder);
        ConfigurePolicyEvaluation(modelBuilder);
        ConfigureCold(modelBuilder);
        ConfigureWeeklyAssessments(modelBuilder);
        ConfigureWorkingAgreements(modelBuilder);
        ConfigureSupervisorRecommendations(modelBuilder);
        ConfigurePeerEvaluations(modelBuilder);
        ConfigureResultRevisions(modelBuilder);
        ConfigureResultAppeals(modelBuilder);
        ConfigureIndustry(modelBuilder);
        ConfigureAcademicTracking(modelBuilder);
        ConfigureReviewGates(modelBuilder);
        ConfigureGateSessionScripts(modelBuilder);
        ConfigureMeetingCollaboration(modelBuilder);
        ConfigureSprints(modelBuilder);
        ConfigureAiRuns(modelBuilder);
        ConfigureAiDeliverableReview(modelBuilder);
        ConfigureProjectRequirements(modelBuilder);
        ConfigureDisciplineEvidence(modelBuilder);
        ConfigureExecutionConcurrency(modelBuilder);
        ConfigureGovernanceBaseline(modelBuilder);
        ConfigureTeamLeaderChangeRequests(modelBuilder);
        ConfigureTopics(modelBuilder);
        ConfigureHybridProjects(modelBuilder);
        ConfigureEvaluationDrafts(modelBuilder);
        ConfigureEvaluationFinalizations(modelBuilder);
        ConfigureProjectResults(modelBuilder);
        ConfigureFinalSubmissionDrafts(modelBuilder);
        ConfigureFinalSubmissions(modelBuilder);
        ConfigureContributions(modelBuilder);
        ConfigureScheduledNotifications(modelBuilder);
        ConfigureRealtimeNotifications(modelBuilder);
        ConfigureNotificationEmail(modelBuilder);
        ConfigurePasswordRecovery(modelBuilder);
        ConfigureExternalLogins(modelBuilder);
        ConfigureStudentQualifications(modelBuilder);
        ConfigureReportingCycles(modelBuilder);
        ConfigureTeamEligibilitySnapshots(modelBuilder);
        ConfigureAcademicKnowledge(modelBuilder);
        ConfigureRiskHistory(modelBuilder);
        modelBuilder.Entity<RubricVersion>(entity =>
        {
            entity.ToTable("rubric_versions", table =>
            {
                table.HasCheckConstraint("ck_rubric_versions_status", "[status] IN ('DRAFT','PENDING_APPROVAL','PUBLISHED','RETIRED')");
                table.HasCheckConstraint("ck_rubric_versions_number", "[version_number] > 0");
            });
            entity.HasKey(x => x.RubricId).HasName("pk_rubric_versions");
            entity.Property(x => x.RubricId).HasColumnName("rubric_id").ValueGeneratedNever();
            entity.Property(x => x.RootRubricId).HasColumnName("root_rubric_id");
            entity.Property(x => x.VersionNumber).HasColumnName("version_number");
            entity.Property(x => x.Status).HasColumnName("status").HasMaxLength(20).IsUnicode(false);
            entity.Property(x => x.ConcurrencyToken).HasColumnName("concurrency_token").IsConcurrencyToken();
            entity.HasIndex(x => new { x.RootRubricId, x.VersionNumber }).IsUnique().HasDatabaseName("uq_rubric_versions_family_number");
            entity.HasOne<Rubric>().WithMany().HasForeignKey(x => x.RubricId).OnDelete(DeleteBehavior.NoAction);
            entity.HasOne<Rubric>().WithMany().HasForeignKey(x => x.RootRubricId).OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<Rubric>(entity =>
        {
            entity.Property(e => e.MajorId).HasColumnName("major_id");
            entity.Property(e => e.ProjectMode).HasMaxLength(32).HasColumnName("project_mode");
            entity.Property(e => e.EvaluationStage).HasMaxLength(32).HasColumnName("evaluation_stage");
            entity.Property(e => e.EvaluationScope).HasMaxLength(32).HasColumnName("evaluation_scope");
            entity.Property(e => e.EffectiveFrom).HasColumnName("effective_from");
            entity.Property(e => e.EffectiveTo).HasColumnName("effective_to");
            entity.Property(e => e.ApprovalReference).HasMaxLength(1000).HasColumnName("approval_reference");
            entity.HasOne(e => e.Major).WithMany().HasForeignKey(e => e.MajorId).OnDelete(DeleteBehavior.NoAction);
            entity.HasIndex(e => new { e.AcademicSemesterId, e.ProjectMode, e.EvaluationStage, e.EvaluationScope, e.MajorId })
                .HasDatabaseName("ix_rubrics_scope");
        });

        modelBuilder.Entity<RubricCriterion>(entity =>
        {
            entity.Property(e => e.LearningOutcome)
                .HasMaxLength(500)
                .HasColumnName("learning_outcome");
            entity.Property(e => e.EvidenceGuidance)
                .HasMaxLength(1000)
                .HasColumnName("evidence_guidance");
            entity.HasMany(e => e.Levels)
                .WithOne(e => e.RubricCriterion)
                .HasForeignKey(e => e.RubricCriterionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RubricCriterionLevel>(entity =>
        {
            entity.ToTable("rubric_criterion_levels", table =>
            {
                table.HasCheckConstraint("ck_rubric_criterion_levels_order", "[level_order] > 0");
                table.HasCheckConstraint("ck_rubric_criterion_levels_band", "[score_from] IS NULL OR [score_to] IS NULL OR [score_from] <= [score_to]");
            });
            entity.HasKey(x => x.Id).HasName("pk_rubric_criterion_levels");
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.RubricCriterionId).HasColumnName("rubric_criterion_id");
            entity.Property(x => x.LevelOrder).HasColumnName("level_order");
            entity.Property(x => x.Label).HasColumnName("label").HasMaxLength(100);
            entity.Property(x => x.Descriptor).HasColumnName("descriptor").HasMaxLength(1000);
            entity.Property(x => x.ScoreFrom).HasColumnName("score_from").HasColumnType("decimal(8, 2)");
            entity.Property(x => x.ScoreTo).HasColumnName("score_to").HasColumnType("decimal(8, 2)");
            entity.Property(x => x.CreatedAt).HasPrecision(0).HasDefaultValueSql("(sysutcdatetime())").HasColumnName("created_at");
            entity.Property(x => x.UpdatedAt).HasPrecision(0).HasDefaultValueSql("(sysutcdatetime())").HasColumnName("updated_at");
            entity.HasIndex(x => new { x.RubricCriterionId, x.LevelOrder }).IsUnique().HasDatabaseName("uq_rubric_criterion_levels_order");
            entity.HasOne(x => x.RubricCriterion).WithMany(x => x.Levels).HasForeignKey(x => x.RubricCriterionId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RubricApproval>(entity =>
        {
            entity.ToTable("rubric_approvals", table =>
            {
                table.HasCheckConstraint("ck_rubric_approvals_decision", "[decision] IS NULL OR [decision] IN ('APPROVED','REJECTED')");
                table.HasCheckConstraint("ck_rubric_approvals_decided", "([decided_by] IS NULL AND [decided_at] IS NULL) OR ([decided_by] IS NOT NULL AND [decided_at] IS NOT NULL)");
                table.HasCheckConstraint("ck_rubric_approvals_four_eyes", "[decided_by] IS NULL OR [decided_by] <> [submitted_by]");
            });
            entity.HasKey(x => x.Id).HasName("pk_rubric_approvals");
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.RubricId).HasColumnName("rubric_id");
            entity.Property(x => x.SubmittedBy).HasColumnName("submitted_by");
            entity.Property(x => x.SubmittedAt).HasPrecision(0).HasDefaultValueSql("(sysutcdatetime())").HasColumnName("submitted_at");
            entity.Property(x => x.DecidedBy).HasColumnName("decided_by");
            entity.Property(x => x.DecidedAt).HasPrecision(0).HasColumnName("decided_at");
            entity.Property(x => x.Decision).HasColumnName("decision").HasMaxLength(16);
            entity.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(1000);
            entity.HasIndex(x => x.RubricId).HasDatabaseName("ix_rubric_approvals_rubric");
            entity.HasOne(x => x.Rubric).WithMany().HasForeignKey(x => x.RubricId).OnDelete(DeleteBehavior.NoAction);
            entity.HasOne(x => x.Submitter).WithMany().HasForeignKey(x => x.SubmittedBy).OnDelete(DeleteBehavior.NoAction);
            entity.HasOne(x => x.Decider).WithMany().HasForeignKey(x => x.DecidedBy).OnDelete(DeleteBehavior.NoAction);
        });
    }
}
