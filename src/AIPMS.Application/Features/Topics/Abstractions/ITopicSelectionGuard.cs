using System.Threading;
using System.Threading.Tasks;

namespace AIPMS.Application.Features.Topics.Abstractions;

public interface ITopicSelectionGuard
{
    Task ValidateTopicSelectionAsync(
        long topicId,
        long projectId,
        long actorUserId,
        CancellationToken cancellationToken);
}
