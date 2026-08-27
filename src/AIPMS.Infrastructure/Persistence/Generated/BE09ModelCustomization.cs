using AIPMS.Infrastructure.Persistence.Generated.Models;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Persistence.Generated;

public partial class AipmsDbContext
{
    partial void OnModelCreatingPartial(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Rubric>(entity =>
        {
            entity.Property(x => x.VersionNumber).HasColumnName("version_number");
            entity.Property(x => x.ApprovalStatus).HasColumnName("approval_status");
            entity.Property(x => x.ApprovedBy).HasColumnName("approved_by");
            entity.Property(x => x.ApprovedAt).HasColumnName("approved_at");
            entity.Property(x => x.RowVersion).HasColumnName("row_version").IsRowVersion();
            entity.HasOne(x => x.ApprovedByNavigation).WithMany().HasForeignKey(x => x.ApprovedBy).HasConstraintName("fk_rubrics_approved_by");
        });
        modelBuilder.Entity<Evaluation>(entity =>
        {
            entity.Property(x => x.EvidenceSummary).HasColumnName("evidence_summary");
            entity.Property(x => x.FinalizedBy).HasColumnName("finalized_by");
            entity.Property(x => x.FinalizedAt).HasColumnName("finalized_at");
            entity.Property(x => x.RoundingRule).HasColumnName("rounding_rule");
            entity.Property(x => x.RowVersion).HasColumnName("row_version").IsRowVersion();
            entity.HasOne(x => x.FinalizedByNavigation).WithMany().HasForeignKey(x => x.FinalizedBy).HasConstraintName("fk_evaluations_finalized_by");
        });
    }
}
