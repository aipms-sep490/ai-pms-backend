using AIPMS.Application.Common.Options;
using AIPMS.Application.Features.Notifications.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AIPMS.Api.Services;

public sealed class DepartmentDailyDigestWorker(
    IServiceScopeFactory scopes,
    IOptions<DepartmentDigestSettings> options,
    TimeProvider clock,
    ILogger<DepartmentDailyDigestWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled) return;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var utcNow = clock.GetUtcNow().UtcDateTime;
                if (utcNow.Hour == Math.Clamp(settings.HourUtc, 0, 23))
                    await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "DepartmentDailyDigestWorker sweep failed; retrying later.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromMinutes(15), clock, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    public async Task RunOnceAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var digest = scope.ServiceProvider.GetRequiredService<IDepartmentDailyDigestService>();
        var sentCount = await digest.SendDigestAsync(clock.GetUtcNow().UtcDateTime, ct);
        if (sentCount > 0)
            logger.LogInformation("DepartmentDailyDigestWorker emitted daily digest for {Count} departments.", sentCount);
    }
}
