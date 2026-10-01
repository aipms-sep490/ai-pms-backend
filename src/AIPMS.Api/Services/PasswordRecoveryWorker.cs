using AIPMS.Application.Features.Auth.Abstractions;
using AIPMS.Infrastructure.Email;
using Microsoft.Extensions.Options;

namespace AIPMS.Api.Services;

internal sealed class PasswordRecoveryWorker(IServiceScopeFactory scopes, IOptions<PasswordRecoverySettings> options,
    TimeProvider clock, ILogger<PasswordRecoveryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled) return;
        var nextCleanup = DateTimeOffset.MinValue;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                for (var i = 0; i < settings.BatchSize; i++)
                {
                    await using var scope = scopes.CreateAsyncScope();
                    if (!await scope.ServiceProvider.GetRequiredService<IPasswordRecoveryQueue>().ProcessOneAsync(stoppingToken)) break;
                }
                if (clock.GetUtcNow() >= nextCleanup)
                {
                    await using var scope = scopes.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<IPasswordRecoveryQueue>().CleanupAsync(stoppingToken);
                    nextCleanup = clock.GetUtcNow().AddHours(1);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception) { logger.LogError("Password recovery worker failed; it will retry. Check database and key-ring availability."); }
            try { await Task.Delay(TimeSpan.FromSeconds(settings.IntervalSeconds), clock, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
