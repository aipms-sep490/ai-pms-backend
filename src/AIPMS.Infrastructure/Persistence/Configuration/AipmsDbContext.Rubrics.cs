using AIPMS.Infrastructure.Persistence.Generated.Models;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Persistence.Generated;

public partial class AipmsDbContext
{
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
        ConfigureMeetingCollaboration(modelBuilder);
        ConfigureSprints(modelBuilder);
        ConfigureAiRuns(modelBuilder);
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
                table.HasCheckConstraint("ck_rubric_versions_status", "[status] IN ('DRAFT','PUBLISHED','RETIRED')");
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
    }
}
