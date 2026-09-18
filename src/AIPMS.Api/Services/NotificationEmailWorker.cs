using AIPMS.Api.Configuration;
using AIPMS.Application.Features.Notifications.Abstractions;
using Microsoft.Extensions.Options;

namespace AIPMS.Api.Services;

internal sealed class NotificationEmailWorker(IServiceScopeFactory scopes,
    IOptions<NotificationEmailSettings> options, TimeProvider clock, ILogger<NotificationEmailWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                for (var i = 0; i < settings.BatchSize; i++)
                {
                    await using var scope = scopes.CreateAsyncScope();
                    var queue = scope.ServiceProvider.GetRequiredService<INotificationEmailQueue>();
                    var delivery = await queue.ClaimNextAsync(clock.GetUtcNow().UtcDateTime, stoppingToken);
                    if (delivery is null) break;
                    var sent = await scope.ServiceProvider.GetRequiredService<INotificationEmailSender>()
                        .TrySendAsync(delivery, stoppingToken);
                    if (sent) await queue.MarkSentAsync(delivery.RecipientId, clock.GetUtcNow().UtcDateTime, stoppingToken);
                    else
                    {
                        var permanent = delivery.AttemptCount >= settings.MaxAttempts;
                        var delayMinutes = Math.Min(60, Math.Pow(2, Math.Min(6, delivery.AttemptCount)));
                        var retryAt = clock.GetUtcNow().UtcDateTime.AddMinutes(delayMinutes);
                        await queue.MarkFailedAsync(delivery.RecipientId, retryAt,
                            permanent ? "SMTP delivery failed after the maximum attempts." : "SMTP delivery failed or is not configured.",
                            permanent, stoppingToken);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "Notification email sweep failed; retrying later."); }
            try { await Task.Delay(TimeSpan.FromSeconds(settings.IntervalSeconds), clock, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
