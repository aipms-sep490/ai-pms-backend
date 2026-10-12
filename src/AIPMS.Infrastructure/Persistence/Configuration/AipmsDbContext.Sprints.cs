using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.Infrastructure.Persistence.Generated;

public partial class AipmsDbContext
{
    private static void ConfigureSprints(ModelBuilder b)
    {
        b.Entity<M.Task>(e => {
            e.Property(x => x.SprintId).HasColumnName("sprint_id");
            e.Property(x => x.StoryPoints).HasColumnName("story_points");
            e.Property(x => x.TaskCode).HasColumnName("task_code").HasMaxLength(25).IsUnicode(false)
                .HasComputedColumnSql("'TASK-'+CONVERT(varchar(20),[id])", stored: true);
            e.HasOne<Sprint>().WithMany().HasForeignKey(x => x.SprintId).OnDelete(DeleteBehavior.NoAction);
        });
        b.Entity<Sprint>(e => {
            e.ToTable("sprints"); e.HasKey(x => x.Id);
            e.Property(x => x.ConcurrencyToken).IsConcurrencyToken();
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Status).HasMaxLength(16).IsUnicode(false);
            e.HasOne<M.Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<M.User>().WithMany().HasForeignKey(x => x.CreatedBy).OnDelete(DeleteBehavior.NoAction);
            e.HasIndex(x => x.ProjectId).IsUnique().HasFilter("[status]='ACTIVE'");
        });
        b.Entity<TaskBoardLabel>(e => {
            e.ToTable("task_board_labels"); e.HasKey(x => new { x.TaskId, x.Label });
            e.Property(x => x.Label).HasMaxLength(32).IsUnicode(false);
            e.HasOne<M.Task>().WithMany(x => x.BoardLabels).HasForeignKey(x => x.TaskId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<SprintTaskEvent>(e => {
            e.ToTable("sprint_task_events"); e.HasKey(x => x.Id);
            e.Property(x => x.Kind).HasMaxLength(16).IsUnicode(false);
            e.Property(x => x.TaskStatus).HasMaxLength(20).IsUnicode(false);
            e.HasOne<Sprint>().WithMany().HasForeignKey(x => x.SprintId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<M.Task>().WithMany().HasForeignKey(x => x.TaskId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<M.User>().WithMany().HasForeignKey(x => x.ActorId).OnDelete(DeleteBehavior.NoAction);
            e.HasIndex(x => new { x.SprintId, x.Id });
        });
        foreach (var type in new[] { typeof(Sprint), typeof(TaskBoardLabel), typeof(SprintTaskEvent) })
            foreach (var p in b.Entity(type).Metadata.GetProperties())
            {
                p.SetColumnName(System.Text.RegularExpressions.Regex.Replace(p.Name, "([a-z0-9])([A-Z])", "$1_$2").ToLowerInvariant());
                if (p.ClrType == typeof(DateTime) || p.ClrType == typeof(DateTime?)) p.SetPrecision(7);
            }
    }
}
