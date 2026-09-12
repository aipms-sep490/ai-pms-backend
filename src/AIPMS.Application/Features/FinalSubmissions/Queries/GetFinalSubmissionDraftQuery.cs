using AIPMS.Application.Features.FinalSubmissions.DTOs;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.FinalSubmissions.Services;
using MediatR;

namespace AIPMS.Application.Features.FinalSubmissions.Queries;

public sealed record GetFinalSubmissionDraftQuery(long ProjectId) : IRequest<FinalSubmissionDraftDto>;
public sealed class GetFinalSubmissionDraftHandler(FinalSubmissionDraftWorkflow workflow)
    : IRequestHandler<GetFinalSubmissionDraftQuery, FinalSubmissionDraftDto>
{
    public Task<FinalSubmissionDraftDto> Handle(GetFinalSubmissionDraftQuery request, CancellationToken ct) => workflow.Get(request.ProjectId, ct);
}

public sealed record GetFinalSubmissionPeriodsQuery(long ProjectId, int Page = 1, int PageSize = 20)
    : IRequest<PagedResult<FinalSubmissionPeriodOptionDto>>;
public sealed class GetFinalSubmissionPeriodsHandler(FinalSubmissionDraftWorkflow workflow)
    : IRequestHandler<GetFinalSubmissionPeriodsQuery, PagedResult<FinalSubmissionPeriodOptionDto>>
{
    public Task<PagedResult<FinalSubmissionPeriodOptionDto>> Handle(GetFinalSubmissionPeriodsQuery request, CancellationToken ct) =>
        workflow.Periods(request.ProjectId, request.Page, request.PageSize, ct);
}
