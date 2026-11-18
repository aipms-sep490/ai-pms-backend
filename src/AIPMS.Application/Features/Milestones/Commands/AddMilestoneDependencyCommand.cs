using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Projects;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Milestones.Abstractions;
using AIPMS.Application.Features.Milestones.DTOs;
using AIPMS.Domain.Milestones;
using MediatR;

namespace AIPMS.Application.Features.Milestones.Commands;

public sealed record AddMilestoneDependencyCommand(
    long MilestoneId,
    long DependsOnMilestoneId,
    string DependencyType = "FINISH_TO_START",
    string? ConcurrencyToken = null) : IRequest<MilestoneDto>, IExecutionMutation
{
    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyList<ExecutionWrite> Writes => [new(ExecutionResource.Milestone, MilestoneId, ConcurrencyToken)];
}

public sealed class AddMilestoneDependencyCommandHandler(
    IMilestoneRepository repository,
    IProjectExecutionGuard executionGuard,
    ICurrentUser currentUser,
    IAuditTrail auditTrail)
    : IRequestHandler<AddMilestoneDependencyCommand, MilestoneDto>
{
    public async Task<MilestoneDto> Handle(
        AddMilestoneDependencyCommand request,
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is null)
        {
            throw new UnauthorizedException();
        }

        var actorUserId = currentUser.UserId.Value;

        if (request.MilestoneId == request.DependsOnMilestoneId)
        {
            throw new ConflictException("Milestone cannot depend on itself.", WorkflowErrorCodes.MilestoneDependencyNotSelf);
        }

        var milestone = await repository.GetByIdAsync(request.MilestoneId, cancellationToken)
            ?? throw new NotFoundException("Milestone", request.MilestoneId);

        var dependsOnMilestone = await repository.GetByIdAsync(request.DependsOnMilestoneId, cancellationToken)
            ?? throw new NotFoundException("Depends-on Milestone", request.DependsOnMilestoneId);

        // Verify project is ACTIVE
        await executionGuard.MustBeActiveForMilestoneAsync(request.MilestoneId, cancellationToken);

        var projectId = milestone.ProjectId;

        // Verify authorization: Student Leader or Assigned Supervisor
        if (!await repository.IsProjectLeaderOrSupervisorAsync(projectId, actorUserId, cancellationToken))
        {
            throw new ForbiddenException("You are not authorized to manage milestone dependencies for this project.");
        }

        // Verify both milestones belong to the same project
        if (!await repository.MilestoneBelongsToProjectAsync(request.DependsOnMilestoneId, projectId, cancellationToken))
        {
            throw new ConflictException("Both milestones must belong to the same project.", WorkflowErrorCodes.MilestoneDependencySameProject);
        }

        // Validate dependency type: only FINISH_TO_START is currently supported by business policy
        if (request.DependencyType != "FINISH_TO_START")
        {
            throw new ConflictException("Only FINISH_TO_START dependency type is currently supported by business policy.");
        }

        // Acquire exclusive project-level lock to prevent concurrent cycle creation race conditions
        await repository.AcquireProjectDependencyLockAsync(projectId, cancellationToken);

        // Verify dependency does not already exist
        if (milestone.Dependencies != null && milestone.Dependencies.Any(d => d.DependsOnMilestoneId == request.DependsOnMilestoneId))
        {
            throw new ConflictException("This milestone dependency mapping already exists.");
        }

        // Check for circular dependency using BFS
        var hasCycle = await MilestoneCycleDetector.HasDependencyCycleAsync(
            request.MilestoneId,
            request.DependsOnMilestoneId,
            async id => await repository.GetDependsOnMilestoneIdsAsync(id, cancellationToken));

        if (hasCycle)
        {
            throw new ConflictException("Adding this dependency introduces a circular reference between milestones.", WorkflowErrorCodes.MilestoneDependencyCycle);
        }

        // Validate Finish-to-Start timing
        if (dependsOnMilestone.DueDate.HasValue && milestone.StartDate.HasValue)
        {
            if (milestone.StartDate.Value < dependsOnMilestone.DueDate.Value)
            {
                throw new ConflictException(
                    $"Successor milestone start date ({milestone.StartDate.Value:yyyy-MM-dd}) cannot be earlier than predecessor milestone due date ({dependsOnMilestone.DueDate.Value:yyyy-MM-dd}).",
                    WorkflowErrorCodes.MilestoneDependencyOrderViolation);
            }
        }

        await repository.AddDependencyAsync(
            request.MilestoneId,
            request.DependsOnMilestoneId,
            request.DependencyType,
            cancellationToken);

        await auditTrail.RecordAsync(
            new AuditEntry(
                actorUserId,
                "MILESTONE_DEPENDENCY_ADDED",
                "MILESTONE",
                milestone.Id,
                new Dictionary<string, object?>
                {
                    ["projectId"] = projectId,
                    ["milestoneId"] = request.MilestoneId,
                    ["dependsOnMilestoneId"] = request.DependsOnMilestoneId,
                    ["dependencyType"] = request.DependencyType
                }),
            cancellationToken);

        return (await repository.GetByIdAsync(request.MilestoneId, cancellationToken))!;
    }
}
