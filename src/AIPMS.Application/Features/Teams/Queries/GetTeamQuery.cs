using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Teams.DTOs;
using AIPMS.Application.Features.Teams.Services;
using MediatR;

namespace AIPMS.Application.Features.Teams.Queries;

public sealed record GetTeamQuery(long TeamId) : IRequest<TeamDto>;

public sealed class GetTeamQueryHandler(TeamWorkflow workflow)
    : IRequestHandler<GetTeamQuery, TeamDto>
{
    public Task<TeamDto> Handle(GetTeamQuery request, CancellationToken cancellationToken) =>
        workflow.GetAsync(request.TeamId, cancellationToken);
}
