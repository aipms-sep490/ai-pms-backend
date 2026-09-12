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

public sealed record CompleteMeetingCommand(long Id) : IRequest<MeetingDto>;

public sealed class CompleteMeetingCommandHandler(
    IMeetingRepository repository,
    IProjectAccessService projectAccess,
    IProjectExecutionGuard executionGuard,
    ICurrentUser currentUser,
    IAuditTrail audit,
    TimeProvider clock) : IRequestHandler<CompleteMeetingCommand, MeetingDto>
{
    public async Task<MeetingDto> Handle(CompleteMeetingCommand command, CancellationToken cancellationToken)
    {
        var actorId = currentUser.UserId ?? throw new UnauthorizedException("User is not authenticated.");
        var projectId = await repository.GetProjectIdAsync(command.Id, cancellationToken)
            ?? throw new NotFoundException("Meeting", command.Id);

        await executionGuard.MustBeActiveAsync(projectId, cancellationToken);

        if (!await projectAccess.CanAccessAsync(actorId, projectId, cancellationToken))
            throw new ForbiddenException("You cannot access this project's meetings.");

        if (!currentUser.Roles.Contains(AppRoles.Admin) && !await repository.CanManageMeetingAsync(command.Id, actorId, cancellationToken))
            throw new ForbiddenException("Only the meeting organizer, team leader, or assigned supervisor can complete this meeting.");

        var status = await repository.GetStatusAsync(command.Id, cancellationToken);
        if (status == "COMPLETED")
            throw new ConflictException("Meeting is already completed.");

        if (status == "CANCELLED")
            throw new ConflictException("Cannot complete a meeting that has been cancelled.");

        if (status != "SCHEDULED")
            throw new ConflictException($"Meeting with status '{status}' cannot be completed.");

        var now = clock.GetUtcNow().UtcDateTime;
        var result = await repository.CompleteAsync(command.Id, now, cancellationToken);

        await audit.RecordAsync(new AuditEntry(
            actorId,
            "MEETING_COMPLETED",
            "MEETING",
            result.Id,
            new Dictionary<string, object?>
            {
                ["projectId"] = projectId,
                ["status"] = result.Status
            }), cancellationToken);

        return result;
    }
}
