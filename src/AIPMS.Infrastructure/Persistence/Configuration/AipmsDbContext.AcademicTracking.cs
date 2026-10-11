using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Persistence.Generated;

public partial class AipmsDbContext
{
    private static void ConfigureAcademicTracking(ModelBuilder b)
    {
        b.Entity<ProjectEvidenceReview>(e => {
            e.ToTable("project_evidence_reviews"); e.HasKey(x => x.Id);
            e.Property(x => x.SourceVersion).HasMaxLength(64).IsUnicode(false);
            e.Property(x => x.Decision).HasMaxLength(20).IsUnicode(false); e.Property(x => x.Reason).HasMaxLength(2000);
            e.HasOne<Generated.Models.ProjectEvidence>().WithMany().HasForeignKey(x => x.EvidenceId).OnDelete(DeleteBehavior.NoAction);
        });
        b.Entity<ProjectCheckpoint>(e => {
            e.ToTable("project_checkpoints"); e.HasKey(x => x.Id); e.Property(x => x.Title).HasMaxLength(200);
            e.Property(x => x.ConcurrencyToken).IsConcurrencyToken();
            e.HasMany(x => x.Decisions).WithOne().HasForeignKey(x => x.CheckpointId).OnDelete(DeleteBehavior.NoAction);
        });
        b.Entity<ProjectCheckpointDecision>(e => {
            e.ToTable("project_checkpoint_decisions"); e.HasKey(x => x.Id);
            e.Property(x => x.Decision).HasMaxLength(20).IsUnicode(false); e.Property(x => x.Reason).HasMaxLength(2000);
        });
        b.Entity<DefenseCommittee>(e => {
            e.ToTable("defense_committees"); e.HasKey(x => x.Id); e.Property(x => x.Name).HasMaxLength(200);
            e.HasMany(x => x.Members).WithOne().HasForeignKey(x => x.CommitteeId).OnDelete(DeleteBehavior.NoAction);
        });
        b.Entity<DefenseCommitteeMember>(e => {
            e.ToTable("defense_committee_members"); e.HasKey(x => new { x.CommitteeId, x.UserId, x.MajorId });
        });
        b.Entity<DefenseSession>(e => {
            e.ToTable("defense_sessions"); e.HasKey(x => x.Id);
            e.Property(x => x.Room).HasMaxLength(100); e.Property(x => x.Status).HasMaxLength(20).IsUnicode(false);
            e.Property(x => x.ConcurrencyToken).IsConcurrencyToken(); e.Property(x => x.CancellationReason).HasMaxLength(2000);
            e.HasOne<DefenseCommittee>().WithMany().HasForeignKey(x => x.CommitteeId).OnDelete(DeleteBehavior.NoAction);
        });
        foreach (var type in new[] { typeof(ProjectEvidenceReview), typeof(ProjectCheckpoint), typeof(ProjectCheckpointDecision),
                     typeof(DefenseCommittee), typeof(DefenseCommitteeMember), typeof(DefenseSession) })
            foreach (var p in b.Entity(type).Metadata.GetProperties())
            {
                p.SetColumnName(System.Text.RegularExpressions.Regex.Replace(p.Name, "([a-z0-9])([A-Z])", "$1_$2").ToLowerInvariant());
                if (p.ClrType == typeof(DateTime) || p.ClrType == typeof(DateTime?)) p.SetPrecision(0);
            }
    }
}
