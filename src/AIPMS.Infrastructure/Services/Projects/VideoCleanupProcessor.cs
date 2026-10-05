using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Meetings.Abstractions;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Generated.Models;
using AIPMS.Infrastructure.Video;
using Microsoft.EntityFrameworkCore;
using Task = System.Threading.Tasks.Task;

namespace AIPMS.Infrastructure.Services.Projects;

public sealed class VideoCleanupProcessor(AipmsDbContext db, IVideoMeetingProvider provider, TimeProvider clock, IAuditTrail audit)
{
    public async Task ProcessAsync(CancellationToken ct)
    {
        await RecoverAbandonedSessions(ct);
        for (var i = 0; i < 20; i++)
        {
            var job = await Claim(ct);
            if (job is null) break;
            string? error = null;
            var permanent = false;
            try { await provider.CloseRoomAsync(job.ProviderRoomKey, ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (VideoProviderPermanentException) { error = "VIDEO_PROVIDER_REJECTED"; permanent = true; }
            catch (Exception ex) when (ex is ServiceUnavailableException or HttpRequestException or OperationCanceledException)
            { error = ex is ServiceUnavailableException { Code: not null } unavailable ? unavailable.Code : "VIDEO_PROVIDER_UNAVAILABLE"; }
            await Finish(job, error, permanent, ct);
        }
    }

    private async Task RecoverAbandonedSessions(CancellationToken ct)
    {
        var cutoff = clock.GetUtcNow().UtcDateTime.AddMinutes(-2);
        var ids = await db.MeetingVideoSessions.AsNoTracking().Where(x =>
                (x.Status == "CREATED" && x.StartedAt == null && x.CreatedAt < cutoff)
                || ((x.Meeting.Status != "SCHEDULED" || x.Meeting.Project.Status != "ACTIVE" || x.Status == "FAILED")
                    && !db.VideoProviderCleanupJobs.Any(j => j.MeetingVideoSessionId == x.Id)))
            .OrderBy(x => x.Id).Take(20).Select(x => x.Id).ToListAsync(ct);
        foreach (var id in ids)
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var row = await db.MeetingVideoSessions.FromSqlInterpolated($"SELECT * FROM dbo.meeting_video_sessions WITH (UPDLOCK,HOLDLOCK) WHERE id={id}").SingleAsync(ct);
            if (row.Status == "CREATED" && row.StartedAt != null) { await tx.CommitAsync(ct); continue; }
            if (row.Status is "CREATED" or "LIVE")
            {
                row.Status = "FAILED"; row.FailureCode = "VIDEO_SESSION_ABANDONED"; row.UpdatedAt = clock.GetUtcNow().UtcDateTime; row.ConcurrencyToken = Guid.NewGuid();
            }
            if (!await db.VideoProviderCleanupJobs.AnyAsync(x => x.MeetingVideoSessionId == id, ct))
                db.VideoProviderCleanupJobs.Add(new() { MeetingVideoSessionId = id, ProviderRoomKey = row.ProviderRoomKey,
                    Status = "PENDING", NextAttemptAt = clock.GetUtcNow().UtcDateTime, CreatedAt = clock.GetUtcNow().UtcDateTime });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            db.ChangeTracker.Clear();
        }
    }

    private async Task<VideoProviderCleanupJob?> Claim(CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var job = await db.VideoProviderCleanupJobs.FromSqlInterpolated($"SELECT TOP(1) * FROM dbo.video_provider_cleanup_jobs WITH (UPDLOCK,READPAST,ROWLOCK) WHERE status IN ('PENDING','PROCESSING') AND next_attempt_at<={now} AND (lease_until IS NULL OR lease_until<={now}) ORDER BY id").SingleOrDefaultAsync(ct);
        if (job is null) { await tx.CommitAsync(ct); return null; }
        job.Status = "PROCESSING"; job.AttemptCount++; job.LeaseToken = Guid.NewGuid(); job.LeaseUntil = now.AddMinutes(2);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        db.Entry(job).State = EntityState.Detached;
        return job;
    }

    private async Task Finish(VideoProviderCleanupJob job, string? error, bool permanent, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var status = error is null ? "SUCCEEDED" : permanent || job.AttemptCount >= 5 ? "FAILED" : "PENDING";
        var retry = now.AddSeconds(Math.Min(240, 30 * Math.Pow(2, job.AttemptCount - 1)));
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // Match lifecycle/webhook ordering: session before cleanup job and presence.
        var session = await db.MeetingVideoSessions.FromSqlInterpolated($"SELECT * FROM dbo.meeting_video_sessions WITH (UPDLOCK,HOLDLOCK) WHERE id={job.MeetingVideoSessionId}")
            .AsNoTracking().SingleAsync(ct);
        var cutoff = session.EndedAt ?? (session.Status is "FAILED" or "ENDED" ? session.UpdatedAt : now);
        var changed = await db.VideoProviderCleanupJobs.Where(x => x.Id == job.Id && x.Status == "PROCESSING" && x.LeaseToken == job.LeaseToken && x.LeaseUntil > now)
            .ExecuteUpdateAsync(x => x.SetProperty(p => p.Status, status).SetProperty(p => p.LastErrorCode, error)
                .SetProperty(p => p.NextAttemptAt, retry).SetProperty(p => p.CompletedAt, status == "PENDING" ? (DateTime?)null : now)
                .SetProperty(p => p.LeaseToken, (Guid?)null).SetProperty(p => p.LeaseUntil, (DateTime?)null), ct);
        if (changed == 1)
        {
            if (error is null)
                await db.MeetingVideoPresenceSessions.Where(x => x.MeetingVideoSessionId == job.MeetingVideoSessionId
                        && (x.LeftAt == null || x.LeftAt > cutoff))
                    .ExecuteUpdateAsync(x => x.SetProperty(p => p.LeftAt, p => p.JoinedAt > cutoff ? p.JoinedAt : cutoff)
                        .SetProperty(p => p.DisconnectReason, "provider_cleanup").SetProperty(p => p.UpdatedAt, now), ct);
            else await audit.RecordAsync(new(null, "VIDEO_CLEANUP_FAILED", "MEETING_VIDEO_SESSION", job.MeetingVideoSessionId,
                new Dictionary<string, object?> { ["errorCode"] = error, ["attempt"] = job.AttemptCount, ["status"] = status }), ct);
        }
        await tx.CommitAsync(ct);
    }
}
