using AIPMS.Application.Common.Models;
using FluentValidation;
using MediatR;

namespace AIPMS.Application.Features.Teams;

public sealed record CancelTeamInvitationCommand(long InvitationId) : IRequest<bool>;

public sealed class CancelTeamInvitationCommandHandler(TeamWorkflow workflow)
    : IRequestHandler<CancelTeamInvitationCommand, bool>
{
    public Task<bool> Handle(CancelTeamInvitationCommand request, CancellationToken cancellationToken) =>
        workflow.CancelAsync(request.InvitationId, cancellationToken);
}

public sealed class CancelTeamInvitationCommandValidator : AbstractValidator<CancelTeamInvitationCommand>
{
    public CancelTeamInvitationCommandValidator()
    {
        RuleFor(x => x.InvitationId).GreaterThan(0);
    }
}
