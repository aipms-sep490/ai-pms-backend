using System;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Milestones.Abstractions;
using AIPMS.Application.Features.Milestones.DTOs;
using MediatR;

namespace AIPMS.Application.Features.Milestones.Commands;

public sealed record UpdateMilestoneCommand(
    long Id,
    string Title,
    string? Description,
    DateOnly? StartDate,
    DateOnly? DueDate,
    string Status,
    int SortOrder) : IRequest<MilestoneDto>;

public sealed class UpdateMilestoneCommandHandler(
    IMilestoneRepository repository,
    IProjectExecutionGuard executionGuard,
    ICurrentUser currentUser,
    IAuditTrail auditTrail)
    : IRequestHandler<UpdateMilestoneCommand, MilestoneDto>
{
    public async Task<MilestoneDto> Handle(
        UpdateMilestoneCommand request,
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is null)
        {
            throw new UnauthorizedException();
        }

        var actorUserId = currentUser.UserId.Value;

        // Retrieve existing milestone
        var milestone = await repository.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new NotFoundException("Milestone", request.Id);

        // Verify project is ACTIVE (strict guard)
        await executionGuard.MustBeActiveForMilestoneAsync(request.Id, cancellationToken);

        // Verify user is authorized: Student Leader or Assigned Supervisor
        if (!await repository.IsProjectLeaderOrSupervisorAsync(milestone.ProjectId, actorUserId, cancellationToken))
        {
            throw new ForbiddenException("You are not authorized to update milestones for this project.");
        }

        var updatedMilestone = await repository.UpdateAsync(
            request.Id,
            request.Title,
            request.Description,
            request.StartDate,
            request.DueDate,
            request.Status,
            request.SortOrder,
            cancellationToken);

        await auditTrail.RecordAsync(
            new AuditEntry(
                actorUserId,
                "MILESTONE_UPDATED",
                "MILESTONE",
                milestone.Id,
                new Dictionary<string, object?>
                {
                    ["projectId"] = milestone.ProjectId,
                    ["title"] = request.Title,
                    ["status"] = request.Status
                }),
            cancellationToken);

        return updatedMilestone;
    }
}
