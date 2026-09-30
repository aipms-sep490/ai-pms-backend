using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.ProgressReports.Abstractions;
using AIPMS.Application.Features.ProgressReports.DTOs;
using MediatR;

namespace AIPMS.Application.Features.ProgressReports.Commands;

public sealed record CreateReportingCycleCommand(
    long ProjectId,
    CreateReportingCycleRequest Request) : IRequest<ReportingCycleDto>;

public sealed class CreateReportingCycleCommandHandler(
    IReportingCycleRepository repository,
    IProgressReportRepository progressReportRepository,
    IProjectAccessService projectAccess,
    IProjectExecutionGuard executionGuard,
    ICurrentUser currentUser,
    IAuditTrail audit,
    TimeProvider clock) : IRequestHandler<CreateReportingCycleCommand, ReportingCycleDto>
{
    public async Task<ReportingCycleDto> Handle(CreateReportingCycleCommand command, CancellationToken cancellationToken)
    {
        var actorId = currentUser.UserId ?? throw new UnauthorizedException("User is not authenticated.");
        var projectId = command.ProjectId;

        if (!await progressReportRepository.ProjectExistsAsync(projectId, cancellationToken))
            throw new NotFoundException("Project", projectId);

        await executionGuard.MustBeActiveAsync(projectId, cancellationToken);

        var isAdmin = await repository.HasAdminRoleInDbAsync(actorId, cancellationToken);
        var isStaff = await repository.HasStaffRoleInDbAsync(actorId, cancellationToken);

        if (!isAdmin && !isStaff)
            throw new ForbiddenException("Only Admin or Department Staff can create reporting cycles.");

        if (isStaff && !isAdmin)
        {
            if (!await projectAccess.CanAccessAsync(actorId, projectId, cancellationToken))
                throw new ForbiddenException("You can only create reporting cycles for projects in your department.");
        }

        var req = command.Request;
        var reportType = req.ReportType?.Trim().ToUpperInvariant() ?? "";
        if (reportType != "WEEKLY" && reportType != "MONTHLY")
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["reportType"] = ["Report type must be either WEEKLY or MONTHLY."]
            });
        }

        var latePolicy = string.IsNullOrWhiteSpace(req.LatePolicy) ? "BLOCK" : req.LatePolicy.Trim().ToUpperInvariant();
        if (latePolicy != "BLOCK" && latePolicy != "FLAG")
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["latePolicy"] = ["Late policy must be either BLOCK or FLAG."]
            });
        }

        long projectPeriodId;
        if (req.ProjectPeriodId.HasValue)
        {
            if (!await repository.IsValidProjectPeriodAsync(projectId, req.ProjectPeriodId.Value, cancellationToken))
                throw new ValidationException(new Dictionary<string, string[]>
                {
                    ["projectPeriodId"] = ["The specified project period does not belong to this project's academic semester."]
                });
            projectPeriodId = req.ProjectPeriodId.Value;
        }
        else
        {
            var defaultPeriodId = await repository.GetDefaultProjectPeriodIdAsync(projectId, cancellationToken);
            if (!defaultPeriodId.HasValue)
                throw new ValidationException(new Dictionary<string, string[]>
                {
                    ["projectPeriodId"] = ["No active project period found for this project's academic semester."]
                });
            projectPeriodId = defaultPeriodId.Value;
        }

        // Normalize all date-time instants to UTC before persistence and overlap checks.
        var periodStart = req.PeriodStart.UtcDateTime;
        var periodEnd = req.PeriodEnd.UtcDateTime;
        var deadline = req.Deadline.UtcDateTime;

        if (periodEnd <= periodStart)
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["periodEnd"] = ["Period end must be strictly after period start."]
            });
        }

        if (await repository.ExistsOverlapAsync(projectId, reportType, periodStart, periodEnd, null, cancellationToken))
        {
            throw new ConflictException("A reporting cycle of this type already overlaps with the requested interval.");
        }

        var now = clock.GetUtcNow().UtcDateTime;

        return await repository.CreateAsync(
            projectId,
            projectPeriodId,
            reportType,
            periodStart,
            periodEnd,
            deadline,
            latePolicy,
            actorId,
            now,
            onCreated: async created =>
            {
                await audit.RecordAsync(new AuditEntry(
                    actorId,
                    "REPORTING_CYCLE_CREATED",
                    "PROGRESS_REPORT_PERIOD",
                    created.Id,
                    new Dictionary<string, object?>
                    {
                        ["projectId"] = projectId,
                        ["reportType"] = created.ReportType,
                        ["periodStart"] = created.PeriodStart.ToString("o"),
                        ["periodEnd"] = created.PeriodEnd.ToString("o"),
                        ["deadline"] = created.Deadline.ToString("o"),
                        ["latePolicy"] = created.LatePolicy
                    }), cancellationToken);
            },
            cancellationToken: cancellationToken);
    }
}
