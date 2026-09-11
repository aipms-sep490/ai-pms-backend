using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Notifications.Models;

namespace AIPMS.Application.Features.Notifications.Abstractions;

public interface INotificationInboxRepository
{
    Task<PagedResult<NotificationInboxItem>> GetAsync(long userId, bool? isRead,
        string? notificationType, int page, int pageSize, CancellationToken cancellationToken);
    Task<long> CountUnreadAsync(long userId, CancellationToken cancellationToken);
    Task<bool> MarkReadAsync(long userId, long notificationId, DateTime now, CancellationToken cancellationToken);
    Task MarkAllReadAsync(long userId, DateTime now, CancellationToken cancellationToken);
}
