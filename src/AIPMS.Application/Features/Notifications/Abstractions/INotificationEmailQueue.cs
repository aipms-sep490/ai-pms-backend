namespace AIPMS.Application.Features.Notifications.Abstractions;

public sealed record NotificationEmailDelivery(
    long RecipientId,
    int AttemptCount,
    string Email,
    string RecipientName,
    string Subject,
    string Body,
    bool ShouldSend = true);

public interface INotificationEmailQueue
{
    Task<NotificationEmailDelivery?> ClaimNextAsync(DateTime nowUtc, CancellationToken ct, string? notificationType = null);
    Task MarkSentAsync(long recipientId, DateTime nowUtc, CancellationToken ct, int? expectedAttempt = null);
    Task MarkFailedAsync(long recipientId, DateTime nextAttemptAtUtc, string error, bool permanent, CancellationToken ct, int? expectedAttempt = null);
}

public interface INotificationEmailSender
{
    Task<bool> TrySendAsync(NotificationEmailDelivery delivery, CancellationToken ct);
}
