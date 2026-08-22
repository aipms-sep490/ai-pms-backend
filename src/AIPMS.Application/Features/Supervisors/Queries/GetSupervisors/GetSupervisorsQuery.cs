using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Supervisors.Abstractions;
using AIPMS.Application.Features.Supervisors.DTOs;
using MediatR;

namespace AIPMS.Application.Features.Supervisors.Queries.GetSupervisors;

public sealed record GetSupervisorsQuery(
    int PageNumber,
    int PageSize,
    string? Search,
    bool? IsAvailable,
    string? Expertise) : IRequest<PagedResult<SupervisorDto>>;

public sealed class GetSupervisorsQueryHandler(ISupervisorRepository supervisorRepository)
    : IRequestHandler<GetSupervisorsQuery, PagedResult<SupervisorDto>>
{
    public Task<PagedResult<SupervisorDto>> Handle(
        GetSupervisorsQuery request,
        CancellationToken cancellationToken)
    {
        return supervisorRepository.GetPagedSupervisorsAsync(
            request.PageNumber,
            request.PageSize,
            request.Search,
            request.IsAvailable,
            request.Expertise,
            cancellationToken);
    }
}
