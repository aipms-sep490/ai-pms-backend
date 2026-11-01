using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Persistence.Generated;

public partial class AipmsDbContext
{
    private static void ConfigureAiDeliverableReview(ModelBuilder b)
    {
        b.Entity<AiDeliverableReviewRun>(e => {
            e.ToTable("ai_deliverable_review_runs"); e.HasKey(x => x.Id); e.Property(x => x.Id).ValueGeneratedNever();
            e.HasOne<AiRun>().WithOne().HasForeignKey<AiDeliverableReviewRun>(x => x.Id).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<Generated.Models.DeliverableVersion>().WithMany().HasForeignKey(x => x.DeliverableVersionId).OnDelete(DeleteBehavior.NoAction);
            e.HasIndex(x => new { x.ProjectId, x.DeliverableVersionId, x.Id });
        });
        b.Entity<AiDeliverableReviewCriterion>(e => {
            e.ToTable("ai_deliverable_review_criteria"); e.HasKey(x => new { x.RunId, x.CriterionId });
            e.Property(x => x.CriterionName).HasMaxLength(500); e.Property(x => x.Status).HasMaxLength(24);
            e.HasOne<AiDeliverableReviewRun>().WithMany().HasForeignKey(x => x.RunId).OnDelete(DeleteBehavior.NoAction);
        });
        b.Entity<AiDeliverableReviewFinding>(e => {
            e.ToTable("ai_deliverable_review_findings"); e.HasKey(x => x.Id); e.Property(x => x.Kind).HasMaxLength(24);
            e.HasOne<AiDeliverableReviewCriterion>().WithMany().HasForeignKey(x => new { x.RunId, x.CriterionId }).OnDelete(DeleteBehavior.NoAction);
        });
        b.Entity<AiDeliverableReviewCitation>(e => {
            e.ToTable("ai_deliverable_review_citations"); e.HasKey(x => x.Id);
            e.Property(x => x.Title).HasMaxLength(500); e.Property(x => x.Version).HasMaxLength(64); e.Property(x => x.Section).HasMaxLength(500);
            e.HasOne<AiDeliverableReviewRun>().WithMany().HasForeignKey(x => x.RunId).OnDelete(DeleteBehavior.NoAction);
        });
        foreach (var type in new[] { typeof(AiDeliverableReviewRun), typeof(AiDeliverableReviewCriterion), typeof(AiDeliverableReviewFinding), typeof(AiDeliverableReviewCitation) })
            foreach (var p in b.Entity(type).Metadata.GetProperties())
                p.SetColumnName(System.Text.RegularExpressions.Regex.Replace(p.Name, "([a-z0-9])([A-Z])", "$1_$2").ToLowerInvariant());
    }
}
