using AIPMS.Infrastructure.Persistence.Generated.Models;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Persistence.Generated;

public partial class AipmsDbContext
{
    private static void ConfigureFinalSubmissions(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<FinalSubmissionRequirements>(e =>
        {
            e.ToTable("final_submission_requirements");
            e.HasKey(x => x.ProjectId);
            e.Property(x => x.ProjectId).HasColumnName("project_id").ValueGeneratedNever();
            e.Property(x => x.ConcurrencyToken).HasColumnName("concurrency_token").IsConcurrencyToken();
            e.Property(x => x.UpdatedBy).HasColumnName("updated_by");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasPrecision(0);
            e.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UpdatedBy).OnDelete(DeleteBehavior.NoAction);
            e.HasMany(x => x.Items).WithOne().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<FinalSubmissionRequirement>(e =>
        {
            e.ToTable("final_submission_requirement_items");
            e.HasKey(x => new { x.ProjectId, x.DeliverableId });
            e.Property(x => x.ProjectId).HasColumnName("project_id");
            e.Property(x => x.DeliverableId).HasColumnName("deliverable_id");
            e.HasOne<Deliverable>().WithMany().HasForeignKey(x => x.DeliverableId).OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<FinalSubmission>(e =>
        {
            e.ToTable("final_submissions");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.ProjectId).HasColumnName("project_id");
            e.Property(x => x.ProjectPeriodId).HasColumnName("project_period_id");
            e.Property(x => x.SubmittedBy).HasColumnName("submitted_by");
            e.Property(x => x.SubmittedAt).HasColumnName("submitted_at").HasPrecision(0);
            e.Property(x => x.Deadline).HasColumnName("deadline").HasPrecision(0);
            e.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(10000);
            e.Property(x => x.DraftConcurrencyToken).HasColumnName("draft_concurrency_token");
            e.Property(x => x.RequirementsConcurrencyToken).HasColumnName("requirements_concurrency_token");
            e.HasIndex(x => x.ProjectId).IsUnique().HasDatabaseName("uq_final_submissions_project");
            e.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<ProjectPeriod>().WithMany().HasForeignKey(x => x.ProjectPeriodId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.SubmittedBy).OnDelete(DeleteBehavior.NoAction);
            e.HasMany(x => x.Items).WithOne().HasForeignKey(x => x.SubmissionId).OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<FinalSubmissionItem>(e =>
        {
            e.ToTable("final_submission_items");
            e.HasKey(x => new { x.SubmissionId, x.DeliverableVersionId });
            e.Property(x => x.SubmissionId).HasColumnName("submission_id");
            e.Property(x => x.DeliverableVersionId).HasColumnName("deliverable_version_id");
            e.Property(x => x.DeliverableId).HasColumnName("deliverable_id");
            e.Property(x => x.Title).HasColumnName("title").HasMaxLength(255);
            e.Property(x => x.VersionNumber).HasColumnName("version_number");
            e.Property(x => x.StatusAtSubmission).HasColumnName("status_at_submission").HasMaxLength(20).IsUnicode(false);
            e.Property(x => x.WasRequired).HasColumnName("was_required");
            e.Property(x => x.FilesJson).HasColumnName("files_json");
            e.HasIndex(x => new { x.SubmissionId, x.DeliverableId }).IsUnique().HasDatabaseName("uq_final_submission_items_deliverable");
            e.HasOne<DeliverableVersion>().WithMany().HasForeignKey(x => x.DeliverableVersionId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<Deliverable>().WithMany().HasForeignKey(x => x.DeliverableId).OnDelete(DeleteBehavior.NoAction);
        });
    }
}
