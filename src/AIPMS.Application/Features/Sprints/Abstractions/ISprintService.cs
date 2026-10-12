using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Tasks.DTOs;

namespace AIPMS.Application.Features.Sprints.Abstractions;

public interface ISprintService
{
    Task<PagedResult<SprintDto>> List(long projectId, int page, int pageSize, CancellationToken ct);
    Task<SprintDto> Save(long projectId, long? id, SaveSprintRequest input, CancellationToken ct);
    Task<SprintDto> Start(long projectId, long id, SprintTokenRequest input, CancellationToken ct);
    Task<SprintDto> Complete(long projectId, long id, CompleteSprintRequest input, CancellationToken ct);
    Task<TaskDto> SaveTask(long taskId, SaveTaskBoardRequest input, CancellationToken ct);
    Task<PagedResult<TaskDto>> Board(long projectId, long? sprintId, bool backlog, string? label, long? majorId, string? status, int page, int pageSize, CancellationToken ct);
    Task<SprintBurndownDto> Burndown(long projectId, long id, CancellationToken ct);
}
