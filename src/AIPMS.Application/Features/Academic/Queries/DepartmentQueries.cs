using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Academic.Abstractions;
using AIPMS.Application.Features.Academic.DTOs;
using MediatR;

namespace AIPMS.Application.Features.Academic.Queries;

public sealed record GetDepartmentsQuery(
    long? OrganizationId,
    string? Search,
    bool? IsActive,
    int Page = 1,
    int PageSize = 20) : IRequest<PagedResult<DepartmentDto>>;

public sealed class GetDepartmentsQueryHandler(
    IAcademicStructureRepository repository)
    : IRequestHandler<GetDepartmentsQuery, PagedResult<DepartmentDto>>
{
    public async Task<PagedResult<DepartmentDto>> Handle(
        GetDepartmentsQuery request,
        CancellationToken cancellationToken)
    {
        var result = await repository.GetDepartmentsAsync(
            request.OrganizationId,
            request.Search?.Trim(),
            request.IsActive,
            request.Page,
            request.PageSize,
            cancellationToken);

        return new PagedResult<DepartmentDto>(
            result.Items.Select(static department => department.ToDto()).ToArray(),
            result.Page,
            result.PageSize,
            result.TotalCount);
    }
}

public sealed record GetDepartmentByIdQuery(long DepartmentId)
    : IRequest<DepartmentDto>;

public sealed class GetDepartmentByIdQueryHandler(
    IAcademicStructureRepository repository)
    : IRequestHandler<GetDepartmentByIdQuery, DepartmentDto>
{
    public async Task<DepartmentDto> Handle(
        GetDepartmentByIdQuery request,
        CancellationToken cancellationToken)
    {
        var department = await repository.GetDepartmentAsync(
            request.DepartmentId,
            cancellationToken);

        return department?.ToDto()
            ?? throw new NotFoundException("Department", request.DepartmentId);
    }
}
