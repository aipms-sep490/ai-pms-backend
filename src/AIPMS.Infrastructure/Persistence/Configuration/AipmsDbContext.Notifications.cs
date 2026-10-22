using Microsoft.EntityFrameworkCore;
using AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.Infrastructure.Persistence.Generated;

public partial class AipmsDbContext
{
    private static void ConfigureRealtimeNotifications(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<NotificationRecipient>(entity =>
        {
            entity.Property(x => x.RealtimeDeliveredAt)
                .HasPrecision(0)
                .HasColumnName("realtime_delivered_at");
            entity.HasIndex(x => new { x.RealtimeDeliveredAt, x.Id }, "ix_notification_recipients_realtime_pending")
                .HasFilter("[realtime_delivered_at] IS NULL");
        });
    }
}
