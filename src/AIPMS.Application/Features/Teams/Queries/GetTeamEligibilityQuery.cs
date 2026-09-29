using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Features.Teams.Abstractions;
using AIPMS.Application.Features.Teams.DTOs;
using AIPMS.Application.Features.Teams.Services;
using MediatR;

namespace AIPMS.Application.Features.Teams.Queries;

public sealed record GetTeamEligibilityQuery(long TeamId) : IRequest<TeamEligibilityCheckDto?>;

public sealed class GetTeamEligibilityQueryHandler(
    TeamEligibilityEvaluationService evaluationService,
    ITeamEligibilityAccessService accessService)
    : IRequestHandler<GetTeamEligibilityQuery, TeamEligibilityCheckDto?>
{
    public async Task<TeamEligibilityCheckDto?> Handle(GetTeamEligibilityQuery request, CancellationToken cancellationToken)
    {
        await accessService.ValidateCanReadEligibilityAsync(request.TeamId, cancellationToken);
        return await evaluationService.GetCurrentEligibilityAsync(request.TeamId, cancellationToken);
    }
}
