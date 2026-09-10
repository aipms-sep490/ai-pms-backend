using AIPMS.Application.Features.Teams.DTOs;
using AIPMS.Application.Features.Teams.Services;
using MediatR;

namespace AIPMS.Application.Features.Teams.Commands;

public sealed record InviteTeamMemberCommand(long TeamId, long InvitedUserId, string? Message) : IRequest<TeamInvitationDto>;

public sealed class InviteTeamMemberCommandHandler(TeamWorkflow workflow)
    : IRequestHandler<InviteTeamMemberCommand, TeamInvitationDto>
{
    public Task<TeamInvitationDto> Handle(InviteTeamMemberCommand request, CancellationToken cancellationToken) =>
        workflow.InviteAsync(request, cancellationToken);
}
