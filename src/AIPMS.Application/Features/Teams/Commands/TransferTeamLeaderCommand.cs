using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Teams.DTOs;
using AIPMS.Application.Features.Teams.Services;
using MediatR;

namespace AIPMS.Application.Features.Teams.Commands;

public sealed record TransferTeamLeaderCommand(long TeamId, long NewLeaderUserId) : IRequest<TeamDto>;

public sealed class TransferTeamLeaderCommandHandler(TeamWorkflow workflow)
    : IRequestHandler<TransferTeamLeaderCommand, TeamDto>
{
    public Task<TeamDto> Handle(TransferTeamLeaderCommand request, CancellationToken cancellationToken) =>
        workflow.TransferAsync(request, cancellationToken);
}
