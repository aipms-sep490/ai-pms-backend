using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Evaluations.DTOs;
using AIPMS.Application.Features.Evaluations.Services;
using MediatR;

namespace AIPMS.Application.Features.Evaluations.Queries;

public sealed record GetEvaluationDraftQuery(long Id) : IRequest<EvaluationDraftDto>;
public sealed record GetProjectEvaluationsQuery(long ProjectId, int Page = 1, int PageSize = 20) : IRequest<PagedResult<EvaluationDraftDto>>;
public sealed record GetEvaluationAssignmentsQuery(long? ProjectId = null, string? Status = null,
    int Page = 1, int PageSize = 20) : IRequest<PagedResult<EvaluationAssignmentDto>>;

public sealed class GetEvaluationDraftQueryHandler(EvaluationDraftWorkflow workflow) : IRequestHandler<GetEvaluationDraftQuery, EvaluationDraftDto>
{
    public Task<EvaluationDraftDto> Handle(GetEvaluationDraftQuery r, CancellationToken ct) => workflow.Get(r.Id, ct);
}
public sealed class GetProjectEvaluationsQueryHandler(EvaluationDraftWorkflow workflow) : IRequestHandler<GetProjectEvaluationsQuery, PagedResult<EvaluationDraftDto>>
{
    public Task<PagedResult<EvaluationDraftDto>> Handle(GetProjectEvaluationsQuery r, CancellationToken ct) => workflow.List(r.ProjectId, r.Page, r.PageSize, ct);
}
public sealed class GetEvaluationAssignmentsQueryHandler(EvaluationDraftWorkflow workflow) : IRequestHandler<GetEvaluationAssignmentsQuery, PagedResult<EvaluationAssignmentDto>>
{
    public Task<PagedResult<EvaluationAssignmentDto>> Handle(GetEvaluationAssignmentsQuery r, CancellationToken ct) => workflow.Assignments(r.ProjectId, r.Status, r.Page, r.PageSize, ct);
}
