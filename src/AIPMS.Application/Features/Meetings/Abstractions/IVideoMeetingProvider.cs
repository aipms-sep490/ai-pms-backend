using AIPMS.Application.Features.Meetings.Video;
using System.Threading;
using System.Threading.Tasks;

namespace AIPMS.Application.Features.Meetings.Abstractions;

public interface IVideoMeetingProvider
{
    string Provider { get; }
    Task CreateRoomAsync(VideoProviderRoomRequest request, CancellationToken cancellationToken);
    Task<VideoJoinCredentialDto> CreateJoinCredentialAsync(VideoProviderJoinRequest request, CancellationToken cancellationToken);
    Task CloseRoomAsync(string roomKey, CancellationToken cancellationToken);
    Task<VideoProviderRoomSnapshot?> GetRoomAsync(string roomKey, CancellationToken cancellationToken);
}




