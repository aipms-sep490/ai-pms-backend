using System.Threading;
using System.Threading.Tasks;

namespace AIPMS.Application.Abstractions.Security;

public interface IProjectExecutionGuard
{
    Task MustBeActiveAsync(long projectId, CancellationToken cancellationToken);
    
    Task MustBeActiveForMilestoneAsync(long milestoneId, CancellationToken cancellationToken);
    
    Task MustBeActiveForTaskAsync(long taskId, CancellationToken cancellationToken);
}
