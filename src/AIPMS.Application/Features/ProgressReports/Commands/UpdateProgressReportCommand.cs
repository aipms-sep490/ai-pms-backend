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

public sealed record UpdateProgressReportCommand(
    long Id,
    UpdateProgressReportRequest Request) : IRequest<ProgressReportDto>;

public sealed class UpdateProgressReportCommandHandler(
    IProgressReportRepository repository,
    IProjectAccessService projectAccess,
    IProjectExecutionGuard executionGuard,
    ICurrentUser currentUser,
    IAuditTrail audit,
    TimeProvider clock) : IRequestHandler<UpdateProgressReportCommand, ProgressReportDto>
{
    public async Task<ProgressReportDto> Handle(UpdateProgressReportCommand command, CancellationToken cancellationToken)
    {
        var actorId = currentUser.UserId ?? throw new UnauthorizedException("User is not authenticated.");
        var projectId = await repository.GetProjectIdAsync(command.Id, cancellationToken)
            ?? throw new NotFoundException("ProgressReport", command.Id);

        await executionGuard.MustBeActiveAsync(projectId, cancellationToken);

        if (!await projectAccess.CanAccessAsync(actorId, projectId, cancellationToken))
            throw new ForbiddenException("You cannot access this project's progress reports.");

        var status = await repository.GetStatusAsync(command.Id, cancellationToken);
        if (status != "DRAFT")
            throw new ConflictException("Submitted or reviewed progress reports cannot be modified.");

        var now = clock.GetUtcNow().UtcDateTime;
        var result = await repository.UpdateAsync(
            command.Id,
            command.Request.Summary,
            command.Request.CompletedWork,
            command.Request.PlannedWork,
            command.Request.IssuesAndRisks,
            now,
            cancellationToken);

        await audit.RecordAsync(new AuditEntry(
            actorId,
            "PROGRESS_REPORT_UPDATED",
            "PROGRESS_REPORT",
            result.Id,
            new Dictionary<string, object?>
            {
                ["projectId"] = projectId,
                ["status"] = result.Status
            }), cancellationToken);

        return result;
    }
}
