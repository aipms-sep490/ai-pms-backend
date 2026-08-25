using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Academic.Abstractions;
using AIPMS.Application.Features.Academic.DTOs;
using MediatR;

namespace AIPMS.Application.Features.Academic.Queries;

public sealed record GetMajorsQuery(
    long? OrganizationId,
    long? DepartmentId,
    string? Search,
    bool? IsActive,
    int Page = 1,
    int PageSize = 20) : IRequest<PagedResult<MajorDto>>;

public sealed class GetMajorsQueryHandler(
    IAcademicStructureRepository repository)
    : IRequestHandler<GetMajorsQuery, PagedResult<MajorDto>>
{
    public async Task<PagedResult<MajorDto>> Handle(
        GetMajorsQuery request,
        CancellationToken cancellationToken)
    {
        var result = await repository.GetMajorsAsync(
            request.OrganizationId,
            request.DepartmentId,
            request.Search?.Trim(),
            request.IsActive,
            request.Page,
            request.PageSize,
            cancellationToken);

        return new PagedResult<MajorDto>(
            result.Items.Select(static major => major.ToDto()).ToArray(),
            result.Page,
            result.PageSize,
            result.TotalCount);
    }
}

public sealed record GetMajorByIdQuery(long MajorId) : IRequest<MajorDto>;

public sealed class GetMajorByIdQueryHandler(
    IAcademicStructureRepository repository)
    : IRequestHandler<GetMajorByIdQuery, MajorDto>
{
    public async Task<MajorDto> Handle(
        GetMajorByIdQuery request,
        CancellationToken cancellationToken)
    {
        var major = await repository.GetMajorAsync(request.MajorId, cancellationToken);
        return major?.ToDto() ?? throw new NotFoundException("Major", request.MajorId);
    }
}
