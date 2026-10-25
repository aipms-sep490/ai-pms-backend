using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Features.Projects.Abstractions;
using AIPMS.Application.Features.Projects.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AIPMS.Api.Services;

public sealed class RiskAlertWorker(
    IServiceScopeFactory scopes,
    IOptions<RiskAlertSettings> options,
    TimeProvider clock,
    ILogger<RiskAlertWorker> logger)
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
                await SweepAsync(settings, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Risk alert sweep failed; retrying at the next interval.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(settings.IntervalSeconds), clock, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    public async Task SweepAsync(RiskAlertSettings settings, CancellationToken ct)
    {
        long cursor = 0;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            IReadOnlyList<long> ids;

            await using (var scope = scopes.CreateAsyncScope())
            {
                ids = await scope.ServiceProvider.GetRequiredService<IRiskAlertService>()
                    .GetProjectIdsAsync(cursor, settings.BatchSize, ct);
            }

            if (ids.Count == 0) return;

            foreach (var id in ids)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<IRiskAlertService>()
                        .ProcessProjectAsync(id, clock.GetUtcNow().UtcDateTime, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Risk alert processing failed for project {ProjectId}.", id);
                }
            }

            cursor = ids[^1];
        }
    }
}
