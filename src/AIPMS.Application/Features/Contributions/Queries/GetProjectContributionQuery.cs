using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Contributions.DTOs;
using AIPMS.Application.Features.Contributions.Services;
using MediatR;

namespace AIPMS.Application.Features.Contributions.Queries;

public sealed record GetProjectContributionQuery(long ProjectId, int Page = 1, int PageSize = 20, bool Snapshot = false)
    : IRequest<ContributionSummaryDto>;
public sealed record GetContributionEvidenceQuery(long ProjectId, long UserId, int Page = 1, int PageSize = 20,
    string? SourceType = null) : IRequest<PagedResult<ContributionEvidenceDto>>;

public sealed class GetProjectContributionQueryHandler(ContributionWorkflow workflow)
    : IRequestHandler<GetProjectContributionQuery, ContributionSummaryDto>
{
    public Task<ContributionSummaryDto> Handle(GetProjectContributionQuery request, CancellationToken ct) =>
        workflow.Summary(request.ProjectId, request.Page, request.PageSize, request.Snapshot, ct);
}

public sealed class GetContributionEvidenceQueryHandler(ContributionWorkflow workflow)
    : IRequestHandler<GetContributionEvidenceQuery, PagedResult<ContributionEvidenceDto>>
{
    public Task<PagedResult<ContributionEvidenceDto>> Handle(GetContributionEvidenceQuery request, CancellationToken ct) =>
        workflow.Evidence(request.ProjectId, request.UserId, request.Page, request.PageSize, request.SourceType, ct);
}
