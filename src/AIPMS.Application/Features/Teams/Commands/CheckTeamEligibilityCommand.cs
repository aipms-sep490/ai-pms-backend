using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Teams.Abstractions;
using AIPMS.Application.Features.Teams.DTOs;
using AIPMS.Application.Features.Teams.Services;
using MediatR;

namespace AIPMS.Application.Features.Teams.Commands;

public sealed record CheckTeamEligibilityCommand(long TeamId) : IRequest<TeamEligibilityCheckDto>;

public sealed class CheckTeamEligibilityCommandHandler(
    TeamEligibilityEvaluationService evaluationService,
    ITeamRepository repository,
    ICurrentUser currentUser)
    : IRequestHandler<CheckTeamEligibilityCommand, TeamEligibilityCheckDto>
{
    public Task<TeamEligibilityCheckDto> Handle(CheckTeamEligibilityCommand request, CancellationToken cancellationToken) =>
        repository.InTransactionAsync(token => HandleInTransactionAsync(request, token), cancellationToken);

    private async Task<TeamEligibilityCheckDto> HandleInTransactionAsync(CheckTeamEligibilityCommand request, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is null)
            throw new UnauthorizedException();

        var team = await repository.GetAsync(request.TeamId, ct)
            ?? throw new NotFoundException("Team", request.TeamId);

        if (!team.Members.Any(m => m.UserId == currentUser.UserId.Value))
            throw new ForbiddenException("Only active team members can run eligibility checks.");

        var (snapshot, _, _) = await evaluationService.EvaluateAndPersistAsync(
            request.TeamId,
            currentUser.UserId.Value,
            "MANUAL_CHECK",
            ct);

        return evaluationService.MapToDto(snapshot, FreshnessStatus.Current);
    }
}
