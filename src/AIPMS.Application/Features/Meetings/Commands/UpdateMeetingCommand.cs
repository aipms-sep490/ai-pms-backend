using AIPMS.Application.Abstractions.Projects;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.Meetings.Abstractions;
using AIPMS.Application.Features.Meetings.DTOs;
using MediatR;

namespace AIPMS.Application.Features.Meetings.Commands;

public sealed record UpdateMeetingCommand(
    long Id,
    UpdateMeetingRequest Request, string? ConcurrencyToken = null) : IRequest<MeetingDto>, IExecutionMutation
{
    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyList<ExecutionWrite> Writes => [new(ExecutionResource.Meeting, Id, ConcurrencyToken ?? Request.ConcurrencyToken)];
}

public sealed class UpdateMeetingCommandHandler(
    IMeetingRepository repository,
    IProjectAccessService projectAccess,
    IProjectExecutionGuard executionGuard,
    ICurrentUser currentUser,
    IAuditTrail audit,
    TimeProvider clock,
    IMeetingVideoMetadataRepository? videoMetadata = null) : IRequestHandler<UpdateMeetingCommand, MeetingDto>
{
    public async Task<MeetingDto> Handle(UpdateMeetingCommand command, CancellationToken cancellationToken)
    {
        var actorId = currentUser.UserId ?? throw new UnauthorizedException("User is not authenticated.");
        var projectId = await repository.GetProjectIdAsync(command.Id, cancellationToken)
            ?? throw new NotFoundException("Meeting", command.Id);

        await executionGuard.MustBeActiveAsync(projectId, cancellationToken);

        if (!await projectAccess.CanAccessAsync(actorId, projectId, cancellationToken))
            throw new ForbiddenException("You cannot access this project's meetings.");

        if (!currentUser.Roles.Contains(AppRoles.Admin) && !await repository.CanManageMeetingAsync(command.Id, actorId, cancellationToken))
            throw new ForbiddenException("Only the meeting organizer, team leader, or assigned supervisor can update this meeting.");

        var status = await repository.GetStatusAsync(command.Id, cancellationToken);
        if (status is "COMPLETED" or "CANCELLED")
            throw new ConflictException("Cannot update a meeting that is completed or cancelled.");

        var delivery = AIPMS.Application.Features.Meetings.Validators.MeetingDeliveryRules.Legacy(command.Request.Location, command.Request.OnlineUrl);
        var mode = command.Request.MeetingDeliveryMode ?? delivery.Mode;
        var channel = command.Request.VideoChannel ?? delivery.Channel;
        var now = clock.GetUtcNow().UtcDateTime;
        var result = await repository.UpdateAsync(
            command.Id,
            command.Request.Title,
            command.Request.Agenda,
            command.Request.StartAt,
            command.Request.EndAt,
            command.Request.Location,
            command.Request.OnlineUrl,
            now,
            command.Request.Minutes,
            command.Request.Decisions,
            command.Request.Blockers,
            async updated =>
            {
                if (videoMetadata != null) await videoMetadata.SetAsync(updated.Id, mode, channel, cancellationToken);
                await audit.RecordAsync(new AuditEntry(
                    actorId,
                    "MEETING_UPDATED",
                    "MEETING",
                    updated.Id,
                    new Dictionary<string, object?>
                    {
                        ["projectId"] = projectId,
                        ["title"] = updated.Title
                    }), cancellationToken);
            },
            cancellationToken);

        return videoMetadata is null ? result : result with { MeetingDeliveryMode = mode, VideoChannel = channel };
    }
}
