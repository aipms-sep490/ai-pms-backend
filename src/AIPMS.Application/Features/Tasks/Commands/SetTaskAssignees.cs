using System;
using System.Collections.Generic;
using System.Linq;
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

public sealed record SetTaskAssigneesCommand(
    long TaskId,
    IReadOnlyList<long> AssigneeUserIds) : IRequest<TaskDto>;

public sealed class SetTaskAssigneesCommandValidator : AbstractValidator<SetTaskAssigneesCommand>
{
    public SetTaskAssigneesCommandValidator()
    {
        RuleFor(static x => x.TaskId)
            .GreaterThan(0).WithMessage("Task ID must be greater than 0.");

        RuleFor(static x => x.AssigneeUserIds)
            .NotNull().WithMessage("AssigneeUserIds cannot be null.")
            .Must(static ids => ids == null || ids.Distinct().Count() == ids.Count)
            .WithMessage("AssigneeUserIds must not contain duplicate values.");
    }
}

public sealed class SetTaskAssigneesCommandHandler(
    ITaskRepository repository,
    IMilestoneRepository milestoneRepository,
    IProjectExecutionGuard executionGuard,
    ICurrentUser currentUser,
    IAuditTrail auditTrail)
    : IRequestHandler<SetTaskAssigneesCommand, TaskDto>
{
    public async Task<TaskDto> Handle(
        SetTaskAssigneesCommand request,
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

        // Retrieve milestone
        var milestone = await milestoneRepository.GetByIdAsync(task.MilestoneId, cancellationToken)
            ?? throw new NotFoundException("Milestone", task.MilestoneId);

        var projectId = milestone.ProjectId;

        // Verify authorization: Student Leader or Assigned Supervisor
        if (!await repository.IsProjectLeaderOrSupervisorAsync(projectId, actorUserId, cancellationToken))
        {
            throw new ForbiddenException("You are not authorized to manage assignees for this project.");
        }

        // Validate all assignees belong to the project team
        foreach (var userId in request.AssigneeUserIds)
        {
            if (!await repository.IsUserActiveTeamMemberAsync(projectId, userId, cancellationToken))
            {
                throw new ConflictException($"User with ID {userId} is not an active member of this project's team.");
            }
        }

        await repository.SetAssigneesAsync(
            request.TaskId,
            request.AssigneeUserIds,
            actorUserId,
            cancellationToken);

        await auditTrail.RecordAsync(
            new AuditEntry(
                actorUserId,
                "TASK_ASSIGNEES_UPDATED",
                "TASK",
                request.TaskId,
                new Dictionary<string, object?>
                {
                    ["projectId"] = projectId,
                    ["milestoneId"] = task.MilestoneId,
                    ["assigneeCount"] = request.AssigneeUserIds.Count
                }),
            cancellationToken);

        return (await repository.GetByIdAsync(request.TaskId, cancellationToken))!;
    }
}
