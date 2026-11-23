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
using AIPMS.Application.Features.Disciplines.Abstractions;
using AIPMS.Application.Features.Disciplines.DTOs;
using AIPMS.Application.Features.Projects.Abstractions;
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
    IReadOnlyList<long> AssigneeUserIds,
    IReadOnlyList<TaskDisciplineInput>? Disciplines = null,
    string? InitialStatus = null,
    long? SprintId = null,
    int? StoryPoints = null,
    IReadOnlyList<string>? Labels = null,
    long? GateRequirementId = null,
    IReadOnlyList<string>? InitialCriteria = null) : IRequest<TaskDto>;

public sealed class CreateTaskCommandHandler(
    ITaskRepository repository,
    IMilestoneRepository milestoneRepository,
    IProjectExecutionGuard executionGuard,
    ICurrentUser currentUser,
    IAuditTrail auditTrail,
    IExecutionWindowService? executionWindowService = null,
    IDisciplineService? disciplines = null)
    : IRequestHandler<CreateTaskCommand, TaskDto>
{
    public Task<TaskDto> Handle(CreateTaskCommand request, CancellationToken cancellationToken) => disciplines is null
        ? HandleCore(request, cancellationToken)
        : disciplines.CreateTaskAsync(request.MilestoneId, request.Disciplines, token => HandleCore(request, token), cancellationToken);

    private async Task<TaskDto> HandleCore(
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

        // Validate initial status (only TODO or IN_PROGRESS allowed on creation; IN_REVIEW/DONE/CANCELLED/BLOCKED => 422 INVALID_INITIAL_STATUS)
        if (!string.IsNullOrWhiteSpace(request.InitialStatus))
        {
            var initStatus = request.InitialStatus.Trim().ToUpperInvariant();
            if (initStatus is not ("TODO" or "IN_PROGRESS"))
                throw new AIPMS.Domain.Exceptions.DomainException(
                    $"Invalid initial task status '{request.InitialStatus}'. Only TODO or IN_PROGRESS is allowed on creation.",
                    WorkflowErrorCodes.InvalidInitialStatus);
        }

        // Verify authorization: Student Leader or Assigned Supervisor can create any task (including GateRequirement core tasks);
        // active STUDENT_MEMBER can create normal tasks within their own majorScope only.
        var isLeaderOrSupervisor = await repository.IsProjectLeaderOrSupervisorAsync(projectId, actorUserId, cancellationToken);
        if (!isLeaderOrSupervisor)
        {
            if (request.GateRequirementId.HasValue)
                throw new ForbiddenException("Only the team leader or assigned supervisor can create core gate-linked tasks.");

            var isActiveMember = await repository.IsUserActiveTeamMemberAsync(projectId, actorUserId, cancellationToken);
            if (!isActiveMember)
                throw new ForbiddenException("You are not authorized to create tasks for this project.");

            if (request.Disciplines is { Count: > 0 })
            {
                var actorMajor = await repository.GetUserMajorIdAsync(projectId, actorUserId, cancellationToken);
                if (actorMajor.HasValue && request.Disciplines.Any(d => d.MajorId != actorMajor.Value))
                    throw new ForbiddenException("Team members can only create tasks within their own major scope.");
            }
        }

        // Validate task dates against milestone and project execution window
        if (executionWindowService is not null)
        {
            await executionWindowService.ValidateTaskDatesAsync(
                projectId,
                request.MilestoneId,
                request.StartAt,
                request.DueAt,
                cancellationToken);
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

        if (!string.IsNullOrWhiteSpace(request.InitialStatus)
            || request.SprintId.HasValue
            || request.StoryPoints.HasValue
            || request.Labels is { Count: > 0 }
            || request.GateRequirementId.HasValue
            || request.InitialCriteria is { Count: > 0 })
        {
            task = await repository.ApplyOneStepCreateExtrasAsync(
                task.Id,
                request.InitialStatus,
                request.SprintId,
                request.StoryPoints,
                request.Labels,
                request.GateRequirementId,
                request.InitialCriteria,
                actorUserId,
                cancellationToken);
        }

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
