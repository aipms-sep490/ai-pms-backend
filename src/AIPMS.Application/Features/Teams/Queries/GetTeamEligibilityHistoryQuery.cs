using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Features.Teams.Abstractions;
using AIPMS.Application.Features.Teams.DTOs;
using AIPMS.Application.Features.Teams.Services;
using MediatR;

namespace AIPMS.Application.Features.Teams.Queries;

public sealed record GetTeamEligibilityHistoryQuery(long TeamId) : IRequest<IReadOnlyList<TeamEligibilityCheckDto>>;

public sealed class GetTeamEligibilityHistoryQueryHandler(
    TeamEligibilityEvaluationService evaluationService,
    ITeamEligibilityAccessService accessService)
    : IRequestHandler<GetTeamEligibilityHistoryQuery, IReadOnlyList<TeamEligibilityCheckDto>>
{
    public async Task<IReadOnlyList<TeamEligibilityCheckDto>> Handle(GetTeamEligibilityHistoryQuery request, CancellationToken cancellationToken)
    {
        await accessService.ValidateCanReadEligibilityAsync(request.TeamId, cancellationToken);
        return await evaluationService.GetEligibilityHistoryAsync(request.TeamId, cancellationToken);
    }
}
