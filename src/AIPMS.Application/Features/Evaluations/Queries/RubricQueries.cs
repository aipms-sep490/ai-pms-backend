using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Evaluations.DTOs;
using AIPMS.Application.Features.Evaluations.Models;
using AIPMS.Application.Features.Evaluations.Services;
using MediatR;

namespace AIPMS.Application.Features.Evaluations.Queries;

public sealed record GetRubricQuery(long Id) : IRequest<RubricDto>;
public sealed record GetRubricsQuery(long? DepartmentId = null, long? AcademicSemesterId = null,
    string? Status = null, string? Search = null, int Page = 1, int PageSize = 20) : IRequest<PagedResult<RubricDto>>;
public sealed class GetRubricQueryHandler(RubricWorkflow workflow) : IRequestHandler<GetRubricQuery, RubricDto>
{
    public Task<RubricDto> Handle(GetRubricQuery r, CancellationToken ct) => workflow.Get(r.Id, ct);
}
public sealed class GetRubricsQueryHandler(RubricWorkflow workflow) : IRequestHandler<GetRubricsQuery, PagedResult<RubricDto>>
{
    public Task<PagedResult<RubricDto>> Handle(GetRubricsQuery r, CancellationToken ct) => workflow.List(
        new RubricFilter(r.DepartmentId, r.AcademicSemesterId, r.Status, r.Search?.Trim(), r.Page, r.PageSize), ct);
}
