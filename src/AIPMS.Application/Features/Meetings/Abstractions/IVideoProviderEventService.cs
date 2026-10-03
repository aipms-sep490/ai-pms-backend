using AIPMS.Application.Features.Meetings.Video;
using System.Threading;
using System.Threading.Tasks;

namespace AIPMS.Application.Features.Meetings.Abstractions;

public interface IVideoProviderEventService
{
    Task ProcessLiveKitAsync(string rawBody, string? authorization, CancellationToken cancellationToken);
}




