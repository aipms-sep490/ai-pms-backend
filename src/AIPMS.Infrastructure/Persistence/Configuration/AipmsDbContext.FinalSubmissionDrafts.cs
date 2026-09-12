using AIPMS.Infrastructure.Persistence.Generated.Models;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Persistence.Generated;

public partial class AipmsDbContext
{
    private static void ConfigureFinalSubmissionDrafts(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<FinalSubmissionDraft>(e =>
        {
            e.ToTable("final_submission_drafts");
            e.HasKey(x => x.Id).HasName("pk_final_submission_drafts");
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.ProjectId).HasColumnName("project_id");
            e.Property(x => x.ProjectPeriodId).HasColumnName("project_period_id");
            e.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(10000);
            e.Property(x => x.CreatedBy).HasColumnName("created_by");
            e.Property(x => x.UpdatedBy).HasColumnName("updated_by");
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasPrecision(0);
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasPrecision(0);
            e.Property(x => x.ConcurrencyToken).HasColumnName("concurrency_token").IsConcurrencyToken();
            e.HasIndex(x => x.ProjectId).IsUnique().HasDatabaseName("uq_final_submission_drafts_project");
            e.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<ProjectPeriod>().WithMany().HasForeignKey(x => x.ProjectPeriodId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.CreatedBy).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UpdatedBy).OnDelete(DeleteBehavior.NoAction);
            e.HasMany(x => x.Items).WithOne().HasForeignKey(x => x.DraftId).OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<FinalSubmissionDraftItem>(e =>
        {
            e.ToTable("final_submission_draft_items");
            e.HasKey(x => new { x.DraftId, x.DeliverableVersionId }).HasName("pk_final_submission_draft_items");
            e.Property(x => x.DraftId).HasColumnName("draft_id");
            e.Property(x => x.DeliverableVersionId).HasColumnName("deliverable_version_id");
            e.HasOne<DeliverableVersion>().WithMany().HasForeignKey(x => x.DeliverableVersionId).OnDelete(DeleteBehavior.NoAction);
        });
    }
}
