using AIPMS.Application.Common.Models;
using FluentValidation;
using MediatR;

namespace AIPMS.Application.Features.Teams;

public sealed record InviteTeamMemberCommand(long TeamId, long InvitedUserId, string? Message) : IRequest<TeamInvitationData>;

public sealed class InviteTeamMemberCommandHandler(TeamWorkflow workflow)
    : IRequestHandler<InviteTeamMemberCommand, TeamInvitationData>
{
    public Task<TeamInvitationData> Handle(InviteTeamMemberCommand request, CancellationToken cancellationToken) =>
        workflow.InviteAsync(request, cancellationToken);
}

public sealed class InviteTeamMemberCommandValidator : AbstractValidator<InviteTeamMemberCommand>
{
    public InviteTeamMemberCommandValidator()
    {
        RuleFor(x => x.TeamId).GreaterThan(0);
        RuleFor(x => x.InvitedUserId).GreaterThan(0);
        RuleFor(x => x.Message).MaximumLength(1000);
    }
}
