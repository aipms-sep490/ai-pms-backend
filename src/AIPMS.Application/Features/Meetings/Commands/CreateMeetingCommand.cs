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

public sealed record CreateMeetingCommand(
    long ProjectId,
    CreateMeetingRequest Request) : IRequest<MeetingDto>;

public sealed class CreateMeetingCommandHandler(
    IMeetingRepository repository,
    IProjectAccessService projectAccess,
    IProjectExecutionGuard executionGuard,
    ICurrentUser currentUser,
    IAuditTrail audit,
    TimeProvider clock) : IRequestHandler<CreateMeetingCommand, MeetingDto>
{
    public async Task<MeetingDto> Handle(CreateMeetingCommand command, CancellationToken cancellationToken)
    {
        var actorId = currentUser.UserId ?? throw new UnauthorizedException("User is not authenticated.");
        var projectId = command.ProjectId;

        if (!await repository.ProjectExistsAsync(projectId, cancellationToken))
            throw new NotFoundException("Project", projectId);

        await executionGuard.MustBeActiveAsync(projectId, cancellationToken);

        if (!await projectAccess.CanAccessAsync(actorId, projectId, cancellationToken))
            throw new ForbiddenException("You cannot access this project's meetings.");

        if (!currentUser.Roles.Contains(AppRoles.Admin) && !await repository.CanScheduleMeetingAsync(projectId, actorId, cancellationToken))
            throw new ForbiddenException("Only the team leader or assigned supervisor can schedule meetings.");

        if (command.Request.ParticipantUserIds != null)
        {
            foreach (var userId in command.Request.ParticipantUserIds)
            {
                if (!await repository.IsProjectMemberOrSupervisorAsync(projectId, userId, cancellationToken))
                    throw new ConflictException($"User {userId} is not an active member or supervisor of this project.");
            }
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var result = await repository.CreateAsync(
            projectId,
            actorId,
            command.Request.Title,
            command.Request.Agenda,
            command.Request.StartAt,
            command.Request.EndAt,
            command.Request.Location,
            command.Request.OnlineUrl,
            command.Request.ParticipantUserIds,
            now,
            async created =>
            {
                await audit.RecordAsync(new AuditEntry(
                    actorId,
                    "MEETING_SCHEDULED",
                    "MEETING",
                    created.Id,
                    new Dictionary<string, object?>
                    {
                        ["projectId"] = projectId,
                        ["title"] = created.Title,
                        ["startAt"] = created.StartAt.ToString("o")
                    }), cancellationToken);
            },
            cancellationToken);

        return result;
    }
}
