using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Milestones.Abstractions;
using AIPMS.Application.Features.Tasks.Abstractions;
using AIPMS.Application.Features.Tasks.DTOs;
using FluentValidation;
using MediatR;

namespace AIPMS.Application.Features.Tasks.Commands;

public sealed record RemoveTaskDependencyCommand(
    long TaskId,
    long DependsOnTaskId) : IRequest<TaskDto>;

public sealed class RemoveTaskDependencyCommandValidator : AbstractValidator<RemoveTaskDependencyCommand>
{
    public RemoveTaskDependencyCommandValidator()
    {
        RuleFor(static x => x.TaskId)
            .GreaterThan(0).WithMessage("TaskId must be greater than 0.");

        RuleFor(static x => x.DependsOnTaskId)
            .GreaterThan(0).WithMessage("DependsOnTaskId must be greater than 0.");
    }
}

public sealed class RemoveTaskDependencyCommandHandler(
    ITaskRepository repository,
    IMilestoneRepository milestoneRepository,
    IProjectExecutionGuard executionGuard,
    ICurrentUser currentUser,
    IAuditTrail auditTrail)
    : IRequestHandler<RemoveTaskDependencyCommand, TaskDto>
{
    public async Task<TaskDto> Handle(
        RemoveTaskDependencyCommand request,
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is null)
        {
            throw new UnauthorizedException();
        }

        var actorUserId = currentUser.UserId.Value;

        // Retrieve task
        var task = await repository.GetByIdAsync(request.TaskId, cancellationToken)
            ?? throw new NotFoundException("Task", request.TaskId);

        // Verify project is ACTIVE (strict guard)
        await executionGuard.MustBeActiveForTaskAsync(request.TaskId, cancellationToken);

        // Retrieve milestone to get projectId
        var milestone = await milestoneRepository.GetByIdAsync(task.MilestoneId, cancellationToken)
            ?? throw new NotFoundException("Milestone", task.MilestoneId);

        var projectId = milestone.ProjectId;

        // Verify authorization: Student Leader or Assigned Supervisor
        if (!await repository.IsProjectLeaderOrSupervisorAsync(projectId, actorUserId, cancellationToken))
        {
            throw new ForbiddenException("You are not authorized to manage task dependencies for this project.");
        }

        await repository.RemoveDependencyAsync(
            request.TaskId,
            request.DependsOnTaskId,
            cancellationToken);

        await auditTrail.RecordAsync(
            new AuditEntry(
                actorUserId,
                "TASK_DEPENDENCY_REMOVED",
                "TASK",
                request.TaskId,
                new Dictionary<string, object?>
                {
                    ["projectId"] = projectId,
                    ["dependsOnTaskId"] = request.DependsOnTaskId
                }),
            cancellationToken);

        return (await repository.GetByIdAsync(request.TaskId, cancellationToken))!;
    }
}
