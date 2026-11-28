using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Milestones.Abstractions;
using AIPMS.Application.Features.Tasks.Abstractions;
using AIPMS.Application.Features.Tasks.DTOs;
using MediatR;

namespace AIPMS.Application.Features.Tasks.Queries;

public sealed record GetTaskByIdQuery(long Id) : IRequest<TaskDto>;

public sealed class GetTaskByIdQueryHandler(
    ITaskRepository repository,
    IMilestoneRepository milestoneRepository,
    IProjectAccessService projectAccessService,
    ICurrentUser currentUser,
    IProjectScopeResolver? scopeResolver = null)
    : IRequestHandler<GetTaskByIdQuery, TaskDto>
{
    public async Task<TaskDto> Handle(
        GetTaskByIdQuery request,
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is null)
        {
            throw new UnauthorizedException();
        }

        var actorUserId = currentUser.UserId.Value;

        var task = await repository.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new NotFoundException("Task", request.Id);

        var milestone = await milestoneRepository.GetByIdAsync(task.MilestoneId, cancellationToken)
            ?? throw new NotFoundException("Milestone", task.MilestoneId);

        // Verify project access
        if (!await projectAccessService.CanAccessAsync(actorUserId, milestone.ProjectId, cancellationToken))
        {
            throw new ForbiddenException("You do not have access to this project.");
        }

        // TK-W2-05: Enforce Discipline Mentor major scope isolation on direct task reads.
        if (scopeResolver is not null)
        {
            var actorScope = await scopeResolver.ResolveAsync(actorUserId, milestone.ProjectId, cancellationToken);
            if (actorScope is { IsFullProjectAccess: false, IsMentor: true })
            {
                var taskMajorIds = await repository.GetTaskMajorIdsAsync(request.Id, cancellationToken);
                if (taskMajorIds.Count > 0 && !taskMajorIds.Any(id => actorScope.CanAccessMajor(id)))
                {
                    throw new ForbiddenException("This task is outside your assigned mentor major scope.", WorkflowErrorCodes.ScopeAccessDenied);
                }
            }
        }

        return task;
    }
}
