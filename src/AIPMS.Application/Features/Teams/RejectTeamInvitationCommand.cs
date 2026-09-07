using AIPMS.Application.Common.Models;
using FluentValidation;
using MediatR;

namespace AIPMS.Application.Features.Teams;

public sealed record RejectTeamInvitationCommand(long InvitationId) : IRequest<bool>;

public sealed class RejectTeamInvitationCommandHandler(TeamWorkflow workflow)
    : IRequestHandler<RejectTeamInvitationCommand, bool>
{
    public Task<bool> Handle(RejectTeamInvitationCommand request, CancellationToken cancellationToken) =>
        workflow.RejectAsync(request.InvitationId, cancellationToken);
}

public sealed class RejectTeamInvitationCommandValidator : AbstractValidator<RejectTeamInvitationCommand>
{
    public RejectTeamInvitationCommandValidator()
    {
        RuleFor(x => x.InvitationId).GreaterThan(0);
    }
}
