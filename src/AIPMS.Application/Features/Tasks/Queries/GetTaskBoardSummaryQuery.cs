using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Tasks.Abstractions;
using MediatR;

namespace AIPMS.Application.Features.Tasks.Queries;

public sealed record TaskBoardStatusCount(string Status, long Count);
public sealed record GetTaskBoardSummaryQuery(long ProjectId, long? MilestoneId, long? SprintId)
    : IRequest<IReadOnlyList<TaskBoardStatusCount>>;

public sealed class GetTaskBoardSummaryQueryHandler(
    ITaskBoardSummaryReader reader, IProjectAccessService access, ICurrentUser current)
    : IRequestHandler<GetTaskBoardSummaryQuery, IReadOnlyList<TaskBoardStatusCount>>
{
    public async Task<IReadOnlyList<TaskBoardStatusCount>> Handle(GetTaskBoardSummaryQuery request, CancellationToken ct)
    {
        if (!current.IsAuthenticated || current.UserId is null) throw new UnauthorizedException();
        if (request.ProjectId <= 0 || request.MilestoneId is <= 0 || request.SprintId is <= 0)
            throw new ValidationException(new Dictionary<string, string[]> { ["scope"] = ["Positive identifiers are required."] });
        if (!await access.CanAccessAsync(current.UserId.Value, request.ProjectId, ct)) throw new ForbiddenException();
        if (!await reader.ScopeBelongsToProject(request.ProjectId, request.MilestoneId, request.SprintId, ct))
            throw new ForbiddenException("The filter does not belong to this project.");
        return await reader.CountByStatus(request.ProjectId, request.MilestoneId, request.SprintId, ct);
    }
}
