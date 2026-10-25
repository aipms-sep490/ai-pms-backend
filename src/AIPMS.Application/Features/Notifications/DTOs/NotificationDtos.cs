namespace AIPMS.Application.Features.Notifications.DTOs;

public sealed record NotificationDto(
    long Id,
    string NotificationType,
    string Title,
    string Content,
    string? RelatedEntityType,
    long? RelatedEntityId,
    DateTime CreatedAt,
    bool IsRead,
    DateTime? ReadAt,
    string? TitleKey = null,
    IReadOnlyDictionary<string, object?>? Params = null);

public sealed record UnreadNotificationCountDto(long Count);
