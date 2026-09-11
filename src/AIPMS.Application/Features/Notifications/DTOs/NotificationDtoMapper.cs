using AIPMS.Application.Features.Notifications.Models;

namespace AIPMS.Application.Features.Notifications.DTOs;

internal static class NotificationDtoMapper
{
    public static NotificationDto ToDto(this NotificationInboxItem item) =>
        new(item.Id, item.NotificationType, item.Title, item.Content, item.RelatedEntityType,
            item.RelatedEntityId, DateTime.SpecifyKind(item.CreatedAt, DateTimeKind.Utc),
            item.IsRead, item.ReadAt.HasValue ? DateTime.SpecifyKind(item.ReadAt.Value, DateTimeKind.Utc) : null);
}
