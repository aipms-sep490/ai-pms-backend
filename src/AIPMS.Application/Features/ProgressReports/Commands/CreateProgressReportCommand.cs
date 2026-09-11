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

public sealed record CreateProgressReportCommand(
    long ProjectId,
    CreateProgressReportRequest Request) : IRequest<ProgressReportDto>;

public sealed class CreateProgressReportCommandHandler(
    IProgressReportRepository repository,
    IProjectAccessService projectAccess,
    IProjectExecutionGuard executionGuard,
    ICurrentUser currentUser,
    IAuditTrail audit,
    TimeProvider clock) : IRequestHandler<CreateProgressReportCommand, ProgressReportDto>
{
    public async Task<ProgressReportDto> Handle(CreateProgressReportCommand command, CancellationToken cancellationToken)
    {
        var actorId = currentUser.UserId ?? throw new UnauthorizedException("User is not authenticated.");
        var projectId = command.ProjectId;

        if (!await repository.ProjectExistsAsync(projectId, cancellationToken))
            throw new NotFoundException("Project", projectId);

        await executionGuard.MustBeActiveAsync(projectId, cancellationToken);

        if (!await projectAccess.CanAccessAsync(actorId, projectId, cancellationToken))
            throw new ForbiddenException("You cannot access this project's progress reports.");

        if (await repository.ExistsForPeriodAsync(projectId, command.Request.ReportType, command.Request.PeriodStart, command.Request.PeriodEnd, null, cancellationToken))
            throw new ConflictException("A progress report for this project, type, and period already exists.");

        var now = clock.GetUtcNow().UtcDateTime;
        var result = await repository.CreateAsync(
            projectId,
            actorId,
            command.Request.ReportType,
            command.Request.PeriodStart,
            command.Request.PeriodEnd,
            command.Request.Summary,
            command.Request.CompletedWork,
            command.Request.PlannedWork,
            command.Request.IssuesAndRisks,
            now,
            cancellationToken);

        await audit.RecordAsync(new AuditEntry(
            actorId,
            "PROGRESS_REPORT_CREATED",
            "PROGRESS_REPORT",
            result.Id,
            new Dictionary<string, object?>
            {
                ["projectId"] = projectId,
                ["reportType"] = result.ReportType,
                ["periodStart"] = result.PeriodStart.ToString("yyyy-MM-dd"),
                ["periodEnd"] = result.PeriodEnd.ToString("yyyy-MM-dd")
            }), cancellationToken);

        return result;
    }
}
