using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Teams.Abstractions;
using AIPMS.Application.Features.Teams.DTOs;
using AIPMS.Application.Features.Teams.Services;
using MediatR;

namespace AIPMS.Application.Features.Teams.Queries;

public sealed record GetTeamInvitationCandidatesQuery(long TeamId, string? Search = null,
    int Page = 1, int PageSize = 20) : IRequest<PagedResult<TeamInvitationCandidateDto>>;

public sealed class GetTeamInvitationCandidatesQueryHandler(TeamWorkflow workflow, ITeamInvitationCandidateReader reader)
    : IRequestHandler<GetTeamInvitationCandidatesQuery, PagedResult<TeamInvitationCandidateDto>>
{
    public async Task<PagedResult<TeamInvitationCandidateDto>> Handle(GetTeamInvitationCandidatesQuery request, CancellationToken cancellationToken)
    {
        var scope = await workflow.GetInvitationCandidateScopeAsync(request.TeamId, cancellationToken);
        var result = await reader.SearchAsync(scope, request.Search?.Trim(), request.Page, request.PageSize, cancellationToken);
        return new(result.Items.Select(candidate => candidate.ToDto()).ToArray(), result.Page, result.PageSize, result.TotalCount);
    }
}
