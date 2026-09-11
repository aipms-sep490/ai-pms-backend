using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Teams.Services;
using MediatR;

namespace AIPMS.Application.Features.Teams.Commands;

public sealed record RejectTeamInvitationCommand(long InvitationId) : IRequest<bool>;

public sealed class RejectTeamInvitationCommandHandler(TeamWorkflow workflow)
    : IRequestHandler<RejectTeamInvitationCommand, bool>
{
    public Task<bool> Handle(RejectTeamInvitationCommand request, CancellationToken cancellationToken) =>
        workflow.RejectAsync(request.InvitationId, cancellationToken);
}
