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

public sealed record UpdateProjectActionItemStatusCommand(
    long ProjectId,
    long ActionItemId,
    UpdateProjectActionItemStatusRequest Request) : IRequest<ProjectActionItemDto>;

public sealed class UpdateProjectActionItemStatusCommandHandler(
    IProjectActionItemRepository repository,
    IProgressReportRepository progressReportRepository,
    IProjectAccessService projectAccess,
    IProjectExecutionGuard executionGuard,
    ICurrentUser currentUser,
    IAuditTrail audit,
    TimeProvider clock) : IRequestHandler<UpdateProjectActionItemStatusCommand, ProjectActionItemDto>
{
    private static readonly HashSet<string> ValidStatuses =
        ["TODO", "IN_PROGRESS", "BLOCKED", "DONE", "CANCELLED"];

    public async Task<ProjectActionItemDto> Handle(UpdateProjectActionItemStatusCommand command, CancellationToken cancellationToken)
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

        var targetStatus = command.Request.Status?.Trim().ToUpperInvariant() ?? "";
        if (!ValidStatuses.Contains(targetStatus))
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["status"] = [$"Status '{command.Request.Status}' is invalid. Allowed: {string.Join(", ", ValidStatuses)}"]
            });
        }

        var currentStatus = existing.Status.ToUpperInvariant();
        if (currentStatus == targetStatus)
        {
            return existing; // Self-transition is a no-op
        }

        // Validate state machine transition matrix
        var isValidTransition = (currentStatus, targetStatus) switch
        {
            ("TODO", "IN_PROGRESS") => true,
            ("TODO", "BLOCKED") => true,
            ("TODO", "CANCELLED") => true,

            ("IN_PROGRESS", "TODO") => true,
            ("IN_PROGRESS", "BLOCKED") => true,
            ("IN_PROGRESS", "DONE") => true,
            ("IN_PROGRESS", "CANCELLED") => true,

            ("BLOCKED", "TODO") => true,
            ("BLOCKED", "IN_PROGRESS") => true,
            ("BLOCKED", "CANCELLED") => true,

            ("DONE", "IN_PROGRESS") => true,     // Reopen terminal
            ("CANCELLED", "TODO") => true,        // Reopen terminal

            _ => false
        };

        if (!isValidTransition)
        {
            throw new ConflictException($"Invalid status transition from {currentStatus} to {targetStatus}.");
        }

        var isAdmin = currentUser.Roles.Contains(AppRoles.Admin, StringComparer.Ordinal);
        var isLeader = await repository.IsProjectLeaderAsync(projectId, actorId, cancellationToken);
        var isSupervisor = await repository.IsAssignedSupervisorAsync(projectId, actorId, cancellationToken);

        var isTerminalReopen = (currentStatus == "DONE" && targetStatus == "IN_PROGRESS")
            || (currentStatus == "CANCELLED" && targetStatus == "TODO");

        if (isTerminalReopen)
        {
            if (!isAdmin && !isLeader && !isSupervisor)
            {
                throw new ForbiddenException("Only the team leader, assigned supervisor, or admin can reopen a completed or cancelled action item.");
            }
        }
        else
        {
            var isAssignee = existing.OwnerId == actorId;
            var isEligibleAssignee = isAssignee && await repository.IsEligibleOwnerAsync(projectId, actorId, cancellationToken);

            if (!isAdmin && !isLeader && !isSupervisor && !isEligibleAssignee)
            {
                throw new ForbiddenException("Only the assignee, team leader, assigned supervisor, or admin can update action item status.");
            }
        }

        Guid? expectedToken = null;
        if (!string.IsNullOrWhiteSpace(command.Request.ConcurrencyToken))
        {
            if (Guid.TryParse(command.Request.ConcurrencyToken, out var parsed))
            {
                expectedToken = parsed;
            }
            else
            {
                throw new ConflictException("Invalid concurrency token format.", WorkflowErrorCodes.StaleConcurrencyToken);
            }
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var result = await repository.UpdateStatusAsync(id, targetStatus, expectedToken, now, cancellationToken);

        await audit.RecordAsync(new AuditEntry(
            actorId,
            "PROJECT_ACTION_ITEM_STATUS_CHANGED",
            "PROJECT_ACTION_ITEM",
            result.Id,
            new Dictionary<string, object?>
            {
                ["projectId"] = projectId,
                ["previousStatus"] = currentStatus,
                ["newStatus"] = targetStatus
            }), cancellationToken);

        return result;
    }
}
