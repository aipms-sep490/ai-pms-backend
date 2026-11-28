namespace AIPMS.Application.Features.Notifications.DTOs;

public sealed record EmailDeliveryFailureDto(
    long NotificationRecipientId,
    long NotificationId,
    string NotificationType,
    string RecipientEmail,
    string Status,
    int AttemptCount,
    DateTime NextAttemptAtUtc,
    DateTime? LastAttemptAtUtc,
    string? LastError);

public sealed record EmailDeliverySummaryDto(
    int PendingCount,
    int RetryPendingCount,
    int SendingCount,
    int SentCount,
    int FailedCount,
    DateTime? OldestPendingAtUtc,
    IReadOnlyList<EmailDeliveryFailureDto> RecentFailures);

public sealed record RetryFailedEmailsResultDto(
    int RequeuedCount,
    DateTime RetriedAtUtc);
