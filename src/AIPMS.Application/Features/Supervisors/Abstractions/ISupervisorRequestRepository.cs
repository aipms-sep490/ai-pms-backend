using System.Threading;
using System.Threading.Tasks;
using AIPMS.Domain.Entities;

namespace AIPMS.Application.Features.Supervisors.Abstractions;

public interface ISupervisorRequestRepository
{
    Task AddAsync(SupervisorRequest request, CancellationToken cancellationToken);
    Task<SupervisorRequest?> GetByIdAsync(long id, CancellationToken cancellationToken);
    Task<bool> HasPendingRequestAsync(long projectId, long supervisorProfileId, CancellationToken cancellationToken);
    Task<bool> ProjectExistsAsync(long projectId, CancellationToken cancellationToken);
    Task<bool> IsProjectApprovedAsync(long projectId, CancellationToken cancellationToken);
    Task ActivateProjectAsync(long projectId, CancellationToken cancellationToken);
    Task UpdateAsync(SupervisorRequest request, CancellationToken cancellationToken);
}
