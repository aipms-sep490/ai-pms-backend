using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Evaluations.DTOs;
using AIPMS.Application.Features.Evaluations.Services;
using MediatR;

namespace AIPMS.Application.Features.Evaluations.Queries;

public sealed record GetEligibleEvaluatorsQuery(long ProjectId, long PeriodId, int Page = 1, int PageSize = 20)
    : IRequest<PagedResult<EligibleEvaluatorDto>>;

public sealed class GetEligibleEvaluatorsQueryHandler(EvaluationDraftWorkflow workflow)
    : IRequestHandler<GetEligibleEvaluatorsQuery, PagedResult<EligibleEvaluatorDto>>
{
    public Task<PagedResult<EligibleEvaluatorDto>> Handle(GetEligibleEvaluatorsQuery request, CancellationToken ct) =>
        workflow.Candidates(request.ProjectId, request.PeriodId, request.Page, request.PageSize, ct);
}

