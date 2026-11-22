using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.Infrastructure.Persistence.Generated;

public partial class AipmsDbContext
{
    public DbSet<TaskCriterion> TaskCriteria => Set<TaskCriterion>();
    public DbSet<AiSetupApplication> AiSetupApplications => Set<AiSetupApplication>();

    private static void ConfigureTaskCriteria(ModelBuilder builder)
    {
        builder.Entity<TaskCriterion>(e =>
        {
            e.ToTable("task_criteria", t =>
            {
                t.HasCheckConstraint("ck_task_criteria_title", "LEN(LTRIM(RTRIM([title]))) > 0");
                t.HasCheckConstraint("ck_task_criteria_completion",
                    "([is_completed] = 0 AND [completed_at] IS NULL AND [completed_by] IS NULL) OR ([is_completed] = 1 AND [completed_at] IS NOT NULL AND [completed_by] IS NOT NULL)");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Title).HasMaxLength(500);
            e.HasOne(x => x.Task).WithMany().HasForeignKey(x => x.TaskId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<M.User>().WithMany().HasForeignKey(x => x.CompletedBy).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<M.User>().WithMany().HasForeignKey(x => x.CreatedBy).OnDelete(DeleteBehavior.NoAction);
            e.HasIndex(x => new { x.TaskId, x.SortOrder, x.Id });
            foreach (var property in e.Metadata.GetProperties())
            {
                property.SetColumnName(System.Text.RegularExpressions.Regex.Replace(property.Name, "([a-z0-9])([A-Z])", "$1_$2").ToLowerInvariant());
                if (property.ClrType == typeof(DateTime) || property.ClrType == typeof(DateTime?)) property.SetPrecision(7);
            }
        });

        builder.Entity<AiSetupApplication>(e =>
        {
            e.ToTable("ai_setup_applications", t =>
                t.HasCheckConstraint("ck_ai_setup_applications_scope", "[scope_type] IN ('PROJECT','RUBRIC')"));
            e.HasKey(x => x.Id);
            e.Property(x => x.ScopeType).HasMaxLength(24).IsUnicode(false);
            e.HasOne<M.User>().WithMany().HasForeignKey(x => x.ActorId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<M.Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.NoAction);
            e.HasIndex(x => new { x.ActorId, x.IdempotencyKey }).IsUnique();
            e.HasIndex(x => new { x.ProjectId, x.CreatedAt });
            foreach (var property in e.Metadata.GetProperties())
            {
                property.SetColumnName(System.Text.RegularExpressions.Regex.Replace(property.Name, "([a-z0-9])([A-Z])", "$1_$2").ToLowerInvariant());
                if (property.ClrType == typeof(DateTime) || property.ClrType == typeof(DateTime?)) property.SetPrecision(7);
            }
        });
    }
}
