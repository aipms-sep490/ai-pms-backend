using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.Infrastructure.Persistence.Generated;

public partial class AipmsDbContext
{
    private static void ConfigurePeerEvaluations(ModelBuilder b)
    {
        b.Entity<PeerRound>(e => {
            e.ToTable("peer_evaluation_rounds"); e.HasKey(x => x.Id);
            e.Property(x => x.Title).HasMaxLength(200);
            e.Property(x => x.Status).HasMaxLength(20).IsUnicode(false);
            e.Property(x => x.ConcurrencyToken).IsConcurrencyToken();
            e.HasMany(x => x.Members).WithOne().HasForeignKey(x => x.RoundId).OnDelete(DeleteBehavior.NoAction);
            e.HasMany(x => x.Submissions).WithOne().HasForeignKey(x => x.RoundId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<M.Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<M.User>().WithMany().HasForeignKey(x => x.CreatedBy).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<M.SupervisorAssignment>().WithMany().HasForeignKey(x => x.PrimaryAssignmentId).OnDelete(DeleteBehavior.NoAction);
        });
        b.Entity<PeerRoundMember>(e => {
            e.ToTable("peer_round_members"); e.HasKey(x => new { x.RoundId, x.UserId });
            e.Property(x => x.FullName).HasMaxLength(255);
            e.HasOne<M.User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<M.Major>().WithMany().HasForeignKey(x => x.MajorId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<M.Department>().WithMany().HasForeignKey(x => x.DepartmentId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<M.TeamMember>().WithMany().HasForeignKey(x => x.TeamMemberId).OnDelete(DeleteBehavior.NoAction);
        });
        b.Entity<PeerSubmission>(e => {
            e.ToTable("peer_submissions"); e.HasKey(x => x.Id);
            e.Property(x => x.Status).HasMaxLength(20).IsUnicode(false);
            e.Property(x => x.ConcurrencyToken).IsConcurrencyToken();
            e.HasIndex(x => new { x.RoundId, x.AuthorId }).IsUnique();
            e.HasOne<M.User>().WithMany().HasForeignKey(x => x.AuthorId).OnDelete(DeleteBehavior.NoAction);
            e.HasMany(x => x.Items).WithOne().HasForeignKey(x => x.SubmissionId).OnDelete(DeleteBehavior.NoAction);
        });
        b.Entity<PeerSubmissionItem>(e => {
            e.ToTable("peer_submission_items"); e.HasKey(x => new { x.SubmissionId, x.TargetUserId });
            e.Property(x => x.Comment).HasMaxLength(2000);
            e.HasOne<M.User>().WithMany().HasForeignKey(x => x.TargetUserId).OnDelete(DeleteBehavior.NoAction);
        });
        foreach (var type in new[] { typeof(PeerRound), typeof(PeerRoundMember), typeof(PeerSubmission), typeof(PeerSubmissionItem) })
            foreach (var p in b.Entity(type).Metadata.GetProperties())
            {
                p.SetColumnName(System.Text.RegularExpressions.Regex.Replace(p.Name, "([a-z0-9])([A-Z])", "$1_$2").ToLowerInvariant());
                if (p.ClrType == typeof(DateTime) || p.ClrType == typeof(DateTime?)) p.SetPrecision(0);
            }
    }
}
