using AIPMS.Application.Features.Meetings.Video;
using AIPMS.Application.Features.Meetings.Abstractions;
using AIPMS.Infrastructure.Persistence.Generated;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AIPMS.Api.Services;

internal sealed class VideoCleanupWorker(IServiceScopeFactory scopes, IOptions<VideoMeetingOptions> options, TimeProvider clock, ILogger<VideoCleanupWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (options.Value.Enabled)
            {
                try { await ProcessAsync(stoppingToken); } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; } catch (Exception) { logger.LogError("Video cleanup worker failed; it will retry."); }
            }
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }

    private async Task ProcessAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AipmsDbContext>(); var provider = scope.ServiceProvider.GetRequiredService<IVideoMeetingProvider>(); var now = clock.GetUtcNow().UtcDateTime;
        var jobs = await db.VideoProviderCleanupJobs.Where(x => (x.Status == "PENDING" || x.Status == "PROCESSING") && x.NextAttemptAt <= now && (x.LeaseUntil == null || x.LeaseUntil < now)).OrderBy(x => x.Id).Take(20).ToListAsync(ct);
        foreach (var job in jobs)
        {
            job.Status = "PROCESSING"; job.AttemptCount++; job.LeaseToken = Guid.NewGuid(); job.LeaseUntil = now.AddMinutes(2); var lease = job.LeaseToken.Value; var attempt = job.AttemptCount; await db.SaveChangesAsync(ct);
            try
            {
                await provider.CloseRoomAsync(job.ProviderRoomKey, ct);
                await db.VideoProviderCleanupJobs.Where(x => x.Id == job.Id && x.LeaseToken == lease).ExecuteUpdateAsync(x => x
                    .SetProperty(p => p.Status, "SUCCEEDED")
                    .SetProperty(p => p.CompletedAt, now)
                    .SetProperty(p => p.LeaseUntil, (DateTime?)null)
                    .SetProperty(p => p.LeaseToken, (Guid?)null), ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                var status = attempt >= 5 ? "FAILED" : "PENDING";
                var next = now.AddMinutes(Math.Min(4, Math.Pow(2, attempt - 1) / 2));
                await db.VideoProviderCleanupJobs.Where(x => x.Id == job.Id && x.LeaseToken == lease).ExecuteUpdateAsync(x => x
                    .SetProperty(p => p.Status, status)
                    .SetProperty(p => p.LastErrorCode, ex is TimeoutException ? "VIDEO_PROVIDER_TIMEOUT" : "VIDEO_PROVIDER_UNAVAILABLE")
                    .SetProperty(p => p.NextAttemptAt, next)
                    .SetProperty(p => p.LeaseUntil, (DateTime?)null)
                    .SetProperty(p => p.LeaseToken, (Guid?)null), ct);
            }
        }
    }
}


