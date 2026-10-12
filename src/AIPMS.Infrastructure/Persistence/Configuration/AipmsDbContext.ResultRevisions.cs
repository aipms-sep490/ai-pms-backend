using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.Infrastructure.Persistence.Generated;

public partial class AipmsDbContext
{
    private static void ConfigureResultRevisions(ModelBuilder b)
    {
        b.Entity<ResultCorrectionBundle>(e => {
            e.ToTable("result_correction_bundles"); e.HasKey(x => x.Id);
            e.Property(x => x.Reason).HasMaxLength(2000); e.Property(x => x.Status).HasMaxLength(20).IsUnicode(false);
            e.Property(x => x.BaseStamp).HasMaxLength(64).IsUnicode(false).IsFixedLength();
            e.Property(x => x.ConcurrencyToken).IsConcurrencyToken();
            e.HasOne<M.Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<M.User>().WithMany().HasForeignKey(x => x.CreatedBy).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<M.User>().WithMany().HasForeignKey(x => x.PublishedBy).OnDelete(DeleteBehavior.NoAction);
            e.HasMany(x => x.Amendments).WithOne().HasForeignKey(x => x.BundleId).OnDelete(DeleteBehavior.NoAction);
        });
        b.Entity<ResultScoreAmendment>(e => {
            e.ToTable("result_score_amendments"); e.HasKey(x => new { x.BundleId, x.EvaluationId, x.CriterionId });
            e.Property(x => x.Score).HasPrecision(9, 2); e.Property(x => x.Reason).HasMaxLength(2000);
            e.HasOne<M.Evaluation>().WithMany().HasForeignKey(x => x.EvaluationId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<M.RubricCriterion>().WithMany().HasForeignKey(x => x.CriterionId).OnDelete(DeleteBehavior.NoAction);
        });
        b.Entity<ResultRevision>(e => {
            e.ToTable("result_revisions"); e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.ProjectId, x.TargetKey, x.Version }).IsUnique();
            e.HasIndex(x => new { x.BundleId, x.TargetKey }).IsUnique();
            e.HasOne<ResultCorrectionBundle>().WithMany().HasForeignKey(x => x.BundleId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<M.Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<M.User>().WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<M.User>().WithMany().HasForeignKey(x => x.PublishedBy).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<ResultRevision>().WithMany().HasForeignKey(x => x.PreviousRevisionId).OnDelete(DeleteBehavior.NoAction);
        });
        foreach (var type in new[] { typeof(ResultCorrectionBundle), typeof(ResultScoreAmendment), typeof(ResultRevision) })
            foreach (var p in b.Entity(type).Metadata.GetProperties())
            {
                p.SetColumnName(System.Text.RegularExpressions.Regex.Replace(p.Name, "([a-z0-9])([A-Z])", "$1_$2").ToLowerInvariant());
                if (p.ClrType == typeof(DateTime) || p.ClrType == typeof(DateTime?)) p.SetPrecision(0);
            }
    }
}
