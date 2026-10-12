using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.Infrastructure.Persistence.Generated;

public partial class AipmsDbContext
{
    private static void ConfigureResultAppeals(ModelBuilder b)
    {
        b.Entity<ResultAppealPolicy>(e => {
            e.ToTable("result_appeal_policies"); e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.ProjectId, x.Version }).IsUnique();
            e.Property(x => x.ApprovalReference).HasMaxLength(1000);
            e.HasOne<M.Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<M.User>().WithMany().HasForeignKey(x => x.RecordedBy).OnDelete(DeleteBehavior.NoAction);
        });
        b.Entity<ResultAppealWindow>(e => {
            e.ToTable("result_appeal_windows"); e.HasKey(x => x.Id);
            e.Property(x => x.Status).HasMaxLength(20).IsUnicode(false);
            e.Property(x => x.ConcurrencyToken).IsConcurrencyToken();
            e.HasOne<M.Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<ResultAppealPolicy>().WithMany().HasForeignKey(x => x.PolicyId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<M.User>().WithMany().HasForeignKey(x => x.CreatedBy).OnDelete(DeleteBehavior.NoAction);
        });
        b.Entity<ResultAppealRequest>(e => {
            e.ToTable("result_appeal_requests"); e.HasKey(x => x.Id);
            e.Property(x => x.Source).HasMaxLength(30).IsUnicode(false);
            e.Property(x => x.Scope).HasMaxLength(30).IsUnicode(false);
            e.Property(x => x.Status).HasMaxLength(20).IsUnicode(false);
            e.Property(x => x.Proposal).HasMaxLength(20).IsUnicode(false);
            e.Property(x => x.BaseStamp).HasMaxLength(64).IsFixedLength().IsUnicode(false);
            e.Property(x => x.Reason).HasMaxLength(2000);
            e.Property(x => x.ReviewReason).HasMaxLength(2000);
            e.Property(x => x.DecisionReason).HasMaxLength(2000);
            e.Property(x => x.ConcurrencyToken).IsConcurrencyToken();
            e.HasOne<M.Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<ResultAppealPolicy>().WithMany().HasForeignKey(x => x.PolicyId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<ResultAppealWindow>().WithMany().HasForeignKey(x => x.WindowId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<M.User>().WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<M.User>().WithMany().HasForeignKey(x => x.RequestedBy).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<M.User>().WithMany().HasForeignKey(x => x.ReviewerId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<M.User>().WithMany().HasForeignKey(x => x.DecidedBy).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<M.Department>().WithMany().HasForeignKey(x => x.ResponsibleDepartmentId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<M.Major>().WithMany().HasForeignKey(x => x.MajorId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<EvaluationSchemeComponent>().WithMany().HasForeignKey(x => x.ComponentId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<ResultCorrectionBundle>().WithMany().HasForeignKey(x => x.BundleId).OnDelete(DeleteBehavior.NoAction);
            e.HasMany(x => x.Files).WithOne().HasForeignKey(x => x.RequestId).OnDelete(DeleteBehavior.NoAction);
        });
        b.Entity<ResultAppealFile>(e => {
            e.ToTable("result_appeal_files"); e.HasKey(x => new { x.RequestId, x.FileId });
            e.Property(x => x.FileName).HasMaxLength(255); e.Property(x => x.ContentType).HasMaxLength(200);
            e.Property(x => x.StorageKey).HasMaxLength(500); e.Property(x => x.Sha256).HasMaxLength(64).IsUnicode(false).IsFixedLength();
            e.HasOne<M.File>().WithMany().HasForeignKey(x => x.FileId).OnDelete(DeleteBehavior.NoAction);
        });
        b.Entity<ResultAppealEvent>(e => {
            e.ToTable("result_appeal_events"); e.HasKey(x => x.Id);
            e.Property(x => x.Action).HasMaxLength(40).IsUnicode(false); e.Property(x => x.Reason).HasMaxLength(2000);
            e.HasOne<ResultAppealRequest>().WithMany().HasForeignKey(x => x.RequestId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<M.User>().WithMany().HasForeignKey(x => x.ActorId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<ResultCorrectionBundle>().WithMany().HasForeignKey(x => x.BundleId).OnDelete(DeleteBehavior.NoAction);
        });
        foreach (var type in new[] { typeof(ResultAppealPolicy), typeof(ResultAppealWindow), typeof(ResultAppealRequest), typeof(ResultAppealFile), typeof(ResultAppealEvent) })
            foreach (var p in b.Entity(type).Metadata.GetProperties())
            {
                p.SetColumnName(System.Text.RegularExpressions.Regex.Replace(p.Name, "([a-z0-9])([A-Z])", "$1_$2").ToLowerInvariant());
                if (p.ClrType == typeof(DateTime) || p.ClrType == typeof(DateTime?)) p.SetPrecision(0);
            }
    }
}
