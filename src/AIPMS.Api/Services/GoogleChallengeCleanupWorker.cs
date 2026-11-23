using AIPMS.Application.Features.Auth.Abstractions;
using AIPMS.Infrastructure.Identity.Configuration;
using Microsoft.Extensions.Options;

namespace AIPMS.Api.Services;

internal sealed class GoogleChallengeCleanupWorker(IServiceScopeFactory scopes, IOptions<GoogleAuthSettings> settings,
    ILogger<GoogleChallengeCleanupWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!settings.Value.Enabled) return;
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<IGoogleAuthService>().CleanupAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception) { logger.LogWarning("Google challenge cleanup failed; retrying at the next interval."); }
        }
    }
}
