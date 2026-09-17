using AIPMS.Api.Configuration;
using AIPMS.Application.Features.Notifications.Abstractions;
using Microsoft.Extensions.Options;

namespace AIPMS.Api.Services;

internal sealed class ScheduledNotificationWorker(IServiceScopeFactory scopes,
    IOptions<ScheduledNotificationSettings> options, TimeProvider clock, ILogger<ScheduledNotificationWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await SweepAsync(settings, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "Scheduled notification sweep failed; retrying at the next interval."); }
            try { await Task.Delay(TimeSpan.FromSeconds(settings.IntervalSeconds), clock, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    internal async Task SweepAsync(ScheduledNotificationSettings settings, CancellationToken ct)
    {
        long cursor = 0;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            IReadOnlyList<long> ids;
            await using (var scope = scopes.CreateAsyncScope())
                ids = await scope.ServiceProvider.GetRequiredService<IScheduledNotificationService>()
                    .GetProjectIdsAsync(cursor, settings.BatchSize, ct);
            if (ids.Count == 0) return;
            foreach (var id in ids)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    // A failed transaction never leaves tracked changes in another project's scope.
                    await using var scope = scopes.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<IScheduledNotificationService>()
                        .ProcessProjectAsync(id, clock.GetUtcNow().UtcDateTime, TimeSpan.FromHours(settings.ReminderHours), ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception ex) { logger.LogError(ex, "Scheduled notifications failed for project {ProjectId}.", id); }
            }
            cursor = ids[^1];
        }
    }
}
