using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Supervisors.DTOs;
using AIPMS.Application.Features.Supervisors.Services;
using MediatR;

namespace AIPMS.Application.Features.Supervisors.Queries;

public sealed record GetSupervisorRequestsQuery(long? ProjectId = null, string? Status = null,
    int Page = 1, int PageSize = 20) : IRequest<PagedResult<SupervisorRequestDto>>;

public sealed class GetSupervisorRequestsQueryHandler(SupervisorRequestWorkflow workflow)
    : IRequestHandler<GetSupervisorRequestsQuery, PagedResult<SupervisorRequestDto>>
{
    public Task<PagedResult<SupervisorRequestDto>> Handle(GetSupervisorRequestsQuery request, CancellationToken ct) =>
        workflow.ListAsync(request.ProjectId, request.Status, request.Page, request.PageSize, ct);
}
