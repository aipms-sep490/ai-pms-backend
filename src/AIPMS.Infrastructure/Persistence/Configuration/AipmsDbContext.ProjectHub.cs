using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.Infrastructure.Persistence.Generated;

public partial class AipmsDbContext
{
    private static void ConfigureProjectHub(ModelBuilder b)
    {
        b.Entity<M.Task>(e =>
        {
            e.Property(x => x.SourceMessageId).HasColumnName("source_message_id");
        });

        b.Entity<ProjectPinnedResource>(e =>
        {
            e.ToTable("project_pinned_resources");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.ProjectId).HasColumnName("project_id");
            e.Property(x => x.ResourceType).HasColumnName("resource_type").HasMaxLength(30).IsUnicode(false);
            e.Property(x => x.Title).HasColumnName("title").HasMaxLength(200);
            e.Property(x => x.Url).HasColumnName("url").HasMaxLength(1000);
            e.Property(x => x.CreatedBy).HasColumnName("created_by");
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasPrecision(0);
            e.HasOne<M.Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<M.User>().WithMany().HasForeignKey(x => x.CreatedBy).OnDelete(DeleteBehavior.NoAction);
        });

        b.Entity<EvidenceAnchoredComment>(e =>
        {
            e.ToTable("evidence_anchored_comments");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.ProjectId).HasColumnName("project_id");
            e.Property(x => x.GateInstanceId).HasColumnName("gate_instance_id");
            e.Property(x => x.EvidenceId).HasColumnName("evidence_id");
            e.Property(x => x.AnchorRef).HasColumnName("anchor_ref").HasMaxLength(200);
            e.Property(x => x.Severity).HasColumnName("severity").HasMaxLength(20).IsUnicode(false);
            e.Property(x => x.Status).HasColumnName("status").HasMaxLength(20).IsUnicode(false);
            e.Property(x => x.Content).HasColumnName("content").HasMaxLength(2000);
            e.Property(x => x.CreatedBy).HasColumnName("created_by");
            e.Property(x => x.ResolvedBy).HasColumnName("resolved_by");
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasPrecision(0);
            e.Property(x => x.ResolvedAt).HasColumnName("resolved_at").HasPrecision(0);
            e.HasIndex(x => new { x.ProjectId, x.GateInstanceId, x.Status }).HasDatabaseName("ix_evidence_anchored_comments_gate_status");
            e.HasOne<M.Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<M.User>().WithMany().HasForeignKey(x => x.CreatedBy).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<M.User>().WithMany().HasForeignKey(x => x.ResolvedBy).OnDelete(DeleteBehavior.NoAction);
        });

        b.Entity<EvidenceRubricCriterionLink>(e =>
        {
            e.ToTable("evidence_rubric_criterion_links");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.ProjectId).HasColumnName("project_id");
            e.Property(x => x.EvidenceId).HasColumnName("evidence_id");
            e.Property(x => x.RubricCriterionId).HasColumnName("rubric_criterion_id");
            e.Property(x => x.CreatedBy).HasColumnName("created_by");
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasPrecision(0);
            e.HasIndex(x => new { x.EvidenceId, x.RubricCriterionId }).IsUnique().HasDatabaseName("uq_evidence_rubric_criterion_link");
            e.HasOne<M.Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<M.RubricCriterion>().WithMany().HasForeignKey(x => x.RubricCriterionId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<M.User>().WithMany().HasForeignKey(x => x.CreatedBy).OnDelete(DeleteBehavior.NoAction);
        });
    }
}
