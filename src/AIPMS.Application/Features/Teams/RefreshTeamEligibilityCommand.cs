using AIPMS.Application.Common.Models;
using FluentValidation;
using MediatR;

namespace AIPMS.Application.Features.Teams;

public sealed record RefreshTeamEligibilityCommand(long TeamId) : IRequest<TeamDto>;

public sealed class RefreshTeamEligibilityCommandHandler(TeamWorkflow workflow)
    : IRequestHandler<RefreshTeamEligibilityCommand, TeamDto>
{
    public Task<TeamDto> Handle(RefreshTeamEligibilityCommand request, CancellationToken cancellationToken) =>
        workflow.RefreshAsync(request.TeamId, cancellationToken);
}

public sealed class RefreshTeamEligibilityCommandValidator : AbstractValidator<RefreshTeamEligibilityCommand>
{
    public RefreshTeamEligibilityCommandValidator()
    {
        RuleFor(x => x.TeamId).GreaterThan(0);
    }
}
