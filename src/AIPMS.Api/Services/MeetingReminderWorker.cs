using AIPMS.Api.Configuration;
using AIPMS.Application.Features.Notifications.Abstractions;
using Microsoft.Extensions.Options;

namespace AIPMS.Api.Services;

internal sealed class MeetingReminderWorker(IServiceScopeFactory scopes, IOptions<MeetingReminderSettings> options,
    TimeProvider clock, ILogger<MeetingReminderWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var settings = options.Value;
        if (!settings.Enabled) return;
        while (!ct.IsCancellationRequested)
        {
            try { await SweepAsync(settings, ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "Meeting reminder sweep failed; retrying next interval."); }
            try { await Task.Delay(TimeSpan.FromSeconds(settings.IntervalSeconds), clock, ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
        }
    }

    internal async Task SweepAsync(MeetingReminderSettings settings, CancellationToken ct)
    {
        long cursor = 0;
        while (true)
        {
            IReadOnlyList<long> ids;
            await using (var scope = scopes.CreateAsyncScope())
                ids = await scope.ServiceProvider.GetRequiredService<IMeetingReminderService>()
                    .GetDueMeetingIdsAsync(cursor, settings.BatchSize, clock.GetUtcNow().UtcDateTime, settings.MinutesBefore, ct);
            if (ids.Count == 0) break;
            foreach (var id in ids)
            {
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<IMeetingReminderService>()
                        .EnqueueAsync(id, clock.GetUtcNow().UtcDateTime, settings.MinutesBefore, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception ex) { logger.LogError(ex, "Meeting reminder failed for meeting {MeetingId}.", id); }
            }
            cursor = ids[^1];
        }
        // This opt-in worker sends only meeting reminders, never unrelated notification backlog.
        for (var i = 0; i < settings.BatchSize; i++)
        {
            await using var scope = scopes.CreateAsyncScope();
            var queue = scope.ServiceProvider.GetRequiredService<INotificationEmailQueue>();
            var delivery = await queue.ClaimNextAsync(clock.GetUtcNow().UtcDateTime, ct, IMeetingReminderService.NotificationType);
            if (delivery is null) break;
            if (!delivery.ShouldSend) continue;
            var sent = await scope.ServiceProvider.GetRequiredService<INotificationEmailSender>().TrySendAsync(delivery, ct);
            if (sent) await queue.MarkSentAsync(delivery.RecipientId, clock.GetUtcNow().UtcDateTime, ct, delivery.AttemptCount);
            else await queue.MarkFailedAsync(delivery.RecipientId,
                clock.GetUtcNow().UtcDateTime.AddSeconds(Math.Min(240, 30 * Math.Pow(2, delivery.AttemptCount - 1))),
                "Meeting reminder SMTP attempt failed or timed out.", delivery.AttemptCount >= settings.MaxAttempts, ct, delivery.AttemptCount);
        }
    }
}
