using AIPMS.Application.Features.Meetings.Video;
using System.Threading;
using System.Threading.Tasks;
namespace AIPMS.Application.Features.Meetings.Abstractions;
public interface IVideoCleanupScheduler { Task EnqueueForMeetingAsync(long meetingId, CancellationToken cancellationToken); }




