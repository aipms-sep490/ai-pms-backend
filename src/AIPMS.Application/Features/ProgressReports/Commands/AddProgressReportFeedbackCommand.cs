using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.ProgressReports.Abstractions;
using AIPMS.Application.Features.ProgressReports.DTOs;
using MediatR;

namespace AIPMS.Application.Features.ProgressReports.Commands;

public sealed record AddProgressReportFeedbackCommand(
    long Id,
    AddProgressReportFeedbackRequest Request) : IRequest<ProgressReportFeedbackDto>;

public sealed class AddProgressReportFeedbackCommandHandler(
    IProgressReportRepository repository,
    IProjectExecutionGuard executionGuard,
    ICurrentUser currentUser,
    IAuditTrail audit,
    TimeProvider clock) : IRequestHandler<AddProgressReportFeedbackCommand, ProgressReportFeedbackDto>
{
    public async Task<ProgressReportFeedbackDto> Handle(AddProgressReportFeedbackCommand command, CancellationToken cancellationToken)
    {
        var actorId = currentUser.UserId ?? throw new UnauthorizedException("User is not authenticated.");
        var projectId = await repository.GetProjectIdAsync(command.Id, cancellationToken)
            ?? throw new NotFoundException("ProgressReport", command.Id);

        await executionGuard.MustBeActiveAsync(projectId, cancellationToken);

        var assignmentId = await repository.GetActiveSupervisorAssignmentIdAsync(projectId, actorId, cancellationToken);
        if (!assignmentId.HasValue)
            throw new ForbiddenException("Only the active assigned supervisor can provide feedback.");

        var status = await repository.GetStatusAsync(command.Id, cancellationToken);
        if (status == "DRAFT")
            throw new ConflictException("Cannot provide feedback on a draft progress report.");

        var now = clock.GetUtcNow().UtcDateTime;
        var feedback = await repository.AddFeedbackAsync(
            command.Id,
            assignmentId.Value,
            command.Request.FeedbackText,
            now,
            cancellationToken);

        await audit.RecordAsync(new AuditEntry(
            actorId,
            "PROGRESS_REPORT_FEEDBACK_ADDED",
            "PROGRESS_REPORT",
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
