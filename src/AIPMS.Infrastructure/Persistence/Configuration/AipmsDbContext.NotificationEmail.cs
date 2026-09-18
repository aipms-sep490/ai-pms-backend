using AIPMS.Infrastructure.Persistence.Generated.Models;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Persistence.Generated;

public partial class AipmsDbContext
{
    private static void ConfigureNotificationEmail(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<NotificationEmailDelivery>(e =>
        {
            e.ToTable("notification_email_deliveries");
            e.HasKey(x => x.NotificationRecipientId);
            e.Property(x => x.NotificationRecipientId).HasColumnName("notification_recipient_id");
            e.Property(x => x.Status).HasColumnName("status").HasMaxLength(20).IsUnicode(false);
            e.Property(x => x.AttemptCount).HasColumnName("attempt_count");
            e.Property(x => x.NextAttemptAt).HasColumnName("next_attempt_at").HasPrecision(0);
            e.Property(x => x.LastAttemptAt).HasColumnName("last_attempt_at").HasPrecision(0);
            e.Property(x => x.SentAt).HasColumnName("sent_at").HasPrecision(0);
            e.Property(x => x.LastError).HasColumnName("last_error").HasMaxLength(1000);
            e.HasIndex(x => new { x.Status, x.NextAttemptAt }, "ix_notification_email_deliveries_queue");
            e.HasOne(x => x.NotificationRecipient).WithOne().HasForeignKey<NotificationEmailDelivery>(x => x.NotificationRecipientId)
                .OnDelete(DeleteBehavior.Cascade).HasConstraintName("fk_notification_email_deliveries_recipient");
        });
    }
}
