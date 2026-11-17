using AIPMS.Application.Features.Tasks.Queries;

namespace AIPMS.Application.Features.Tasks.Abstractions;

public interface ITaskBoardSummaryReader
{
    Task<bool> ScopeBelongsToProject(long projectId, long? milestoneId, long? sprintId, CancellationToken ct);
    Task<IReadOnlyList<TaskBoardStatusCount>> CountByStatus(long projectId, long? milestoneId, long? sprintId, CancellationToken ct);
}
