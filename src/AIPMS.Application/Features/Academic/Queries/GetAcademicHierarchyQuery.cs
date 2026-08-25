using AIPMS.Application.Features.Academic.Abstractions;
using AIPMS.Application.Features.Academic.DTOs;
using MediatR;

namespace AIPMS.Application.Features.Academic.Queries;

public sealed record GetAcademicHierarchyQuery(
    long? OrganizationId,
    string? Search,
    bool IncludeInactive = false)
    : IRequest<IReadOnlyList<AcademicHierarchyOrganizationDto>>;

public sealed class GetAcademicHierarchyQueryHandler(
    IAcademicStructureRepository repository)
    : IRequestHandler<
        GetAcademicHierarchyQuery,
        IReadOnlyList<AcademicHierarchyOrganizationDto>>
{
    public async Task<IReadOnlyList<AcademicHierarchyOrganizationDto>> Handle(
        GetAcademicHierarchyQuery request,
        CancellationToken cancellationToken)
    {
        var hierarchy = await repository.GetHierarchyAsync(
            request.OrganizationId,
            request.Search?.Trim(),
            request.IncludeInactive,
            cancellationToken);

        return hierarchy.Select(static organization => organization.ToDto()).ToArray();
    }
}
