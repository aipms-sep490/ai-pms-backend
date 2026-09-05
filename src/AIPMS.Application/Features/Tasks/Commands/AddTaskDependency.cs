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
using AIPMS.Application.Features.Tasks.Domain;
using AIPMS.Application.Features.Tasks.DTOs;
using FluentValidation;
using MediatR;

namespace AIPMS.Application.Features.Tasks.Commands;

public sealed record AddTaskDependencyCommand(
    long TaskId,
    long DependsOnTaskId,
    string DependencyType) : IRequest<TaskDto>;

public sealed class AddTaskDependencyCommandValidator : AbstractValidator<AddTaskDependencyCommand>
{
    private static readonly string[] AllowedTypes = ["FINISH_TO_START", "START_TO_START", "FINISH_TO_FINISH", "START_TO_FINISH"];

    public AddTaskDependencyCommandValidator()
    {
        RuleFor(static x => x.TaskId)
            .GreaterThan(0).WithMessage("TaskId must be greater than 0.");

        RuleFor(static x => x.DependsOnTaskId)
            .GreaterThan(0).WithMessage("DependsOnTaskId must be greater than 0.");

        RuleFor(static x => x.DependencyType)
            .NotEmpty().WithMessage("DependencyType is required.")
            .Must(static t => AllowedTypes.Contains(t))
            .WithMessage($"DependencyType must be one of: {string.Join(", ", AllowedTypes)}.");

        RuleFor(static x => x)
            .Must(static x => x.TaskId != x.DependsOnTaskId)
            .WithMessage("Task cannot depend on itself.");
    }
}

public sealed class AddTaskDependencyCommandHandler(
    ITaskRepository repository,
    IMilestoneRepository milestoneRepository,
    IProjectExecutionGuard executionGuard,
    ICurrentUser currentUser,
    IAuditTrail auditTrail)
    : IRequestHandler<AddTaskDependencyCommand, TaskDto>
{
    public async Task<TaskDto> Handle(
        AddTaskDependencyCommand request,
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is null)
        {
            throw new UnauthorizedException();
        }

        var actorUserId = currentUser.UserId.Value;

        // Retrieve existing tasks
        var task = await repository.GetByIdAsync(request.TaskId, cancellationToken)
            ?? throw new NotFoundException("Task", request.TaskId);

        var dependsOnTask = await repository.GetByIdAsync(request.DependsOnTaskId, cancellationToken)
            ?? throw new NotFoundException("Depends-on Task", request.DependsOnTaskId);

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

        // Verify both tasks belong to the same project
        if (!await repository.TaskBelongsToProjectAsync(request.DependsOnTaskId, projectId, cancellationToken))
        {
            throw new ConflictException("Both tasks must belong to the same project.");
        }

        // Verify dependency does not already exist
        if (task.Dependencies.Any(d => d.DependsOnTaskId == request.DependsOnTaskId))
        {
            throw new ConflictException("This dependency mapping already exists.");
        }

        // Check for circular dependency
        var hasCycle = await TaskCycleDetector.HasDependencyCycleAsync(
            request.TaskId,
            request.DependsOnTaskId,
            id => repository.GetDependsOnTaskIdsAsync(id, cancellationToken));

        if (hasCycle)
        {
            throw new ConflictException("Adding this dependency introduces a circular reference between tasks.");
        }

        await repository.AddDependencyAsync(
            request.TaskId,
            request.DependsOnTaskId,
            request.DependencyType,
            cancellationToken);

        await auditTrail.RecordAsync(
            new AuditEntry(
                actorUserId,
                "TASK_DEPENDENCY_ADDED",
                "TASK",
                request.TaskId,
                new Dictionary<string, object?>
                {
                    ["projectId"] = projectId,
                    ["dependsOnTaskId"] = request.DependsOnTaskId,
                    ["dependencyType"] = request.DependencyType
                }),
            cancellationToken);

        return (await repository.GetByIdAsync(request.TaskId, cancellationToken))!;
    }
}
