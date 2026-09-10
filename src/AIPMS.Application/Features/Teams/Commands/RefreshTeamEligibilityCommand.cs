using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Teams.DTOs;
using AIPMS.Application.Features.Teams.Services;
using MediatR;

namespace AIPMS.Application.Features.Teams.Commands;

public sealed record RefreshTeamEligibilityCommand(long TeamId) : IRequest<TeamDto>;

public sealed class RefreshTeamEligibilityCommandHandler(TeamWorkflow workflow)
    : IRequestHandler<RefreshTeamEligibilityCommand, TeamDto>
{
    public Task<TeamDto> Handle(RefreshTeamEligibilityCommand request, CancellationToken cancellationToken) =>
        workflow.RefreshAsync(request.TeamId, cancellationToken);
}
