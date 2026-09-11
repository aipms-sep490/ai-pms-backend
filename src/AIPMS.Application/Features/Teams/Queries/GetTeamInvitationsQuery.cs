using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Teams.DTOs;
using AIPMS.Application.Features.Teams.Services;
using MediatR;

namespace AIPMS.Application.Features.Teams.Queries;

public sealed record GetTeamInvitationsQuery(long? TeamId = null, int Page = 1, int PageSize = 20) : IRequest<PagedResult<TeamInvitationDto>>;

public sealed class GetTeamInvitationsQueryHandler(TeamWorkflow workflow)
    : IRequestHandler<GetTeamInvitationsQuery, PagedResult<TeamInvitationDto>>
{
    public Task<PagedResult<TeamInvitationDto>> Handle(GetTeamInvitationsQuery request, CancellationToken cancellationToken) =>
        workflow.InvitationsAsync(request.TeamId, request.Page, request.PageSize, cancellationToken);
}
