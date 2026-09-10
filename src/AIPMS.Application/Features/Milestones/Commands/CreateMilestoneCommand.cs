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

public sealed record CreateMilestoneCommand(
    long ProjectId,
    string Title,
    string? Description,
    DateOnly? StartDate,
    DateOnly? DueDate,
    int SortOrder) : IRequest<MilestoneDto>;

public sealed class CreateMilestoneCommandHandler(
    IMilestoneRepository repository,
    IProjectExecutionGuard executionGuard,
    ICurrentUser currentUser,
    IAuditTrail auditTrail)
    : IRequestHandler<CreateMilestoneCommand, MilestoneDto>
{
    public async Task<MilestoneDto> Handle(
        CreateMilestoneCommand request,
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is null)
        {
            throw new UnauthorizedException();
        }

        var actorUserId = currentUser.UserId.Value;

        // Verify project is ACTIVE (strict guard)
        await executionGuard.MustBeActiveAsync(request.ProjectId, cancellationToken);

        // Verify user is authorized: Student Leader or Assigned Supervisor
        if (!await repository.IsProjectLeaderOrSupervisorAsync(request.ProjectId, actorUserId, cancellationToken))
        {
            throw new ForbiddenException("You are not authorized to create milestones for this project.");
        }

        var milestone = await repository.CreateAsync(
            request.ProjectId,
            request.Title,
            request.Description,
            request.StartDate,
            request.DueDate,
            request.SortOrder,
            actorUserId,
            cancellationToken);

        await auditTrail.RecordAsync(
            new AuditEntry(
                actorUserId,
                "MILESTONE_CREATED",
                "MILESTONE",
                milestone.Id,
                new Dictionary<string, object?>
                {
                    ["projectId"] = request.ProjectId,
                    ["title"] = request.Title
                }),
            cancellationToken);

        return milestone;
    }
}
