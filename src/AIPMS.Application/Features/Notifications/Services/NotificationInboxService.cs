using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Notifications.Abstractions;
using AIPMS.Application.Features.Notifications.DTOs;

namespace AIPMS.Application.Features.Notifications.Services;

public sealed class NotificationInboxService(INotificationInboxRepository repository,
    ICurrentUser currentUser, TimeProvider clock)
{
    private long UserId(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!currentUser.IsAuthenticated || currentUser.UserId is not > 0)
            throw new UnauthorizedException();
        return currentUser.UserId.Value;
    }

    public async Task<PagedResult<NotificationDto>> GetAsync(bool? isRead, string? notificationType,
        int page, int pageSize, CancellationToken ct)
    {
        var type = string.IsNullOrWhiteSpace(notificationType) ? null : notificationType.Trim();
        var result = await repository.GetAsync(UserId(ct), isRead, type, page, pageSize, ct);
        return new(result.Items.Select(item => item.ToDto()).ToArray(), result.Page, result.PageSize, result.TotalCount);
    }

    public async Task<UnreadNotificationCountDto> CountUnreadAsync(CancellationToken ct) =>
        new(await repository.CountUnreadAsync(UserId(ct), ct));

    public async Task MarkReadAsync(long notificationId, CancellationToken ct)
    {
        if (!await repository.MarkReadAsync(UserId(ct), notificationId, clock.GetUtcNow().UtcDateTime, ct))
            throw new NotFoundException("Notification", notificationId);
    }

    public Task MarkAllReadAsync(CancellationToken ct) =>
        repository.MarkAllReadAsync(UserId(ct), clock.GetUtcNow().UtcDateTime, ct);
}
