using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Persistence.Generated;

public partial class AipmsDbContext
{
    private static void ConfigureReviewGates(ModelBuilder b)
    {
        b.Entity<ReviewGateTemplate>(e => {
            e.ToTable("review_gate_templates"); e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.Code, x.Version }).IsUnique();
            e.Property(x => x.Code).HasMaxLength(50).IsUnicode(false);
            e.Property(x => x.Name).HasMaxLength(255); e.Property(x => x.Mode).HasMaxLength(30).IsUnicode(false);
            e.Property(x => x.Major).HasMaxLength(255); e.Property(x => x.Status).HasMaxLength(20).IsUnicode(false);
            e.Property(x => x.ConcurrencyToken).IsConcurrencyToken();
            e.HasMany(x => x.Gates).WithOne().HasForeignKey(x => x.TemplateId).OnDelete(DeleteBehavior.NoAction);
        });
        b.Entity<ReviewGateDefinition>(e => {
            e.ToTable("review_gate_definitions"); e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TemplateId, x.Code }).IsUnique();
            e.Property(x => x.Code).HasMaxLength(20).IsUnicode(false); e.Property(x => x.Title).HasMaxLength(255);
            e.Property(x => x.Objective).HasMaxLength(2000); e.Property(x => x.ReviewerPolicy).HasMaxLength(100).IsUnicode(false);
            e.Property(x => x.ReviewScope).HasMaxLength(30).IsUnicode(false);
            e.HasMany(x => x.Requirements).WithOne().HasForeignKey(x => x.GateDefinitionId).OnDelete(DeleteBehavior.NoAction);
        });
        b.Entity<ReviewGateRequirement>(e => {
            e.ToTable("review_gate_requirements"); e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.GateDefinitionId, x.Code }).IsUnique();
            e.Property(x => x.Code).HasMaxLength(50).IsUnicode(false); e.Property(x => x.Title).HasMaxLength(255);
            e.Property(x => x.Type).HasMaxLength(40).IsUnicode(false); e.Property(x => x.Scope).HasMaxLength(30).IsUnicode(false);
            e.Property(x => x.Verification).HasMaxLength(30).IsUnicode(false);
        });
        b.Entity<ProjectGateInstance>(e => {
            e.ToTable("project_gate_instances"); e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.ProjectId, x.Code }).IsUnique();
            e.Property(x => x.Code).HasMaxLength(20).IsUnicode(false); e.Property(x => x.Title).HasMaxLength(255);
            e.Property(x => x.Objective).HasMaxLength(2000); e.Property(x => x.Status).HasMaxLength(30).IsUnicode(false);
            e.Property(x => x.ReviewEligibility).HasMaxLength(20).IsUnicode(false); e.Property(x => x.ConcurrencyToken).IsConcurrencyToken();
            e.HasMany(x => x.Requirements).WithOne().HasForeignKey(x => x.GateInstanceId).OnDelete(DeleteBehavior.NoAction);
            e.HasMany(x => x.Assignments).WithOne().HasForeignKey(x => x.GateInstanceId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<ReviewGateTemplate>().WithMany().HasForeignKey(x => x.TemplateId).OnDelete(DeleteBehavior.NoAction);
        });
        b.Entity<ProjectGateRequirement>(e => {
            e.ToTable("project_gate_requirements"); e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.GateInstanceId, x.Code }).IsUnique();
            e.Property(x => x.Code).HasMaxLength(50).IsUnicode(false); e.Property(x => x.Title).HasMaxLength(255);
            e.Property(x => x.Type).HasMaxLength(40).IsUnicode(false); e.Property(x => x.Scope).HasMaxLength(30).IsUnicode(false);
            e.Property(x => x.VerificationStatus).HasMaxLength(20).IsUnicode(false); e.Property(x => x.ReviewerFeedback).HasMaxLength(2000);
            e.HasMany(x => x.Evidence).WithOne(x => x.Requirement).HasForeignKey(x => x.RequirementId).OnDelete(DeleteBehavior.NoAction);
        });
        b.Entity<GateEvidence>(e => {
            e.ToTable("review_gate_evidence"); e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.RequirementId, x.Version }).IsUnique();
            e.Property(x => x.ArtifactName).HasMaxLength(255); e.Property(x => x.ContentType).HasMaxLength(255).IsUnicode(false);
            e.Property(x => x.Sha256).HasMaxLength(64).IsUnicode(false); e.Property(x => x.StorageKey).HasMaxLength(32).IsUnicode(false);
            e.Property(x => x.Notes).HasMaxLength(2000);
            e.Property(x => x.Status).HasMaxLength(20).IsUnicode(false); e.Property(x => x.ConcurrencyToken).IsConcurrencyToken();
            e.HasOne<Generated.Models.Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.NoAction);
        });
        b.Entity<GateReviewerAssignment>(e => {
            e.ToTable("review_gate_assignments"); e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.GateInstanceId, x.ReviewerId, x.Scope }).IsUnique();
            e.Property(x => x.Scope).HasMaxLength(30).IsUnicode(false); e.Property(x => x.Status).HasMaxLength(20).IsUnicode(false);
            e.Property(x => x.LatestDecision).HasMaxLength(30).IsUnicode(false); e.Property(x => x.ReviewerNotes).HasMaxLength(2000);
            e.Property(x => x.ConcurrencyToken).IsConcurrencyToken();
            e.HasOne<Generated.Models.User>().WithMany().HasForeignKey(x => x.ReviewerId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne(x => x.GateInstance).WithMany(x => x.Assignments).HasForeignKey(x => x.GateInstanceId).OnDelete(DeleteBehavior.NoAction);
        });
        b.Entity<GateEvidenceVerification>(e => {
            e.ToTable("review_gate_evidence_verifications"); e.HasKey(x => x.Id);
            e.Property(x => x.Decision).HasMaxLength(20).IsUnicode(false); e.Property(x => x.Feedback).HasMaxLength(2000);
            e.HasOne<GateEvidence>().WithMany().HasForeignKey(x => x.EvidenceId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<GateReviewerAssignment>().WithMany().HasForeignKey(x => x.AssignmentId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<Generated.Models.User>().WithMany().HasForeignKey(x => x.ReviewerId).OnDelete(DeleteBehavior.NoAction);
        });
        b.Entity<GateDecision>(e => {
            e.ToTable("review_gate_decisions"); e.HasKey(x => x.Id);
            e.Property(x => x.Decision).HasMaxLength(30).IsUnicode(false); e.Property(x => x.Notes).HasMaxLength(2000);
            e.HasOne<GateReviewerAssignment>().WithMany().HasForeignKey(x => x.AssignmentId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<Generated.Models.User>().WithMany().HasForeignKey(x => x.DecidedBy).OnDelete(DeleteBehavior.NoAction);
        });
        b.Entity<ProjectGateScopeDecision>(e => {
            e.ToTable("project_gate_scope_decisions"); e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.ProjectId, x.Scope }).IsUnique();
            e.Property(x => x.Scope).HasMaxLength(30).IsUnicode(false); e.Property(x => x.Status).HasMaxLength(30).IsUnicode(false);
            e.Property(x => x.Notes).HasMaxLength(2000); e.Property(x => x.ConcurrencyToken).IsConcurrencyToken();
            e.HasOne<Generated.Models.Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<Generated.Models.User>().WithMany().HasForeignKey(x => x.DecidedBy).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<Generated.Models.Major>().WithMany().HasForeignKey(x => x.MajorId).OnDelete(DeleteBehavior.NoAction);
        });
        foreach (var type in new[] { typeof(ReviewGateTemplate), typeof(ReviewGateDefinition), typeof(ReviewGateRequirement), typeof(ProjectGateInstance), typeof(ProjectGateRequirement), typeof(GateEvidence), typeof(GateReviewerAssignment), typeof(GateEvidenceVerification), typeof(GateDecision), typeof(ProjectGateScopeDecision) })
            foreach (var p in b.Entity(type).Metadata.GetProperties())
            {
                p.SetColumnName(System.Text.RegularExpressions.Regex.Replace(p.Name, "([a-z0-9])([A-Z])", "$1_$2").ToLowerInvariant());
                if (p.ClrType == typeof(DateTime) || p.ClrType == typeof(DateTime?)) p.SetPrecision(0);
            }
    }
}
