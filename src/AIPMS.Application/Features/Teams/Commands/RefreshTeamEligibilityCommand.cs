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

public sealed record RefreshTeamEligibilityCommand(long TeamId) : IRequest<TeamDto>;

public sealed class RefreshTeamEligibilityCommandHandler(
    TeamEligibilityEvaluationService? evaluationService,
    ITeamRepository repository,
    TeamWorkflow workflow,
    ICurrentUser currentUser)
    : IRequestHandler<RefreshTeamEligibilityCommand, TeamDto>
{
    public RefreshTeamEligibilityCommandHandler(TeamWorkflow workflow)
        : this(null, null!, workflow, null!)
    {
    }

    public Task<TeamDto> Handle(RefreshTeamEligibilityCommand request, CancellationToken cancellationToken)
    {
        if (evaluationService is null || repository is null || currentUser is null)
        {
            return workflow.RefreshAsync(request.TeamId, cancellationToken);
        }

        return repository.InTransactionAsync(token => HandleInTransactionAsync(request, token), cancellationToken);
    }

    private async Task<TeamDto> HandleInTransactionAsync(RefreshTeamEligibilityCommand request, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is null)
            throw new UnauthorizedException();

        var team = await repository.GetAsync(request.TeamId, ct)
            ?? throw new NotFoundException("Team", request.TeamId);

        if (!team.Members.Any(m => m.UserId == currentUser.UserId.Value))
            throw new ForbiddenException("Only active team members can refresh eligibility.");

        var (_, _, _) = await evaluationService!.EvaluateAndPersistAsync(
            request.TeamId,
            currentUser.UserId.Value,
            "REFRESH_ALIAS",
            ct);

        return await workflow.GetAsync(request.TeamId, ct);
    }
}
