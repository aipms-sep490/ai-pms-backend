using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Supervisors.Abstractions;
using AIPMS.Application.Features.Supervisors.DTOs;
using AIPMS.Application.Features.Supervisors.Models;
using AIPMS.Application.Features.Supervisors.Services;
using MediatR;

namespace AIPMS.Application.Features.Supervisors.Queries;

public sealed record GetSupervisorsQuery(long? DepartmentId = null, string? Search = null,
    string? Expertise = null, bool? IsAvailable = null, int Page = 1, int PageSize = 20)
    : IRequest<PagedResult<SupervisorProfileDto>>;

public sealed class GetSupervisorsQueryHandler(ISupervisorProfileRepository repository, SupervisorAccessService access)
    : IRequestHandler<GetSupervisorsQuery, PagedResult<SupervisorProfileDto>>
{
    public async Task<PagedResult<SupervisorProfileDto>> Handle(GetSupervisorsQuery request, CancellationToken ct)
    {
        await access.EnsureCanReadAsync(ct);
        var result = await repository.SearchAsync(new SupervisorSearch(request.DepartmentId,
            request.Search?.Trim(), request.Expertise?.Trim(), request.IsAvailable, request.Page, request.PageSize), ct);
        return new(result.Items.Select(p => p.ToDto()).ToArray(), result.Page, result.PageSize, result.TotalCount);
    }
}
