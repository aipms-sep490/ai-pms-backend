using AIPMS.Application.Features.Meetings.Video;
using System.Threading;
using System.Threading.Tasks;

namespace AIPMS.Application.Features.Meetings.Abstractions;

public interface IMeetingVideoService
{
    Task<VideoSessionDto> GetSessionAsync(long meetingId, CancellationToken cancellationToken);
    Task<VideoSessionDto> StartAsync(long meetingId, CancellationToken cancellationToken);
    Task<VideoJoinCredentialDto> JoinAsync(long meetingId, CancellationToken cancellationToken);
    Task<VideoSessionDto> EndAsync(long meetingId, CancellationToken cancellationToken);
    Task<VideoPresenceDto> GetPresenceAsync(long meetingId, CancellationToken cancellationToken);
}




