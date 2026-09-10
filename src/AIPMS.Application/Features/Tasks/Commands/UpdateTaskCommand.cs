using System;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Milestones.Abstractions;
using AIPMS.Application.Features.Tasks.Abstractions;
using AIPMS.Application.Features.Tasks.DTOs;
using AIPMS.Domain.Tasks;
using MediatR;

namespace AIPMS.Application.Features.Tasks.Commands;

public sealed record UpdateTaskCommand(
    long Id,
    long MilestoneId,
    long? ParentTaskId,
    string Title,
    string? Description,
    string? Priority,
    DateTime? StartAt,
    DateTime? DueAt) : IRequest<TaskDto>;

public sealed class UpdateTaskCommandHandler(
    ITaskRepository repository,
    IMilestoneRepository milestoneRepository,
    IProjectExecutionGuard executionGuard,
    ICurrentUser currentUser,
    IAuditTrail auditTrail)
    : IRequestHandler<UpdateTaskCommand, TaskDto>
{
    public async Task<TaskDto> Handle(
        UpdateTaskCommand request,
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is null)
        {
            throw new UnauthorizedException();
        }

        var actorUserId = currentUser.UserId.Value;

        // Retrieve existing task
        var task = await repository.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new NotFoundException("Task", request.Id);

        // Verify project is ACTIVE (strict guard)
        await executionGuard.MustBeActiveForTaskAsync(request.Id, cancellationToken);

        // Retrieve target milestone
        var milestone = await milestoneRepository.GetByIdAsync(task.MilestoneId, cancellationToken)
            ?? throw new NotFoundException("Milestone", task.MilestoneId);

        var projectId = milestone.ProjectId;

        // Verify authorization: Student Leader or Assigned Supervisor
        if (!await repository.IsProjectLeaderOrSupervisorAsync(projectId, actorUserId, cancellationToken))
        {
            throw new ForbiddenException("You are not authorized to update tasks for this project.");
        }

        // Verify new milestone (if changed) belongs to the same project
        if (request.MilestoneId != task.MilestoneId)
        {
            if (!await repository.MilestoneBelongsToProjectAsync(request.MilestoneId, projectId, cancellationToken))
            {
                throw new ConflictException("The new milestone must belong to the same project.");
            }
        }

        // Validate parent task
        if (request.ParentTaskId.HasValue)
        {
            if (request.ParentTaskId.Value == request.Id)
            {
                throw new ConflictException("Task cannot be its own parent.");
            }

            var parentTask = await repository.GetByIdAsync(request.ParentTaskId.Value, cancellationToken)
                ?? throw new NotFoundException("Parent Task", request.ParentTaskId.Value);

            if (!await repository.TaskBelongsToProjectAsync(request.ParentTaskId.Value, projectId, cancellationToken))
            {
                throw new ConflictException("Parent task must belong to the same project.");
            }

            // Circular parent task hierarchy cycle detection
            var hasCycle = await TaskCycleDetector.HasParentCycleAsync(
                request.Id,
                request.ParentTaskId.Value,
                id => repository.GetParentTaskIdAsync(id, cancellationToken));

            if (hasCycle)
            {
                throw new ConflictException("Setting this parent task introduces a cycle in the parent task hierarchy.");
            }
        }

        var updatedTask = await repository.UpdateAsync(
            request.Id,
            request.MilestoneId,
            request.ParentTaskId,
            request.Title,
            request.Description,
            request.Priority,
            request.StartAt,
            request.DueAt,
            cancellationToken);

        await auditTrail.RecordAsync(
            new AuditEntry(
                actorUserId,
                "TASK_UPDATED",
                "TASK",
                task.Id,
                new Dictionary<string, object?>
                {
                    ["projectId"] = projectId,
                    ["milestoneId"] = request.MilestoneId,
                    ["title"] = request.Title
                }),
            cancellationToken);

        return updatedTask;
    }
}
