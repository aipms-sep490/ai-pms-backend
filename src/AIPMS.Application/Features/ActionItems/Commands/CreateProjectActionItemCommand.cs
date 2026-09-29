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

public sealed record CreateProjectActionItemCommand(
    long ProjectId,
    CreateProjectActionItemRequest Request) : IRequest<ProjectActionItemDto>;

public sealed class CreateProjectActionItemCommandHandler(
    IProjectActionItemRepository repository,
    IProgressReportRepository progressReportRepository,
    IProjectAccessService projectAccess,
    IProjectExecutionGuard executionGuard,
    ICurrentUser currentUser,
    IAuditTrail audit,
    TimeProvider clock) : IRequestHandler<CreateProjectActionItemCommand, ProjectActionItemDto>
{
    public async Task<ProjectActionItemDto> Handle(CreateProjectActionItemCommand command, CancellationToken cancellationToken)
    {
        var actorId = currentUser.UserId ?? throw new UnauthorizedException("User is not authenticated.");
        var projectId = command.ProjectId;

        if (!await progressReportRepository.ProjectExistsAsync(projectId, cancellationToken))
            throw new NotFoundException("Project", projectId);

        await executionGuard.MustBeActiveAsync(projectId, cancellationToken);

        if (!await projectAccess.CanAccessAsync(actorId, projectId, cancellationToken))
            throw new ForbiddenException("You cannot access this project's action items.");

        var req = command.Request;
        if (string.IsNullOrWhiteSpace(req.Title))
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["title"] = ["Title is required for an action item."]
            });

        var sourceType = req.SourceType?.Trim().ToUpperInvariant() ?? "";
        if (sourceType != "MEETING" && sourceType != "PROGRESS_REPORT")
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["sourceType"] = ["SourceType must be either MEETING or PROGRESS_REPORT."]
            });

        var isAdmin = currentUser.Roles.Contains(AppRoles.Admin, StringComparer.Ordinal);
        var isLeader = await repository.IsProjectLeaderAsync(projectId, actorId, cancellationToken);
        var isSupervisor = await repository.IsAssignedSupervisorAsync(projectId, actorId, cancellationToken);

        if (sourceType == "MEETING")
        {
            if (!req.MeetingId.HasValue || req.ProgressReportId.HasValue)
                throw new ValidationException(new Dictionary<string, string[]>
                {
                    ["source"] = ["Meeting action items must specify meetingId and have no progressReportId."]
                });

            if (!await repository.IsMeetingInProjectAsync(req.MeetingId.Value, projectId, cancellationToken))
                throw new NotFoundException("Meeting", req.MeetingId.Value);

            // P2-5: Cancelled meetings are read-only — no new action items allowed.
            if (await repository.IsMeetingCancelledAsync(req.MeetingId.Value, cancellationToken))
                throw new ConflictException("Cannot create action items for a cancelled meeting.");

            var isCreator = await repository.IsMeetingCreatorAsync(req.MeetingId.Value, actorId, cancellationToken);
            var isParticipant = await repository.IsMeetingParticipantAsync(req.MeetingId.Value, actorId, cancellationToken);
            var isActiveMember = await repository.IsActiveTeamMemberAsync(projectId, actorId, cancellationToken);

            if (!isAdmin && !isLeader && !isSupervisor && !isCreator && !(isParticipant && isActiveMember))
                throw new ForbiddenException("You do not have permission to create an action item for this meeting.");
        }
        else // PROGRESS_REPORT
        {
            if (!req.ProgressReportId.HasValue || req.MeetingId.HasValue)
                throw new ValidationException(new Dictionary<string, string[]>
                {
                    ["source"] = ["Progress report action items must specify progressReportId and have no meetingId."]
                });

            if (!await repository.IsProgressReportInProjectAsync(req.ProgressReportId.Value, projectId, cancellationToken))
                throw new NotFoundException("ProgressReport", req.ProgressReportId.Value);

            var isActiveMember = await repository.IsActiveTeamMemberAsync(projectId, actorId, cancellationToken);

            if (!isAdmin && !isActiveMember && !isSupervisor)
                throw new ForbiddenException("Only active team members or assigned supervisors can create action items from progress reports.");
        }

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

        var now = clock.GetUtcNow().UtcDateTime;
        return await repository.CreateAsync(
            projectId,
            sourceType,
            req.MeetingId,
            req.ProgressReportId,
            req.Title,
            req.Description,
            req.OwnerId,
            req.TaskId,
            req.MilestoneId,
            req.DueAt,
            actorId,
            now,
            onCreated: async created =>
            {
                await audit.RecordAsync(new AuditEntry(
                    actorId,
                    "PROJECT_ACTION_ITEM_CREATED",
                    "PROJECT_ACTION_ITEM",
                    created.Id,
                    new Dictionary<string, object?>
                    {
                        ["projectId"] = projectId,
                        ["sourceType"] = created.SourceType,
                        ["meetingId"] = created.MeetingId,
                        ["progressReportId"] = created.ProgressReportId,
                        ["title"] = created.Title,
                        ["ownerId"] = created.OwnerId
                    }), cancellationToken);
            },
            cancellationToken: cancellationToken);
    }
}
