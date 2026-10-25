using AIPMS.Infrastructure.Persistence.Generated.Models;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Persistence.Generated;

public partial class AipmsDbContext
{
    private static void ConfigureRiskHistory(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RiskHistory>(e =>
        {
            e.ToTable("risk_history");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.ProjectId).HasColumnName("project_id");
            e.Property(x => x.RiskLevel).HasColumnName("risk_level").HasMaxLength(50);
            e.Property(x => x.RiskScore).HasColumnName("risk_score").HasPrecision(5, 2);
            e.Property(x => x.OverdueTasks).HasColumnName("overdue_tasks");
            e.Property(x => x.BlockedTasks).HasColumnName("blocked_tasks");
            e.Property(x => x.UnassignedTasks).HasColumnName("unassigned_tasks");
            e.Property(x => x.ProgressPercentage).HasColumnName("progress_percentage").HasPrecision(5, 2);
            e.Property(x => x.RecordedAt).HasColumnName("recorded_at");
            e.HasIndex(x => new { x.ProjectId, x.RecordedAt });
            e.HasOne(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.NoAction);
        });
    }
}
