using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Academic.Abstractions;
using AIPMS.Application.Features.Academic.DTOs;
using MediatR;

namespace AIPMS.Application.Features.Academic.Queries;

public sealed record GetOrganizationsQuery(
    string? Search,
    bool? IsActive,
    int Page = 1,
    int PageSize = 20) : IRequest<PagedResult<OrganizationDto>>;

public sealed class GetOrganizationsQueryHandler(
    IAcademicStructureRepository repository)
    : IRequestHandler<GetOrganizationsQuery, PagedResult<OrganizationDto>>
{
    public async Task<PagedResult<OrganizationDto>> Handle(
        GetOrganizationsQuery request,
        CancellationToken cancellationToken)
    {
        var result = await repository.GetOrganizationsAsync(
            request.Search?.Trim(),
            request.IsActive,
            request.Page,
            request.PageSize,
            cancellationToken);

        return new PagedResult<OrganizationDto>(
            result.Items.Select(static organization => organization.ToDto()).ToArray(),
            result.Page,
            result.PageSize,
            result.TotalCount);
    }
}

public sealed record GetOrganizationByIdQuery(long OrganizationId)
    : IRequest<OrganizationDto>;

public sealed class GetOrganizationByIdQueryHandler(
    IAcademicStructureRepository repository)
    : IRequestHandler<GetOrganizationByIdQuery, OrganizationDto>
{
    public async Task<OrganizationDto> Handle(
        GetOrganizationByIdQuery request,
        CancellationToken cancellationToken)
    {
        var organization = await repository.GetOrganizationAsync(
            request.OrganizationId,
            cancellationToken);

        return organization?.ToDto()
            ?? throw new NotFoundException("Organization", request.OrganizationId);
    }
}
