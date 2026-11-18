using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.Infrastructure.Persistence.Generated;

public partial class AipmsDbContext
{
    public DbSet<TaskDependencyImpact> TaskDependencyImpacts => Set<TaskDependencyImpact>();

    private static void ConfigureTaskDependencyImpacts(ModelBuilder builder)
    {
        builder.Entity<TaskDependencyImpact>(e =>
        {
            e.ToTable("task_dependency_impacts", t => t.HasCheckConstraint("ck_task_dependency_impacts_status",
                "[status] IN ('PENDING','CONFIRMED_VALID','REWORK_REQUIRED')"));
            e.HasKey(x => x.Id);
            e.Property(x => x.Status).HasMaxLength(24).IsUnicode(false);
            e.Property(x => x.ReopenReason).HasMaxLength(1000);
            e.Property(x => x.ReviewReason).HasMaxLength(1000);
            e.HasOne(x => x.Task).WithMany().HasForeignKey(x => x.TaskId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne(x => x.PredecessorTask).WithMany().HasForeignKey(x => x.PredecessorTaskId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne(x => x.ReopenHistory).WithMany().HasForeignKey(x => x.ReopenHistoryId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<M.User>().WithMany().HasForeignKey(x => x.ReopenedBy).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<M.User>().WithMany().HasForeignKey(x => x.ReviewedBy).OnDelete(DeleteBehavior.NoAction);
            e.HasIndex(x => new { x.TaskId, x.ReopenHistoryId }).IsUnique();
            e.HasIndex(x => new { x.TaskId, x.Status });
            foreach (var property in e.Metadata.GetProperties())
            {
                property.SetColumnName(System.Text.RegularExpressions.Regex.Replace(property.Name, "([a-z0-9])([A-Z])", "$1_$2").ToLowerInvariant());
                if (property.ClrType == typeof(DateTime) || property.ClrType == typeof(DateTime?)) property.SetPrecision(7);
            }
        });
    }
}
