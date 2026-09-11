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
using MediatR;

namespace AIPMS.Application.Features.Tasks.Commands;

public sealed record CreateTaskCommand(
    long MilestoneId,
    long? ParentTaskId,
    string Title,
    string? Description,
    string? Priority,
    DateTime? StartAt,
    DateTime? DueAt,
    IReadOnlyList<long> AssigneeUserIds) : IRequest<TaskDto>;

public sealed class CreateTaskCommandHandler(
    ITaskRepository repository,
    IMilestoneRepository milestoneRepository,
    IProjectExecutionGuard executionGuard,
    ICurrentUser currentUser,
    IAuditTrail auditTrail)
    : IRequestHandler<CreateTaskCommand, TaskDto>
{
    public async Task<TaskDto> Handle(
        CreateTaskCommand request,
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is null)
        {
            throw new UnauthorizedException();
        }

        var actorUserId = currentUser.UserId.Value;

        // Retrieve milestone
        var milestone = await milestoneRepository.GetByIdAsync(request.MilestoneId, cancellationToken)
            ?? throw new NotFoundException("Milestone", request.MilestoneId);

        var projectId = milestone.ProjectId;

        // Verify project is ACTIVE (strict guard)
        await executionGuard.MustBeActiveAsync(projectId, cancellationToken);

        // Verify authorization: Student Leader or Assigned Supervisor
        if (!await repository.IsProjectLeaderOrSupervisorAsync(projectId, actorUserId, cancellationToken))
        {
            throw new ForbiddenException("You are not authorized to create tasks for this project.");
        }

        // Validate parent task (must exist and belong to the same project)
        if (request.ParentTaskId.HasValue)
        {
            var parentTask = await repository.GetByIdAsync(request.ParentTaskId.Value, cancellationToken)
                ?? throw new NotFoundException("Parent Task", request.ParentTaskId.Value);

            if (!await repository.TaskBelongsToProjectAsync(request.ParentTaskId.Value, projectId, cancellationToken))
            {
                throw new ConflictException("Parent task must belong to the same project.");
            }
        }

        // Validate all assignees belong to the project team
        foreach (var userId in request.AssigneeUserIds)
        {
            if (!await repository.IsUserActiveTeamMemberAsync(projectId, userId, cancellationToken))
            {
                throw new ConflictException($"User with ID {userId} is not an active member of this project's team.");
            }
        }

        var task = await repository.CreateAsync(
            request.MilestoneId,
            request.ParentTaskId,
            request.Title,
            request.Description,
            request.Priority,
            request.StartAt,
            request.DueAt,
            request.AssigneeUserIds,
            actorUserId,
            cancellationToken);

        await auditTrail.RecordAsync(
            new AuditEntry(
                actorUserId,
                "TASK_CREATED",
                "TASK",
                task.Id,
                new Dictionary<string, object?>
                {
                    ["projectId"] = projectId,
                    ["milestoneId"] = request.MilestoneId,
                    ["title"] = request.Title
                }),
            cancellationToken);

        return task;
    }
}
