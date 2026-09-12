using AIPMS.Infrastructure.Persistence.Generated.Models;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Persistence.Generated;

public partial class AipmsDbContext
{
    private static void ConfigureProjectResults(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProjectResultPolicy>(e =>
        {
            e.ToTable("project_result_policies"); e.HasKey(x => x.ProjectId);
            e.Property(x => x.ProjectId).HasColumnName("project_id").ValueGeneratedNever();
            e.Property(x => x.PassThreshold).HasColumnName("pass_threshold").HasPrecision(4, 2);
            e.Property(x => x.ConcurrencyToken).HasColumnName("concurrency_token").IsConcurrencyToken();
            e.Property(x => x.UpdatedBy).HasColumnName("updated_by");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasPrecision(0);
            e.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UpdatedBy).OnDelete(DeleteBehavior.NoAction);
            e.HasMany(x => x.Items).WithOne().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<ProjectResultPolicyItem>(e =>
        {
            e.ToTable("project_result_policy_items"); e.HasKey(x => new { x.ProjectId, x.AssignmentId });
            e.Property(x => x.ProjectId).HasColumnName("project_id");
            e.Property(x => x.AssignmentId).HasColumnName("assignment_id");
            e.Property(x => x.WeightPercent).HasColumnName("weight_percent").HasPrecision(5, 2);
            e.HasOne<EvaluationAssignment>().WithMany().HasForeignKey(x => x.AssignmentId).OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<ProjectResult>(e =>
        {
            e.ToTable("project_results"); e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.ProjectId).HasColumnName("project_id");
            e.Property(x => x.FinalSubmissionId).HasColumnName("final_submission_id");
            e.Property(x => x.PublishedBy).HasColumnName("published_by");
            e.Property(x => x.PublishedAt).HasColumnName("published_at").HasPrecision(0);
            e.Property(x => x.SnapshotJson).HasColumnName("snapshot_json");
            e.HasIndex(x => x.ProjectId).IsUnique().HasDatabaseName("uq_project_results_project");
            e.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<FinalSubmission>().WithMany().HasForeignKey(x => x.FinalSubmissionId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.PublishedBy).OnDelete(DeleteBehavior.NoAction);
            e.HasMany(x => x.Evaluations).WithOne().HasForeignKey(x => x.ResultId).OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<ProjectResultEvaluation>(e =>
        {
            e.ToTable("project_result_evaluations"); e.HasKey(x => new { x.ResultId, x.EvaluationId });
            e.Property(x => x.ResultId).HasColumnName("result_id");
            e.Property(x => x.EvaluationId).HasColumnName("evaluation_id");
            e.HasOne<EvaluationFinalization>().WithMany().HasForeignKey(x => x.EvaluationId).OnDelete(DeleteBehavior.NoAction);
        });
    }
}
