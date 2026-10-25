using System;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.ProgressReports.Abstractions;
using AIPMS.Application.Features.ProgressReports.DTOs;
using MediatR;

namespace AIPMS.Application.Features.ProgressReports.Queries;

public sealed record GetProgressReportPrefillQuery(
    long ProjectId,
    long? ProgressReportPeriodId = null) : IRequest<ProgressReportPrefillDto>;

public sealed class GetProgressReportPrefillQueryHandler(
    IProgressReportRepository repository,
    IReportingCycleRepository cycleRepository,
    IProjectAccessService projectAccess,
    IProjectExecutionGuard executionGuard,
    ICurrentUser currentUser,
    TimeProvider clock) : IRequestHandler<GetProgressReportPrefillQuery, ProgressReportPrefillDto>
{
    public async Task<ProgressReportPrefillDto> Handle(GetProgressReportPrefillQuery query, CancellationToken cancellationToken)
    {
        var actorId = currentUser.UserId ?? throw new UnauthorizedException("User is not authenticated.");
        var projectId = query.ProjectId;

        if (!await repository.ProjectExistsAsync(projectId, cancellationToken))
            throw new NotFoundException("Project", projectId);

        await executionGuard.MustBeActiveAsync(projectId, cancellationToken);

        if (!await projectAccess.CanAccessAsync(actorId, projectId, cancellationToken))
            throw new ForbiddenException("You cannot access this project's progress reports.");

        DateOnly periodStart;
        DateOnly periodEnd;
        string reportType;

        if (query.ProgressReportPeriodId.HasValue)
        {
            var cycle = await cycleRepository.GetCycleHeaderAsync(query.ProgressReportPeriodId.Value, cancellationToken)
                ?? throw new NotFoundException("ProgressReportPeriod", query.ProgressReportPeriodId.Value);

            if (cycle.ProjectId != projectId)
                throw new NotFoundException("ProgressReportPeriod", query.ProgressReportPeriodId.Value);

            periodStart = DateOnly.FromDateTime(cycle.PeriodStart);
            periodEnd = DateOnly.FromDateTime(cycle.PeriodEnd);
            reportType = cycle.ReportType;
        }
        else
        {
            var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
            var dayOfWeek = (int)today.DayOfWeek;
            var mondayOffset = dayOfWeek == 0 ? -6 : 1 - dayOfWeek;
            periodStart = today.AddDays(mondayOffset);
            periodEnd = periodStart.AddDays(6);
            reportType = "WEEKLY";
        }

        return await repository.GetPrefillDataAsync(
            projectId,
            query.ProgressReportPeriodId,
            periodStart,
            periodEnd,
            reportType,
            cancellationToken);
    }
}
