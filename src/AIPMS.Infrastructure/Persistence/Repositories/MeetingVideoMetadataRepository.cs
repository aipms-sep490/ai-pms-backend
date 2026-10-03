using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Meetings.Abstractions;
using AIPMS.Infrastructure.Persistence.Generated;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;
namespace AIPMS.Infrastructure.Persistence.Repositories;
public sealed class MeetingVideoMetadataRepository(AipmsDbContext db) : IMeetingVideoMetadataRepository
{
    public async Task SetAsync(long meetingId, string deliveryMode, string videoChannel, CancellationToken ct)
    {
        var meeting = await db.Meetings.FirstOrDefaultAsync(x => x.Id == meetingId, ct) ?? throw new NotFoundException("Meeting", meetingId);
        if (meeting.VideoChannel != videoChannel && await db.MeetingVideoSessions.AnyAsync(x => x.MeetingId == meetingId && (x.Status == "CREATED" || x.Status == "LIVE"), ct))
            throw new ConflictException("End the active video session before changing its channel.", "VIDEO_SESSION_ALREADY_ACTIVE");
        meeting.MeetingDeliveryMode = deliveryMode; meeting.VideoChannel = videoChannel; meeting.UpdatedAt = DateTime.UtcNow; await db.SaveChangesAsync(ct);
    }
}


