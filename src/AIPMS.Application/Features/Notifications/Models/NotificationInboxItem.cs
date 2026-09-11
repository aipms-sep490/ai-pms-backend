namespace AIPMS.Application.Features.Notifications.Models;

public sealed record NotificationInboxItem(long Id, string NotificationType, string Title,
    string Content, string? RelatedEntityType, long? RelatedEntityId, DateTime CreatedAt,
    bool IsRead, DateTime? ReadAt);
