using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.Meetings.Abstractions;
using MediatR;

namespace AIPMS.Application.Features.Meetings.Commands;

public sealed record RemoveMeetingParticipantCommand(
    long Id,
    long UserId) : IRequest;

public sealed class RemoveMeetingParticipantCommandHandler(
    IMeetingRepository repository,
    IProjectAccessService projectAccess,
    IProjectExecutionGuard executionGuard,
    ICurrentUser currentUser,
    IAuditTrail audit) : IRequestHandler<RemoveMeetingParticipantCommand>
{
    public async Task Handle(RemoveMeetingParticipantCommand command, CancellationToken cancellationToken)
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
            throw new ConflictException("Cannot remove participants from a completed or cancelled meeting.");

        if (!await repository.IsParticipantAsync(command.Id, command.UserId, cancellationToken))
            throw new NotFoundException("MeetingParticipant", command.UserId);

        await repository.RemoveParticipantAsync(command.Id, command.UserId, cancellationToken);

        await audit.RecordAsync(new AuditEntry(
            actorId,
            "MEETING_PARTICIPANT_REMOVED",
            "MEETING",
            command.Id,
            new Dictionary<string, object?>
            {
                ["projectId"] = projectId,
                ["removedUserId"] = command.UserId
            }), cancellationToken);
    }
}
