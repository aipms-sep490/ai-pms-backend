using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Teams.Abstractions;
using AIPMS.Domain.Teams;

namespace AIPMS.Application.Features.Teams.Services;

public sealed class TeamRosterMutationGuard(ITeamRepository teamRepository) : ITeamRosterMutationGuard
{
    public void ValidateRosterMutable(string teamStatus, IEnumerable<string> projectStatuses)
    {
        if (teamStatus is not ("FORMING" or "ELIGIBLE")
            || projectStatuses.Any(TeamRules.ProjectLocksRoster))
        {
            throw new ConflictException("The team roster is locked by team or project status.");
        }
    }

    public async Task ValidateRosterMutableAsync(long teamId, CancellationToken cancellationToken)
    {
        var team = await teamRepository.GetAsync(teamId, cancellationToken)
            ?? throw new NotFoundException("Team", teamId);

        ValidateRosterMutable(team.Status, team.ProjectStatuses);
    }
}
