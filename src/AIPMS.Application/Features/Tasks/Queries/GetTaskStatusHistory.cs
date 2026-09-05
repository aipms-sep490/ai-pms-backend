using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Milestones.Abstractions;
using AIPMS.Application.Features.Tasks.Abstractions;
using AIPMS.Application.Features.Tasks.DTOs;
using FluentValidation;
using MediatR;

namespace AIPMS.Application.Features.Tasks.Queries;

public sealed record GetTaskStatusHistoryQuery(long TaskId) : IRequest<IReadOnlyList<TaskStatusHistoryDto>>;

public sealed class GetTaskStatusHistoryQueryValidator : AbstractValidator<GetTaskStatusHistoryQuery>
{
    public GetTaskStatusHistoryQueryValidator()
    {
        RuleFor(static x => x.TaskId)
            .GreaterThan(0).WithMessage("Task ID must be greater than 0.");
    }
}

public sealed class GetTaskStatusHistoryQueryHandler(
    ITaskRepository repository,
    IMilestoneRepository milestoneRepository,
    IProjectAccessService projectAccessService,
    ICurrentUser currentUser)
    : IRequestHandler<GetTaskStatusHistoryQuery, IReadOnlyList<TaskStatusHistoryDto>>
{
    public async Task<IReadOnlyList<TaskStatusHistoryDto>> Handle(
        GetTaskStatusHistoryQuery request,
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is null)
        {
            throw new UnauthorizedException();
        }

        var actorUserId = currentUser.UserId.Value;

        var task = await repository.GetByIdAsync(request.TaskId, cancellationToken)
            ?? throw new NotFoundException("Task", request.TaskId);

        var milestone = await milestoneRepository.GetByIdAsync(task.MilestoneId, cancellationToken)
            ?? throw new NotFoundException("Milestone", task.MilestoneId);

        var projectId = milestone.ProjectId;

        // Verify project access
        if (!await projectAccessService.CanAccessAsync(actorUserId, projectId, cancellationToken))
        {
            throw new ForbiddenException("You do not have access to this project.");
        }

        return await repository.GetStatusHistoryAsync(request.TaskId, cancellationToken);
    }
}
