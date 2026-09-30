using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.ActionItems.Abstractions;
using AIPMS.Application.Features.ActionItems.DTOs;
using AIPMS.Application.Features.ProgressReports.Abstractions;
using MediatR;

namespace AIPMS.Application.Features.ActionItems.Commands;

public sealed record UpdateProjectActionItemCommand(
    long ProjectId,
    long ActionItemId,
    UpdateProjectActionItemRequest Request) : IRequest<ProjectActionItemDto>;

public sealed class UpdateProjectActionItemCommandHandler(
    IProjectActionItemRepository repository,
    IProgressReportRepository progressReportRepository,
    IProjectAccessService projectAccess,
    IProjectExecutionGuard executionGuard,
    ICurrentUser currentUser,
    IAuditTrail audit,
    TimeProvider clock) : IRequestHandler<UpdateProjectActionItemCommand, ProjectActionItemDto>
{
    public async Task<ProjectActionItemDto> Handle(UpdateProjectActionItemCommand command, CancellationToken cancellationToken)
    {
        var actorId = currentUser.UserId ?? throw new UnauthorizedException("User is not authenticated.");
        var projectId = command.ProjectId;
        var id = command.ActionItemId;

        if (!await progressReportRepository.ProjectExistsAsync(projectId, cancellationToken))
            throw new NotFoundException("Project", projectId);

        await executionGuard.MustBeActiveAsync(projectId, cancellationToken);

        if (!await projectAccess.CanAccessAsync(actorId, projectId, cancellationToken))
            throw new ForbiddenException("You cannot access this project's action items.");

        var existing = await repository.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException("ProjectActionItem", id);

        if (existing.ProjectId != projectId)
            throw new NotFoundException("ProjectActionItem", id);

        if (existing.MeetingId.HasValue && await repository.IsMeetingCancelledAsync(existing.MeetingId.Value, cancellationToken))
            throw new ConflictException("Cannot modify action items associated with a cancelled meeting.");

        var isAdmin = await repository.HasAdminRoleInDbAsync(actorId, cancellationToken);
        var isLeader = await repository.IsProjectLeaderAsync(projectId, actorId, cancellationToken);
        var isSupervisor = await repository.IsAssignedSupervisorAsync(projectId, actorId, cancellationToken);
        var isCreator = existing.CreatedBy == actorId;

        if (!isAdmin && !isLeader && !isSupervisor && !isCreator)
            throw new ForbiddenException("Only the creator, team leader, assigned supervisor, or admin can edit action item details.");

        var req = command.Request;
        if (string.IsNullOrWhiteSpace(req.Title))
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["title"] = ["Title is required for an action item."]
            });

        if (req.OwnerId.HasValue)
        {
            if (!await repository.IsEligibleOwnerAsync(projectId, req.OwnerId.Value, cancellationToken))
                throw new ConflictException("Owner must be an active project member or assigned supervisor.");
        }

        if (req.TaskId.HasValue && !await repository.IsTaskInProjectAsync(req.TaskId.Value, projectId, cancellationToken))
            throw new NotFoundException("Task", req.TaskId.Value);

        if (req.MilestoneId.HasValue && !await repository.IsMilestoneInProjectAsync(req.MilestoneId.Value, projectId, cancellationToken))
            throw new NotFoundException("Milestone", req.MilestoneId.Value);

        // When both task and milestone are specified, the task must belong to the selected milestone.
        if (req.TaskId.HasValue && req.MilestoneId.HasValue
            && !await repository.IsTaskBelongsToMilestoneAsync(req.TaskId.Value, req.MilestoneId.Value, cancellationToken))
        {
            throw new ConflictException("The specified task does not belong to the selected milestone.");
        }

        Guid? expectedToken = null;
        if (!string.IsNullOrWhiteSpace(req.ConcurrencyToken))
        {
            if (Guid.TryParse(req.ConcurrencyToken, out var parsed))
            {
                expectedToken = parsed;
            }
            else
            {
                throw new ConflictException("Invalid concurrency token format.", WorkflowErrorCodes.StaleConcurrencyToken);
            }
        }

        var now = clock.GetUtcNow().UtcDateTime;
        return await repository.UpdateDetailsAsync(
            id,
            req.Title,
            req.Description,
            req.OwnerId,
            req.TaskId,
            req.MilestoneId,
            req.DueAt,
            expectedToken,
            now,
            onUpdated: async updated =>
            {
                await audit.RecordAsync(new AuditEntry(
                    actorId,
                    "PROJECT_ACTION_ITEM_UPDATED",
                    "PROJECT_ACTION_ITEM",
                    updated.Id,
                    new Dictionary<string, object?>
                    {
                        ["projectId"] = projectId,
                        ["title"] = updated.Title,
                        ["ownerId"] = updated.OwnerId
                    }), cancellationToken);
            },
            cancellationToken: cancellationToken);
    }
}
