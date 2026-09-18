namespace AIPMS.Application.Features.Notifications.Abstractions;

public sealed record NotificationEmailDelivery(
    long RecipientId,
    int AttemptCount,
    string Email,
    string RecipientName,
    string Subject,
    string Body);

public interface INotificationEmailQueue
{
    Task<NotificationEmailDelivery?> ClaimNextAsync(DateTime nowUtc, CancellationToken ct);
    Task MarkSentAsync(long recipientId, DateTime nowUtc, CancellationToken ct);
    Task MarkFailedAsync(long recipientId, DateTime nextAttemptAtUtc, string error, bool permanent, CancellationToken ct);
}

public interface INotificationEmailSender
{
    Task<bool> TrySendAsync(NotificationEmailDelivery delivery, CancellationToken ct);
}
