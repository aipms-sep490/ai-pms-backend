namespace AIPMS.Application.Features.Notifications.DTOs;

public sealed record NotificationDto(long Id, string NotificationType, string Title,
    string Content, string? RelatedEntityType, long? RelatedEntityId, DateTime CreatedAt,
    bool IsRead, DateTime? ReadAt);

public sealed record UnreadNotificationCountDto(long Count);
