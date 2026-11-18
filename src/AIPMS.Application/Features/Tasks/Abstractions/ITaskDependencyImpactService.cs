using AIPMS.Application.Features.Tasks.DTOs;

namespace AIPMS.Application.Features.Tasks.Abstractions;

public interface ITaskDependencyImpactService
{
    Task<TaskDependencyImpactsDto> GetAsync(long taskId, CancellationToken ct);
    Task<TaskDto> ResolveAsync(long taskId, long impactId, string decision, string reason, CancellationToken ct);
}
