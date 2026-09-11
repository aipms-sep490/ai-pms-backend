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

public sealed record GetProgressReportsQuery(
    long ProjectId,
    string? ReportType = null,
    string? Status = null,
    DateOnly? From = null,
    DateOnly? To = null,
    int Page = 1,
    int PageSize = 20) : IRequest<PagedResult<ProgressReportDto>>;

public sealed class GetProgressReportsQueryHandler(
    IProgressReportRepository repository,
    IProjectAccessService projectAccess,
    ICurrentUser currentUser) : IRequestHandler<GetProgressReportsQuery, PagedResult<ProgressReportDto>>
{
    public async Task<PagedResult<ProgressReportDto>> Handle(GetProgressReportsQuery query, CancellationToken cancellationToken)
    {
        var actorId = currentUser.UserId ?? throw new UnauthorizedException("User is not authenticated.");
        var projectId = query.ProjectId;

        if (!await repository.ProjectExistsAsync(projectId, cancellationToken))
            throw new NotFoundException("Project", projectId);

        if (!await projectAccess.CanAccessAsync(actorId, projectId, cancellationToken))
            throw new ForbiddenException("You cannot access this project's progress reports.");

        return await repository.GetReportsAsync(
            projectId,
            query.ReportType,
            query.Status,
            query.From,
            query.To,
            query.Page,
            query.PageSize,
            cancellationToken);
    }
}
