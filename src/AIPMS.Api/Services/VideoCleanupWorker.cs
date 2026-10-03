using AIPMS.Application.Features.Meetings.Video;
using AIPMS.Infrastructure.Services.Projects;
using Microsoft.Extensions.Options;

namespace AIPMS.Api.Services;

internal sealed class VideoCleanupWorker(IServiceScopeFactory scopes, IOptions<VideoMeetingOptions> options, ILogger<VideoCleanupWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (options.Value.Enabled)
                {
                    await using var scope = scopes.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<VideoCleanupProcessor>().ProcessAsync(stoppingToken);
                }
                if (!await timer.WaitForNextTickAsync(stoppingToken)) break;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception) { logger.LogError("Video cleanup failed; durable jobs will be retried."); await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); }
        }
    }
}
