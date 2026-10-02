using AIPMS.Application.Features.Meetings.Video;
using AIPMS.Application.Features.Meetings.Abstractions;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Generated.Models;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;
using AsyncTask = System.Threading.Tasks.Task;
namespace AIPMS.Infrastructure.Services.Projects;
internal sealed class VideoCleanupScheduler(AipmsDbContext db, TimeProvider clock) : IVideoCleanupScheduler
{
    public async AsyncTask EnqueueForMeetingAsync(long meetingId, CancellationToken ct)
    {
        var sessions = await db.MeetingVideoSessions.Where(x => x.MeetingId == meetingId && (x.Status == "CREATED" || x.Status == "LIVE")).ToListAsync(ct);
        foreach (var session in sessions)
        {
            if (await db.VideoProviderCleanupJobs.AnyAsync(x => x.MeetingVideoSessionId == session.Id && (x.Status == "PENDING" || x.Status == "PROCESSING"), ct)) continue;
            db.VideoProviderCleanupJobs.Add(new VideoProviderCleanupJob { MeetingVideoSessionId = session.Id, ProviderRoomKey = session.ProviderRoomKey, Status = "PENDING", NextAttemptAt = clock.GetUtcNow().UtcDateTime, CreatedAt = clock.GetUtcNow().UtcDateTime });
        }
        await db.SaveChangesAsync(ct);
    }
}


