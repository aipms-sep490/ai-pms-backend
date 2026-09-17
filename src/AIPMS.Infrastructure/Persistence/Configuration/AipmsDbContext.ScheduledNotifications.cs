using AIPMS.Infrastructure.Persistence.Generated.Models;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Persistence.Generated;

public partial class AipmsDbContext
{
    private static void ConfigureScheduledNotifications(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ScheduledNotificationOccurrence>(e =>
        {
            e.ToTable("scheduled_notification_occurrences");
            e.HasKey(x => new { x.ProjectId, x.OccurrenceKey });
            e.Property(x => x.ProjectId).HasColumnName("project_id");
            e.Property(x => x.OccurrenceKey).HasColumnName("occurrence_key").HasMaxLength(180).IsUnicode(false);
            e.Property(x => x.NotificationId).HasColumnName("notification_id");
            e.HasIndex(x => x.NotificationId).IsUnique();
            e.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne(x => x.Notification).WithMany().HasForeignKey(x => x.NotificationId).OnDelete(DeleteBehavior.NoAction);
        });
    }
}
