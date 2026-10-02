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
        meeting.MeetingDeliveryMode = deliveryMode; meeting.VideoChannel = videoChannel; meeting.UpdatedAt = DateTime.UtcNow; await db.SaveChangesAsync(ct);
    }
}


