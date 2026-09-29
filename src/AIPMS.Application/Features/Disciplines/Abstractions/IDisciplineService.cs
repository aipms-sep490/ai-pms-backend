using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Disciplines.DTOs;
using AIPMS.Application.Features.Tasks.DTOs;

namespace AIPMS.Application.Features.Disciplines.Abstractions;

public interface IDisciplineService
{
    Task<ResponsibilityListDto> TeamResponsibilitiesAsync(long teamId, long majorId, CancellationToken ct);
    Task<ResponsibilityListDto> ProjectResponsibilitiesAsync(long projectId, long majorId, CancellationToken ct);
    Task<ResponsibilityListDto> ReplaceResponsibilitiesAsync(long teamId, long majorId, ReplaceResponsibilitiesRequest request, CancellationToken ct);
    Task<TaskDisciplinesDto> TaskDisciplinesAsync(long taskId, CancellationToken ct);
    Task<TaskDisciplinesDto> ReplaceTaskDisciplinesAsync(long taskId, ReplaceTaskDisciplinesRequest request, CancellationToken ct);
    Task<TaskDto> CreateTaskAsync(long milestoneId, IReadOnlyList<TaskDisciplineInput>? disciplines,
        Func<CancellationToken, Task<TaskDto>> create, CancellationToken ct);
    Task<ProjectEvidenceDto> AddEvidenceAsync(long projectId, CreateProjectEvidenceRequest request, CancellationToken ct);
    Task<PagedResult<ProjectEvidenceDto>> EvidenceAsync(long projectId, string? sourceType, long? majorId,
        string? verificationStatus, int page, int pageSize, CancellationToken ct);
}
