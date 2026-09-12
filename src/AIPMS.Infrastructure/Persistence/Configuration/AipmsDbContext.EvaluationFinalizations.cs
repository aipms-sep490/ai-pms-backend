using AIPMS.Infrastructure.Persistence.Generated.Models;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Persistence.Generated;

public partial class AipmsDbContext
{
    private static void ConfigureEvaluationFinalizations(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<EvaluationFinalization>(e =>
        {
            e.ToTable("evaluation_finalizations");
            e.HasKey(x => x.EvaluationId);
            e.Property(x => x.EvaluationId).HasColumnName("evaluation_id").ValueGeneratedNever();
            e.Property(x => x.FinalSubmissionId).HasColumnName("final_submission_id");
            e.Property(x => x.FinalizedBy).HasColumnName("finalized_by");
            e.Property(x => x.FinalizedAt).HasColumnName("finalized_at").HasPrecision(0);
            e.Property(x => x.SnapshotJson).HasColumnName("snapshot_json");
            e.HasOne<Evaluation>().WithMany().HasForeignKey(x => x.EvaluationId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<FinalSubmission>().WithMany().HasForeignKey(x => x.FinalSubmissionId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.FinalizedBy).OnDelete(DeleteBehavior.NoAction);
            e.HasIndex(x => x.FinalSubmissionId).HasDatabaseName("ix_evaluation_finalizations_submission");
        });
    }
}
