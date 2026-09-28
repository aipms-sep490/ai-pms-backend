using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AIPMS.Application.Features.Teams.Abstractions;

public interface ITeamRosterMutationGuard
{
    void ValidateRosterMutable(string teamStatus, IEnumerable<string> projectStatuses);
    Task ValidateRosterMutableAsync(long teamId, CancellationToken cancellationToken);
}
