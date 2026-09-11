using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Teams.Services;
using MediatR;

namespace AIPMS.Application.Features.Teams.Commands;

public sealed record RemoveTeamMemberCommand(long TeamId, long? MemberUserId) : IRequest<bool>;

public sealed class RemoveTeamMemberCommandHandler(TeamWorkflow workflow)
    : IRequestHandler<RemoveTeamMemberCommand, bool>
{
    public Task<bool> Handle(RemoveTeamMemberCommand request, CancellationToken cancellationToken) =>
        workflow.RemoveAsync(request.TeamId, request.MemberUserId, cancellationToken);
}
