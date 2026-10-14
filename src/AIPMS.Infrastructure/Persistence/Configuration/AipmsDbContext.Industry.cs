using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.Infrastructure.Persistence.Generated;

public partial class AipmsDbContext
{
    private static void ConfigureIndustry(ModelBuilder b)
    {
        b.Entity<IndustryExpert>(e => {
            e.ToTable("industry_experts"); e.HasKey(x => x.UserId); e.Property(x => x.UserId).ValueGeneratedNever();
            e.Property(x => x.Organization).HasMaxLength(255);
            e.HasOne<M.User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<M.User>().WithMany().HasForeignKey(x => x.CreatedBy).OnDelete(DeleteBehavior.NoAction);
        });
        b.Entity<IndustryInvitation>(e => {
            e.ToTable("industry_invitations"); e.HasKey(x => x.Id);
            e.Property(x => x.Email).HasMaxLength(255); e.Property(x => x.TokenHash).HasMaxLength(128).IsUnicode(false);
            e.HasIndex(x => x.TokenHash).IsUnique(); e.HasIndex(x => x.NotificationId).IsUnique();
            e.HasOne<IndustryExpert>().WithMany().HasForeignKey(x => x.ExpertId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<M.Notification>().WithMany().HasForeignKey(x => x.NotificationId).OnDelete(DeleteBehavior.NoAction);
        });
        b.Entity<IndustryChallenge>(e => {
            e.ToTable("industry_challenges"); e.HasKey(x => x.Id);
            e.Property(x => x.NonceHash).HasMaxLength(128).IsUnicode(false); e.Property(x => x.BrowserHash).HasMaxLength(128).IsUnicode(false);
        });
        b.Entity<IndustryAssignment>(e => {
            e.ToTable("industry_assignments"); e.HasKey(x => x.Id);
            e.Property(x => x.Scope).HasMaxLength(20).IsUnicode(false); e.Property(x => x.ConcurrencyToken).IsConcurrencyToken();
            e.HasOne<M.Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<IndustryExpert>().WithMany().HasForeignKey(x => x.ExpertId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<M.Major>().WithMany().HasForeignKey(x => x.MajorId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<M.User>().WithMany().HasForeignKey(x => x.AssignedBy).OnDelete(DeleteBehavior.NoAction);
        });
        b.Entity<IndustryEvidenceShare>(e => {
            e.ToTable("industry_evidence_shares"); e.HasKey(x => x.Id);
            e.Property(x => x.FileName).HasMaxLength(255); e.Property(x => x.ContentType).HasMaxLength(200);
            e.Property(x => x.StorageKey).HasMaxLength(500); e.Property(x => x.Sha256).HasMaxLength(64).IsFixedLength().IsUnicode(false);
            e.HasOne<IndustryAssignment>().WithMany().HasForeignKey(x => x.AssignmentId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<M.DeliverableVersion>().WithMany().HasForeignKey(x => x.DeliverableVersionId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<M.File>().WithMany().HasForeignKey(x => x.FileId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<M.User>().WithMany().HasForeignKey(x => x.SharedBy).OnDelete(DeleteBehavior.NoAction);
        });
        b.Entity<IndustryFeedback>(e => {
            e.ToTable("industry_feedback"); e.HasKey(x => x.Id); e.Property(x => x.Content).HasMaxLength(10000);
            e.HasIndex(x => new { x.AssignmentId, x.Version }).IsUnique();
            e.HasOne<IndustryAssignment>().WithMany().HasForeignKey(x => x.AssignmentId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<M.User>().WithMany().HasForeignKey(x => x.SubmittedBy).OnDelete(DeleteBehavior.NoAction);
        });
        foreach (var t in new[] { typeof(IndustryExpert), typeof(IndustryInvitation), typeof(IndustryChallenge), typeof(IndustryAssignment), typeof(IndustryEvidenceShare), typeof(IndustryFeedback) })
            foreach (var p in b.Entity(t).Metadata.GetProperties())
            {
                p.SetColumnName(System.Text.RegularExpressions.Regex.Replace(p.Name, "([a-z0-9])([A-Z])", "$1_$2").ToLowerInvariant());
                if (p.ClrType == typeof(DateTime) || p.ClrType == typeof(DateTime?)) p.SetPrecision(0);
            }
    }
}
