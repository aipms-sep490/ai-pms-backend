using AIPMS.Application.Common.Models;
using FluentValidation;
using MediatR;

namespace AIPMS.Application.Features.Teams;

public sealed record AcceptTeamInvitationCommand(long InvitationId) : IRequest<TeamDto>;

public sealed class AcceptTeamInvitationCommandHandler(TeamWorkflow workflow)
    : IRequestHandler<AcceptTeamInvitationCommand, TeamDto>
{
    public Task<TeamDto> Handle(AcceptTeamInvitationCommand request, CancellationToken cancellationToken) =>
        workflow.AcceptAsync(request.InvitationId, cancellationToken);
}

public sealed class AcceptTeamInvitationCommandValidator : AbstractValidator<AcceptTeamInvitationCommand>
{
    public AcceptTeamInvitationCommandValidator()
    {
        RuleFor(x => x.InvitationId).GreaterThan(0);
    }
}
