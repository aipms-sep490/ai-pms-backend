using System.Threading.Tasks;
using AIPMS.Application.Features.Meetings.Abstractions;
using AIPMS.Infrastructure.Persistence.Generated;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Services.Projects;

internal sealed class VideoCleanupScheduler(AipmsDbContext db, TimeProvider clock) : IVideoCleanupScheduler
{
    // Caller owns the Meeting transaction. A provider call is never made here.
    public async Task EnqueueForMeetingAsync(long meetingId, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var sessions = await db.MeetingVideoSessions.Where(x => x.MeetingId == meetingId).ToListAsync(ct);
        foreach (var session in sessions)
        {
            if (session.Status is "CREATED" or "LIVE")
            {
                session.Status = "ENDED";
                session.EndedAt = now;
                session.UpdatedAt = now;
                session.ConcurrencyToken = Guid.NewGuid();
            }
            var job = await db.VideoProviderCleanupJobs.SingleOrDefaultAsync(x => x.MeetingVideoSessionId == session.Id, ct);
            if (job is null)
            {
                // A CreateRoom request may still be in flight; defer closing until its 15s timeout has elapsed.
                db.VideoProviderCleanupJobs.Add(new() { MeetingVideoSessionId = session.Id, ProviderRoomKey = session.ProviderRoomKey,
                    Status = "PENDING", NextAttemptAt = session.CreatedAt.AddSeconds(30) > now ? session.CreatedAt.AddSeconds(30) : now, CreatedAt = now });
            }
        }
        await db.SaveChangesAsync(ct);
    }
}
