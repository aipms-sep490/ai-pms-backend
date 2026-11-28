using AIPMS.Api.Configuration;
using AIPMS.Application.Features.ReviewGates.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AIPMS.Api.Services;

public sealed class DualDeptSlaWorker(
    IServiceScopeFactory scopes,
    IOptions<DualDeptSlaSettings> options,
    TimeProvider clock,
    ILogger<DualDeptSlaWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled) return;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Dual-department SLA sweep failed; retrying at the next interval.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromMinutes(settings.IntervalMinutes), clock, stoppingToken);
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
        var sla = scope.ServiceProvider.GetRequiredService<IDualDeptSlaService>();
        var result = await sla.SweepAsync(actorUserId: null, ct);
        if (result.Reminders48hSent > 0 || result.Escalations72hSent > 0)
        {
            logger.LogInformation(
                "DualDeptSlaWorker emitted {Reminders} 2-WD reminders and {Escalations} 3-WD escalations.",
                result.Reminders48hSent, result.Escalations72hSent);
        }
    }
}
