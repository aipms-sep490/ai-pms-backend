using System.Threading;
using System.Threading.Tasks;
using AIPMS.Domain.Entities;

namespace AIPMS.Application.Features.Supervisors.Abstractions;

public interface ISupervisorAssignmentRepository
{
    Task AddAsync(SupervisorAssignment assignment, CancellationToken cancellationToken);
    Task<int> CountActiveAssignmentsAsync(long supervisorProfileId, CancellationToken cancellationToken);
    Task<SupervisorAssignment?> GetActiveAssignmentByProjectAsync(long projectId, CancellationToken cancellationToken);
    Task<SupervisorAssignment?> GetByIdAsync(long id, CancellationToken cancellationToken);
    Task UpdateAsync(SupervisorAssignment assignment, CancellationToken cancellationToken);
}
