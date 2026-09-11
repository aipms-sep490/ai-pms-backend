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

public sealed record AddMeetingParticipantCommand(
    long Id,
    AddMeetingParticipantRequest Request) : IRequest<MeetingParticipantDto>;

public sealed class AddMeetingParticipantCommandHandler(
    IMeetingRepository repository,
    IProjectAccessService projectAccess,
    IProjectExecutionGuard executionGuard,
    ICurrentUser currentUser,
    IAuditTrail audit,
    TimeProvider clock) : IRequestHandler<AddMeetingParticipantCommand, MeetingParticipantDto>
{
    public async Task<MeetingParticipantDto> Handle(AddMeetingParticipantCommand command, CancellationToken cancellationToken)
    {
        var actorId = currentUser.UserId ?? throw new UnauthorizedException("User is not authenticated.");
        var projectId = await repository.GetProjectIdAsync(command.Id, cancellationToken)
            ?? throw new NotFoundException("Meeting", command.Id);

        await executionGuard.MustBeActiveAsync(projectId, cancellationToken);

        if (!await projectAccess.CanAccessAsync(actorId, projectId, cancellationToken))
            throw new ForbiddenException("You cannot access this project's meetings.");

        if (!currentUser.Roles.Contains(AppRoles.Admin) && !await repository.CanManageMeetingAsync(command.Id, actorId, cancellationToken))
            throw new ForbiddenException("Only the meeting organizer, team leader, or assigned supervisor can manage participants.");

        var status = await repository.GetStatusAsync(command.Id, cancellationToken);
        if (status is "COMPLETED" or "CANCELLED")
            throw new ConflictException("Cannot add participants to a completed or cancelled meeting.");

        if (!await repository.IsProjectMemberOrSupervisorAsync(projectId, command.Request.UserId, cancellationToken))
            throw new ConflictException($"User {command.Request.UserId} is not an active member or supervisor of this project.");

        if (await repository.IsParticipantAsync(command.Id, command.Request.UserId, cancellationToken))
            throw new ConflictException("User is already a participant in this meeting.");

        var now = clock.GetUtcNow().UtcDateTime;
        var result = await repository.AddParticipantAsync(
            command.Id,
            command.Request.UserId,
            command.Request.AttendanceStatus,
            now,
            cancellationToken);

        await audit.RecordAsync(new AuditEntry(
            actorId,
            "MEETING_PARTICIPANT_ADDED",
            "MEETING",
            command.Id,
            new Dictionary<string, object?>
            {
                ["projectId"] = projectId,
                ["participantUserId"] = command.Request.UserId
            }), cancellationToken);

        return result;
    }
}
