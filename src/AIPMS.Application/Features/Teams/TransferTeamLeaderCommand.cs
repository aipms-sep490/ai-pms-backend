using AIPMS.Application.Common.Models;
using FluentValidation;
using MediatR;

namespace AIPMS.Application.Features.Teams;

public sealed record TransferTeamLeaderCommand(long TeamId, long NewLeaderUserId) : IRequest<TeamDto>;

public sealed class TransferTeamLeaderCommandHandler(TeamWorkflow workflow)
    : IRequestHandler<TransferTeamLeaderCommand, TeamDto>
{
    public Task<TeamDto> Handle(TransferTeamLeaderCommand request, CancellationToken cancellationToken) =>
        workflow.TransferAsync(request, cancellationToken);
}

public sealed class TransferTeamLeaderCommandValidator : AbstractValidator<TransferTeamLeaderCommand>
{
    public TransferTeamLeaderCommandValidator()
    {
        RuleFor(x => x.TeamId).GreaterThan(0);
        RuleFor(x => x.NewLeaderUserId).GreaterThan(0);
    }
}
