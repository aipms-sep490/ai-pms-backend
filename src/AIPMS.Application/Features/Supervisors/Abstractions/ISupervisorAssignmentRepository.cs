using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Supervisors.Models;

namespace AIPMS.Application.Features.Supervisors.Abstractions;

public interface ISupervisorAssignmentRepository
{
    Task<T> InTransactionAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct);
    Task LockAsync(long assignmentId, CancellationToken ct);
    Task<SupervisorAssignmentModel?> GetAsync(long assignmentId, CancellationToken ct);
    Task<bool> ProjectExistsAsync(long projectId, CancellationToken ct);
    Task<bool> IsProjectDepartmentAsync(long projectId, long departmentId, CancellationToken ct);
    Task<PagedResult<SupervisorAssignmentModel>> SearchAsync(SupervisorAssignmentSearch search, CancellationToken ct);
    Task<SupervisorAssignmentModel> EndAsync(long assignmentId, DateTime now, CancellationToken ct);
}
