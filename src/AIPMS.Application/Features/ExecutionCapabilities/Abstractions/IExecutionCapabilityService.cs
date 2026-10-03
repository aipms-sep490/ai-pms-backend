using AIPMS.Application.Features.ExecutionCapabilities.DTOs;

namespace AIPMS.Application.Features.ExecutionCapabilities.Abstractions;

public interface IExecutionCapabilityService
{
    Task<ExecutionCapabilityDto> GetProjectAsync(long projectId, CancellationToken cancellationToken);
    Task<ExecutionCapabilityDto> GetTaskAsync(long taskId, CancellationToken cancellationToken);
    Task<ExecutionCapabilityDto> GetMilestoneAsync(long milestoneId, CancellationToken cancellationToken);
}
