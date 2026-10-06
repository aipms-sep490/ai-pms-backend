using System.Data;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Features.Notifications.Abstractions;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Models;
using AIPMS.Infrastructure.Services.Projects;
using Microsoft.EntityFrameworkCore;
using StorageDelivery = AIPMS.Infrastructure.Persistence.Models.NotificationEmailDelivery;
using AppDelivery = AIPMS.Application.Features.Notifications.Abstractions.NotificationEmailDelivery;

namespace AIPMS.Infrastructure.Persistence.Repositories;

internal sealed class NotificationEmailQueue(AipmsDbContext db, IProjectAccessService? access = null) : INotificationEmailQueue
{
    public async Task<AppDelivery?> ClaimNextAsync(DateTime nowUtc, CancellationToken ct, string? notificationType = null)
    {
        // READPAST is valid under READ COMMITTED and lets parallel workers skip claimed rows.
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        var row = await db.Set<StorageDelivery>()
            .FromSqlInterpolated($"""
                SELECT TOP (1) d.*
                FROM dbo.notification_email_deliveries d WITH (UPDLOCK, READPAST, ROWLOCK)
                WHERE ((d.status IN ('PENDING','RETRY') AND d.next_attempt_at <= {nowUtc})
                   OR (d.status = 'SENDING' AND d.last_attempt_at < DATEADD(minute,
                       CASE WHEN EXISTS (SELECT 1 FROM dbo.notification_recipients r
                           JOIN dbo.notifications n ON n.id=r.notification_id
                           WHERE r.id=d.notification_recipient_id AND n.notification_type='MEETING_START_REMINDER')
                       THEN -6 ELSE -15 END, {nowUtc})))
                  AND ({notificationType} IS NULL OR EXISTS (
                    SELECT 1 FROM dbo.notification_recipients r JOIN dbo.notifications n ON n.id=r.notification_id
                    WHERE r.id=d.notification_recipient_id AND n.notification_type={notificationType}))
                ORDER BY d.next_attempt_at, d.notification_recipient_id
                """).SingleOrDefaultAsync(ct);
        if (row is null) return null;
        await db.Entry(row).ReloadAsync(ct);
        row.Status = "SENDING";
        row.AttemptCount++;
        var claimedAttempt = row.AttemptCount;
        row.LastAttemptAt = nowUtc;
        row.LastError = null;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        var source = await db.Set<StorageDelivery>().AsNoTracking()
            .Where(d => d.NotificationRecipientId == row.NotificationRecipientId)
            .Select(d => new { d.NotificationRecipient.NotificationId, d.NotificationRecipient.UserId,
                d.NotificationRecipient.Notification.NotificationType, Delivery = new AppDelivery(d.NotificationRecipientId, claimedAttempt,
                d.NotificationRecipient.User.Email, d.NotificationRecipient.User.FullName,
                d.NotificationRecipient.Notification.Title, d.NotificationRecipient.Notification.Content, true) })
            .SingleAsync(ct);
        if (source.NotificationType != IMeetingReminderService.NotificationType) return source.Delivery;
        var current = await MeetingReminderService.CurrentEmailAsync(db, access ?? new ProjectAccessService(db),
            source.NotificationId, source.UserId, nowUtc, ct);
        if (current is { } email) return source.Delivery with { Subject = email.Subject, Body = email.Body };
        await MarkFailedAsync(row.NotificationRecipientId, nowUtc, MeetingReminderService.SuppressedCode, true, ct, claimedAttempt);
        return source.Delivery with { ShouldSend = false };
    }

    public Task MarkSentAsync(long recipientId, DateTime nowUtc, CancellationToken ct, int? expectedAttempt = null) =>
        db.Set<StorageDelivery>().Where(d => d.NotificationRecipientId == recipientId && d.Status == "SENDING"
                && (expectedAttempt == null || d.AttemptCount == expectedAttempt))
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.Status, "SENT")
                .SetProperty(d => d.SentAt, nowUtc).SetProperty(d => d.LastError, (string?)null), ct);

    public Task MarkFailedAsync(long recipientId, DateTime nextAttemptAtUtc, string error, bool permanent, CancellationToken ct, int? expectedAttempt = null) =>
        MarkFailedCoreAsync(recipientId, nextAttemptAtUtc, error, permanent, ct, expectedAttempt);

    private Task MarkFailedCoreAsync(long recipientId, DateTime nextAttemptAtUtc, string error, bool permanent, CancellationToken ct, int? expectedAttempt)
    {
        var safeError = error.Length > 1000 ? error.Substring(0, 1000) : error;
        return db.Set<StorageDelivery>().Where(d => d.NotificationRecipientId == recipientId && d.Status == "SENDING"
                && (expectedAttempt == null || d.AttemptCount == expectedAttempt))
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.Status, permanent ? "FAILED" : "RETRY")
                .SetProperty(d => d.NextAttemptAt, nextAttemptAtUtc)
                .SetProperty(d => d.LastError, safeError), ct);
    }
}
