using System;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.ProgressReports.Abstractions;
using AIPMS.Application.Features.ProgressReports.DTOs;
using MediatR;

namespace AIPMS.Application.Features.ProgressReports.Queries;

public sealed record GetReportingCyclesQuery(
    long ProjectId,
    string? ReportType = null,
    DateTime? From = null,
    DateTime? To = null,
    int Page = 1,
    int PageSize = 20) : IRequest<PagedResult<ReportingCycleDto>>;

public sealed class GetReportingCyclesQueryHandler(
    IReportingCycleRepository repository,
    IProgressReportRepository progressReportRepository,
    IProjectAccessService projectAccess,
    ICurrentUser currentUser) : IRequestHandler<GetReportingCyclesQuery, PagedResult<ReportingCycleDto>>
{
    public async Task<PagedResult<ReportingCycleDto>> Handle(GetReportingCyclesQuery query, CancellationToken cancellationToken)
    {
        var actorId = currentUser.UserId ?? throw new UnauthorizedException("User is not authenticated.");
        var projectId = query.ProjectId;

        if (!await progressReportRepository.ProjectExistsAsync(projectId, cancellationToken))
            throw new NotFoundException("Project", projectId);

        if (!await projectAccess.CanAccessAsync(actorId, projectId, cancellationToken))
            throw new ForbiddenException("You cannot access this project's reporting cycles.");

        return await repository.ListAsync(
            projectId,
            query.ReportType,
            query.From,
            query.To,
            query.Page,
            query.PageSize,
            cancellationToken);
    }
}

public sealed record GetReportingCycleByIdQuery(
    long ProjectId,
    long CycleId) : IRequest<ReportingCycleDto>;

public sealed class GetReportingCycleByIdQueryHandler(
    IReportingCycleRepository repository,
    IProgressReportRepository progressReportRepository,
    IProjectAccessService projectAccess,
    ICurrentUser currentUser) : IRequestHandler<GetReportingCycleByIdQuery, ReportingCycleDto>
{
    public async Task<ReportingCycleDto> Handle(GetReportingCycleByIdQuery query, CancellationToken cancellationToken)
    {
        var actorId = currentUser.UserId ?? throw new UnauthorizedException("User is not authenticated.");
        var projectId = query.ProjectId;

        if (!await progressReportRepository.ProjectExistsAsync(projectId, cancellationToken))
            throw new NotFoundException("Project", projectId);

        if (!await projectAccess.CanAccessAsync(actorId, projectId, cancellationToken))
            throw new ForbiddenException("You cannot access this project's reporting cycles.");

        var cycle = await repository.GetByIdAsync(query.CycleId, cancellationToken)
            ?? throw new NotFoundException("ProgressReportPeriod", query.CycleId);

        if (cycle.ProjectId != projectId)
            throw new NotFoundException("ProgressReportPeriod", query.CycleId);

        return cycle;
    }
}
