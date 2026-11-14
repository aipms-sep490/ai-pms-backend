using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.Semesters.Abstractions;
using AIPMS.Application.Features.Semesters.DTOs;
using MediatR;

namespace AIPMS.Application.Features.Semesters.Queries;

// ── Get Semesters (paged list) ────────────────────────────────────────────────

public sealed record GetSemestersQuery(
    long? OrganizationId,
    string? Search,
    string? Status,
    int Page = 1,
    int PageSize = 20) : IRequest<PagedResult<SemesterDto>>;

public sealed class GetSemestersQueryHandler(ISemesterRepository repository, IAcademicDirectoryScopeReader scopeReader)
    : IRequestHandler<GetSemestersQuery, PagedResult<SemesterDto>>
{
    public async Task<PagedResult<SemesterDto>> Handle(
        GetSemestersQuery request,
        CancellationToken cancellationToken)
    {
        var scope = await scopeReader.GetAsync(cancellationToken);
        var result = await repository.GetSemestersAsync(
            request.OrganizationId,
            request.Search?.Trim(),
            request.Status,
            request.Page,
            request.PageSize,
            cancellationToken,
            scope.OrganizationId);

        return new PagedResult<SemesterDto>(
            result.Items.Select(static s => s.ToDto()).ToArray(),
            result.Page,
            result.PageSize,
            result.TotalCount);
    }
}

// ── Get Semester by Id ────────────────────────────────────────────────────────

public sealed record GetSemesterByIdQuery(long SemesterId) : IRequest<SemesterDto>;

public sealed class GetSemesterByIdQueryHandler(ISemesterRepository repository, IAcademicDirectoryScopeReader scopeReader)
    : IRequestHandler<GetSemesterByIdQuery, SemesterDto>
{
    public async Task<SemesterDto> Handle(
        GetSemesterByIdQuery request,
        CancellationToken cancellationToken)
    {
        var scope = await scopeReader.GetAsync(cancellationToken);
        var semester = await repository.GetSemesterAsync(
            request.SemesterId,
            cancellationToken,
            scope.OrganizationId);

        return semester?.ToDto()
            ?? throw new NotFoundException("AcademicSemester", request.SemesterId);
    }
}

// ── Get ProjectPeriods (paged list) ───────────────────────────────────────────

public sealed record GetProjectPeriodsQuery(
    long? SemesterId,
    string? Search,
    string? Status,
    string? PeriodType,
    int Page = 1,
    int PageSize = 20) : IRequest<PagedResult<ProjectPeriodDto>>;

public sealed class GetProjectPeriodsQueryHandler(ISemesterRepository repository, IAcademicDirectoryScopeReader scopeReader)
    : IRequestHandler<GetProjectPeriodsQuery, PagedResult<ProjectPeriodDto>>
{
    public async Task<PagedResult<ProjectPeriodDto>> Handle(
        GetProjectPeriodsQuery request,
        CancellationToken cancellationToken)
    {
        var scope = await scopeReader.GetAsync(cancellationToken);
        var result = await repository.GetProjectPeriodsAsync(
            request.SemesterId,
            request.Search?.Trim(),
            request.Status,
            request.PeriodType,
            request.Page,
            request.PageSize,
            cancellationToken,
            scope.OrganizationId);

        return new PagedResult<ProjectPeriodDto>(
            result.Items.Select(static p => p.ToDto()).ToArray(),
            result.Page,
            result.PageSize,
            result.TotalCount);
    }
}

// ── Get ProjectPeriod by Id ───────────────────────────────────────────────────

public sealed record GetProjectPeriodByIdQuery(long PeriodId) : IRequest<ProjectPeriodDto>;

public sealed class GetProjectPeriodByIdQueryHandler(ISemesterRepository repository, IAcademicDirectoryScopeReader scopeReader)
    : IRequestHandler<GetProjectPeriodByIdQuery, ProjectPeriodDto>
{
    public async Task<ProjectPeriodDto> Handle(
        GetProjectPeriodByIdQuery request,
        CancellationToken cancellationToken)
    {
        var scope = await scopeReader.GetAsync(cancellationToken);
        var period = await repository.GetProjectPeriodAsync(
            request.PeriodId,
            cancellationToken,
            scope.OrganizationId);

        return period?.ToDto()
            ?? throw new NotFoundException("ProjectPeriod", request.PeriodId);
    }
}
