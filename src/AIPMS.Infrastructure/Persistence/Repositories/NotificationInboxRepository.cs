using System.Threading.Tasks;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Notifications.Abstractions;
using AIPMS.Application.Features.Notifications.Models;
using AIPMS.Infrastructure.Persistence.Generated;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Persistence.Repositories;

internal sealed class NotificationInboxRepository(AipmsDbContext context) : INotificationInboxRepository
{
    public async Task<PagedResult<NotificationInboxItem>> GetAsync(long userId, bool? isRead,
        string? notificationType, int page, int pageSize, CancellationToken ct)
    {
        var query = context.NotificationRecipients.AsNoTracking().Where(r => r.UserId == userId);
        if (isRead.HasValue) query = query.Where(r => r.IsRead == isRead.Value);
        if (notificationType is not null) query = query.Where(r => r.Notification.NotificationType == notificationType);
        var total = await query.LongCountAsync(ct);
        var items = await query.OrderByDescending(r => r.Notification.CreatedAt).ThenByDescending(r => r.NotificationId)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(r => new NotificationInboxItem(r.NotificationId, r.Notification.NotificationType,
                r.Notification.Title, r.Notification.Content, r.Notification.RelatedEntityType,
                r.Notification.RelatedEntityId, r.Notification.CreatedAt, r.IsRead, r.ReadAt))
            .ToListAsync(ct);
        return new(items, page, pageSize, total);
    }

    public Task<long> CountUnreadAsync(long userId, CancellationToken ct) =>
        context.NotificationRecipients.LongCountAsync(r => r.UserId == userId && !r.IsRead, ct);

    public Task<bool> MarkReadAsync(long userId, long notificationId, DateTime now, CancellationToken ct) =>
        RetryDeadlockAsync(() => MarkReadCoreAsync(userId, notificationId, now, ct), ct);

    private async Task<bool> MarkReadCoreAsync(long userId, long notificationId, DateTime now, CancellationToken ct)
    {
        var owned = context.NotificationRecipients.Where(r => r.UserId == userId && r.NotificationId == notificationId);
        // Conditional SQL update preserves the first read timestamp across retries and concurrent requests.
        var changed = await owned.Where(r => !r.IsRead).ExecuteUpdateAsync(setters => setters
            .SetProperty(r => r.IsRead, true).SetProperty(r => r.ReadAt, now).SetProperty(r => r.UpdatedAt, now), ct);
        return changed > 0 || await owned.AnyAsync(ct);
    }

    public async Task MarkAllReadAsync(long userId, DateTime now, CancellationToken ct) =>
        await RetryDeadlockAsync(() => context.NotificationRecipients.Where(r => r.UserId == userId && !r.IsRead)
            .ExecuteUpdateAsync(setters => setters.SetProperty(r => r.IsRead, true)
                .SetProperty(r => r.ReadAt, now).SetProperty(r => r.UpdatedAt, now), ct), ct);

    private async Task<T> RetryDeadlockAsync<T>(Func<Task<T>> action, CancellationToken ct)
    {
        // SQL rolls back the victim statement. These conditional autocommit writes are
        // idempotent; retain the original timestamp and never retry part of an outer transaction.
        for (var attempt = 0; ; attempt++)
        {
            try { return await action(); }
            catch (Exception exception) when (attempt < 3 && context.Database.CurrentTransaction is null && IsDeadlock(exception))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(25 * (attempt + 1) + Random.Shared.Next(25)), ct);
            }
        }
    }

    private static bool IsDeadlock(Exception exception)
    {
        for (Exception? cause = exception; cause is not null; cause = cause.InnerException)
            if (cause is SqlException { Number: 1205 }) return true;
        return false;
    }
}
