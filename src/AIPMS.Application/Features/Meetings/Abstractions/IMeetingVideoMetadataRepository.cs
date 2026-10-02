using System.Threading;
using System.Threading.Tasks;
namespace AIPMS.Application.Features.Meetings.Abstractions;
public interface IMeetingVideoMetadataRepository { Task SetAsync(long meetingId, string deliveryMode, string videoChannel, CancellationToken cancellationToken); }


