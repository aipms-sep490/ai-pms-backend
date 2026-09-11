using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Teams.Services;
using MediatR;

namespace AIPMS.Application.Features.Teams.Commands;

public sealed record CancelTeamInvitationCommand(long InvitationId) : IRequest<bool>;

public sealed class CancelTeamInvitationCommandHandler(TeamWorkflow workflow)
    : IRequestHandler<CancelTeamInvitationCommand, bool>
{
    public Task<bool> Handle(CancelTeamInvitationCommand request, CancellationToken cancellationToken) =>
        workflow.CancelAsync(request.InvitationId, cancellationToken);
}
