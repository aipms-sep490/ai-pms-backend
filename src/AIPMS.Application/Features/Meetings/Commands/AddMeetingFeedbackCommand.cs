using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Meetings.Abstractions;
using AIPMS.Application.Features.Meetings.DTOs;
using MediatR;

namespace AIPMS.Application.Features.Meetings.Commands;

public sealed record AddMeetingFeedbackCommand(
    long Id,
    AddMeetingFeedbackRequest Request) : IRequest<MeetingFeedbackDto>;

public sealed class AddMeetingFeedbackCommandHandler(
    IMeetingRepository repository,
    IProjectExecutionGuard executionGuard,
    ICurrentUser currentUser,
    IAuditTrail audit,
    TimeProvider clock) : IRequestHandler<AddMeetingFeedbackCommand, MeetingFeedbackDto>
{
    public async Task<MeetingFeedbackDto> Handle(AddMeetingFeedbackCommand command, CancellationToken cancellationToken)
    {
        var actorId = currentUser.UserId ?? throw new UnauthorizedException("User is not authenticated.");
        var projectId = await repository.GetProjectIdAsync(command.Id, cancellationToken)
            ?? throw new NotFoundException("Meeting", command.Id);

        await executionGuard.MustBeActiveAsync(projectId, cancellationToken);

        var assignmentId = await repository.GetActiveSupervisorAssignmentIdAsync(projectId, actorId, cancellationToken);
        if (!assignmentId.HasValue)
            throw new ForbiddenException("Only the active assigned supervisor can provide feedback.");

        var now = clock.GetUtcNow().UtcDateTime;
        var feedback = await repository.AddFeedbackAsync(
            command.Id,
            assignmentId.Value,
            command.Request.FeedbackText,
            now,
            cancellationToken);

        await audit.RecordAsync(new AuditEntry(
            actorId,
            "MEETING_FEEDBACK_ADDED",
            "MEETING",
            command.Id,
            new Dictionary<string, object?>
            {
                ["projectId"] = projectId,
                ["feedbackId"] = feedback.Id,
                ["supervisorAssignmentId"] = assignmentId.Value
            }), cancellationToken);

        return feedback;
    }
}
