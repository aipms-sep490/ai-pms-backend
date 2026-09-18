using System.Data;
using System.Threading.Tasks;
using AIPMS.Application.Features.Notifications.Abstractions;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using StorageDelivery = AIPMS.Infrastructure.Persistence.Models.NotificationEmailDelivery;
using AppDelivery = AIPMS.Application.Features.Notifications.Abstractions.NotificationEmailDelivery;

namespace AIPMS.Infrastructure.Persistence.Repositories;

internal sealed class NotificationEmailQueue(AipmsDbContext db) : INotificationEmailQueue
{
    public async Task<AppDelivery?> ClaimNextAsync(DateTime nowUtc, CancellationToken ct)
    {
        // READPAST is valid under READ COMMITTED and lets parallel workers skip claimed rows.
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        var row = await db.Set<StorageDelivery>()
            .FromSqlInterpolated($"""
                SELECT TOP (1) d.*
                FROM dbo.notification_email_deliveries d WITH (UPDLOCK, READPAST, ROWLOCK)
                WHERE (d.status IN ('PENDING','RETRY') AND d.next_attempt_at <= {nowUtc})
                   OR (d.status = 'SENDING' AND d.last_attempt_at < DATEADD(minute, -15, {nowUtc}))
                ORDER BY d.next_attempt_at, d.notification_recipient_id
                """).SingleOrDefaultAsync(ct);
        if (row is null) return null;
        row.Status = "SENDING";
        row.AttemptCount++;
        row.LastAttemptAt = nowUtc;
        row.LastError = null;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await db.Set<StorageDelivery>().AsNoTracking()
            .Where(d => d.NotificationRecipientId == row.NotificationRecipientId)
            .Select(d => new AppDelivery(d.NotificationRecipientId, d.AttemptCount,
                d.NotificationRecipient.User.Email, d.NotificationRecipient.User.FullName,
                d.NotificationRecipient.Notification.Title, d.NotificationRecipient.Notification.Content))
            .SingleAsync(ct);
    }

    public Task MarkSentAsync(long recipientId, DateTime nowUtc, CancellationToken ct) =>
        db.Set<StorageDelivery>().Where(d => d.NotificationRecipientId == recipientId && d.Status == "SENDING")
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.Status, "SENT")
                .SetProperty(d => d.SentAt, nowUtc).SetProperty(d => d.LastError, (string?)null), ct);

    public Task MarkFailedAsync(long recipientId, DateTime nextAttemptAtUtc, string error, bool permanent, CancellationToken ct) =>
        MarkFailedCoreAsync(recipientId, nextAttemptAtUtc, error, permanent, ct);

    private Task MarkFailedCoreAsync(long recipientId, DateTime nextAttemptAtUtc, string error, bool permanent, CancellationToken ct)
    {
        var safeError = error.Length > 1000 ? error.Substring(0, 1000) : error;
        return db.Set<StorageDelivery>().Where(d => d.NotificationRecipientId == recipientId && d.Status == "SENDING")
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.Status, permanent ? "FAILED" : "RETRY")
                .SetProperty(d => d.NextAttemptAt, nextAttemptAtUtc)
                .SetProperty(d => d.LastError, safeError), ct);
    }
}
