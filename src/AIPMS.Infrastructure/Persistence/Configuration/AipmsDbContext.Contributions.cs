using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Persistence.Generated;

public partial class AipmsDbContext
{
    public DbSet<ContributionSnapshot> ContributionSnapshots => Set<ContributionSnapshot>();

    private static void ConfigureContributions(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ContributionSnapshot>(e =>
        {
            e.ToTable("contribution_snapshots");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.ProjectId).HasColumnName("project_id");
            e.Property(x => x.UserId).HasColumnName("user_id");
            e.Property(x => x.SnapshotAt).HasColumnName("snapshot_at").HasPrecision(0);
            e.Property(x => x.SnapshotHash).HasColumnName("snapshot_hash").HasMaxLength(64).IsFixedLength();
            e.Property(x => x.ActivityScore).HasColumnName("activity_score");
            e.Property(x => x.EvidenceCount).HasColumnName("evidence_count");
            e.Property(x => x.SnapshotJson).HasColumnName("snapshot_json");
            e.HasIndex(x => new { x.ProjectId, x.UserId, x.SnapshotHash }).IsUnique();
        });
    }
}
