using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.Teams.Abstractions;
using AIPMS.Application.Features.Teams.DTOs;
using AIPMS.Application.Features.Teams.Services;
using MediatR;

namespace AIPMS.Application.Features.Teams.Queries;

public sealed record GetTeamEligibilityHistoryQuery(long TeamId) : IRequest<IReadOnlyList<TeamEligibilityCheckDto>>;

public sealed class GetTeamEligibilityHistoryQueryHandler(
    TeamEligibilityEvaluationService evaluationService,
    ITeamRepository repository,
    ICurrentUser currentUser)
    : IRequestHandler<GetTeamEligibilityHistoryQuery, IReadOnlyList<TeamEligibilityCheckDto>>
{
    public async Task<IReadOnlyList<TeamEligibilityCheckDto>> Handle(GetTeamEligibilityHistoryQuery request, CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is null)
            throw new UnauthorizedException();

        var team = await repository.GetAsync(request.TeamId, cancellationToken)
            ?? throw new NotFoundException("Team", request.TeamId);

        var isAdminOrStaff = currentUser.Roles.Contains(AppRoles.Admin)
            || currentUser.Roles.Contains(AppRoles.DepartmentStaff);

        if (!isAdminOrStaff && !team.Members.Any(m => m.UserId == currentUser.UserId.Value))
            throw new ForbiddenException("You do not have access to view this team's eligibility history.");

        return await evaluationService.GetEligibilityHistoryAsync(request.TeamId, cancellationToken);
    }
}
