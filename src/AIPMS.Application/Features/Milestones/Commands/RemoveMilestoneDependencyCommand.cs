using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Projects;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Milestones.Abstractions;
using AIPMS.Application.Features.Milestones.DTOs;
using MediatR;

namespace AIPMS.Application.Features.Milestones.Commands;

public sealed record RemoveMilestoneDependencyCommand(
    long MilestoneId,
    long DependsOnMilestoneId,
    string? ConcurrencyToken = null) : IRequest<MilestoneDto>, IExecutionMutation
{
    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyList<ExecutionWrite> Writes => [new(ExecutionResource.Milestone, MilestoneId, ConcurrencyToken)];
}

public sealed class RemoveMilestoneDependencyCommandHandler(
    IMilestoneRepository repository,
    IProjectExecutionGuard executionGuard,
    ICurrentUser currentUser,
    IAuditTrail auditTrail)
    : IRequestHandler<RemoveMilestoneDependencyCommand, MilestoneDto>
{
    public async Task<MilestoneDto> Handle(
        RemoveMilestoneDependencyCommand request,
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is null)
        {
            throw new UnauthorizedException();
        }

        var actorUserId = currentUser.UserId.Value;

        var milestone = await repository.GetByIdAsync(request.MilestoneId, cancellationToken)
            ?? throw new NotFoundException("Milestone", request.MilestoneId);

        // Verify project is ACTIVE
        await executionGuard.MustBeActiveForMilestoneAsync(request.MilestoneId, cancellationToken);

        var projectId = milestone.ProjectId;

        // Verify authorization: Student Leader or Assigned Supervisor
        if (!await repository.IsProjectLeaderOrSupervisorAsync(projectId, actorUserId, cancellationToken))
        {
            throw new ForbiddenException("You are not authorized to manage milestone dependencies for this project.");
        }

        await repository.RemoveDependencyAsync(
            request.MilestoneId,
            request.DependsOnMilestoneId,
            cancellationToken);

        await auditTrail.RecordAsync(
            new AuditEntry(
                actorUserId,
                "MILESTONE_DEPENDENCY_REMOVED",
                "MILESTONE",
                milestone.Id,
                new Dictionary<string, object?>
                {
                    ["projectId"] = projectId,
                    ["milestoneId"] = request.MilestoneId,
                    ["dependsOnMilestoneId"] = request.DependsOnMilestoneId
                }),
            cancellationToken);

        return (await repository.GetByIdAsync(request.MilestoneId, cancellationToken))!;
    }
}
