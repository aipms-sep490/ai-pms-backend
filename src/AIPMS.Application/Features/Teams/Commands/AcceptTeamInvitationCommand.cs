using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Teams.DTOs;
using AIPMS.Application.Features.Teams.Services;
using MediatR;

namespace AIPMS.Application.Features.Teams.Commands;

public sealed record AcceptTeamInvitationCommand(long InvitationId) : IRequest<TeamDto>;

public sealed class AcceptTeamInvitationCommandHandler(TeamWorkflow workflow)
    : IRequestHandler<AcceptTeamInvitationCommand, TeamDto>
{
    public Task<TeamDto> Handle(AcceptTeamInvitationCommand request, CancellationToken cancellationToken) =>
        workflow.AcceptAsync(request.InvitationId, cancellationToken);
}
