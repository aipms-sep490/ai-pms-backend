using AIPMS.Application.Features.Tasks.DTOs;

namespace AIPMS.Application.Features.Tasks.Abstractions;

public interface ITaskCriteriaService
{
    Task<TaskCriteriaListDto> ListAsync(long taskId, CancellationToken ct = default);
    Task<TaskCriteriaListDto> ReplaceAsync(long taskId, ReplaceTaskCriteriaRequest request, CancellationToken ct = default);
    Task<TaskCriteriaListDto> ToggleAsync(long taskId, long criterionId, ToggleTaskCriterionRequest request, CancellationToken ct = default);
}
