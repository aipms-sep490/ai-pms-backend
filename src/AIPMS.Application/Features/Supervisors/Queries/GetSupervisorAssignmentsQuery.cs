using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Supervisors.DTOs;
using AIPMS.Application.Features.Supervisors.Services;
using MediatR;

namespace AIPMS.Application.Features.Supervisors.Queries;

public sealed record GetSupervisorAssignmentsQuery(long? ProjectId, string? Status = null,
    int Page = 1, int PageSize = 20) : IRequest<PagedResult<SupervisorAssignmentDto>>;

public sealed record GetSupervisorAssignmentQuery(long AssignmentId) : IRequest<SupervisorAssignmentDto>;

public sealed class GetSupervisorAssignmentsQueryHandler(SupervisorAssignmentWorkflow workflow)
    : IRequestHandler<GetSupervisorAssignmentsQuery, PagedResult<SupervisorAssignmentDto>>
{
    public Task<PagedResult<SupervisorAssignmentDto>> Handle(GetSupervisorAssignmentsQuery request, CancellationToken ct) =>
        workflow.ListAsync(request.ProjectId, request.Status, request.Page, request.PageSize, ct);
}

public sealed class GetSupervisorAssignmentQueryHandler(SupervisorAssignmentWorkflow workflow)
    : IRequestHandler<GetSupervisorAssignmentQuery, SupervisorAssignmentDto>
{
    public Task<SupervisorAssignmentDto> Handle(GetSupervisorAssignmentQuery request, CancellationToken ct) =>
        workflow.GetAsync(request.AssignmentId, ct);
}
