using AIPMS.Application.Features.ActionAggregators;

namespace AIPMS.Application.Features.ActionAggregators.Abstractions;

public interface IActionAggregatorService
{
    Task<StudentNextActionsDto> StudentNextActions(CancellationToken ct);
    Task<SupervisorTriageQueueDto> SupervisorTriage(CancellationToken ct);
    Task<DepartmentUrgentActionsDto> DepartmentUrgent(long departmentId, CancellationToken ct);
}
