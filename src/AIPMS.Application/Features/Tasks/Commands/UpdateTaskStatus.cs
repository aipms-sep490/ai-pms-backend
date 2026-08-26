using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Milestones.Abstractions;
using AIPMS.Application.Features.Tasks.Abstractions;
using AIPMS.Application.Features.Tasks.Domain;
using AIPMS.Application.Features.Tasks.DTOs;
using FluentValidation;
using MediatR;

namespace AIPMS.Application.Features.Tasks.Commands;

public sealed record UpdateTaskStatusCommand(
    long TaskId,
    string NewStatus,
    string? Reason) : IRequest<TaskDto>;

public sealed class UpdateTaskStatusCommandValidator : AbstractValidator<UpdateTaskStatusCommand>
{
    private static readonly string[] AllowedStatuses = ["TODO", "IN_PROGRESS", "BLOCKED", "IN_REVIEW", "DONE", "CANCELLED"];

    public UpdateTaskStatusCommandValidator()
    {
        RuleFor(static x => x.TaskId)
            .GreaterThan(0).WithMessage("Task ID must be greater than 0.");

        RuleFor(static x => x.NewStatus)
            .NotEmpty().WithMessage("NewStatus is required.")
            .Must(static s => AllowedStatuses.Contains(s))
            .WithMessage($"NewStatus must be one of: {string.Join(", ", AllowedStatuses)}.");

        RuleFor(static x => x.Reason)
            .MaximumLength(1000).WithMessage("Reason must not exceed 1000 characters.");

        // Blocker reason required
        RuleFor(static x => x)
            .Must(static x => x.NewStatus != "BLOCKED" || !string.IsNullOrWhiteSpace(x.Reason))
            .WithName("Reason")
            .WithMessage("A reason is required when transition status to BLOCKED.");
    }
}

public sealed class UpdateTaskStatusCommandHandler(
    ITaskRepository repository,
    IMilestoneRepository milestoneRepository,
    IProjectExecutionGuard executionGuard,
    ICurrentUser currentUser,
    IAuditTrail auditTrail)
    : IRequestHandler<UpdateTaskStatusCommand, TaskDto>
{
    public async Task<TaskDto> Handle(
        UpdateTaskStatusCommand request,
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is null)
        {
            throw new UnauthorizedException();
        }

        var actorUserId = currentUser.UserId.Value;

        // Retrieve existing task
        var task = await repository.GetByIdAsync(request.TaskId, cancellationToken)
            ?? throw new NotFoundException("Task", request.TaskId);

        // Verify project is ACTIVE (strict guard)
        await executionGuard.MustBeActiveForTaskAsync(request.TaskId, cancellationToken);

        // Retrieve milestone to get projectId
        var milestone = await milestoneRepository.GetByIdAsync(task.MilestoneId, cancellationToken)
            ?? throw new NotFoundException("Milestone", task.MilestoneId);

        var projectId = milestone.ProjectId;

        // Verify authorization: Student Leader, Assigned Supervisor, or Task Assignee
        var isLeaderOrSupervisor = await repository.IsProjectLeaderOrSupervisorAsync(projectId, actorUserId, cancellationToken);
        var isAssignee = await repository.IsTaskAssigneeAsync(request.TaskId, actorUserId, cancellationToken);

        if (!isLeaderOrSupervisor && !isAssignee)
        {
            throw new ForbiddenException("You are not authorized to update this task's status.");
        }

        // Validate state machine transition
        if (!TaskStateMachine.CanTransition(task.Status, request.NewStatus))
        {
            throw new ConflictException($"Cannot transition task from status {task.Status} to {request.NewStatus}.");
        }

        await repository.UpdateStatusAsync(
            request.TaskId,
            request.NewStatus,
            request.Reason,
            actorUserId,
            cancellationToken);

        await auditTrail.RecordAsync(
            new AuditEntry(
                actorUserId,
                "TASK_STATUS_UPDATED",
                "TASK",
                request.TaskId,
                new Dictionary<string, object?>
                {
                    ["projectId"] = projectId,
                    ["oldStatus"] = task.Status,
                    ["newStatus"] = request.NewStatus,
                    ["reason"] = request.Reason
                }),
            cancellationToken);

        return (await repository.GetByIdAsync(request.TaskId, cancellationToken))!;
    }
}
