using AIPMS.Application.Common.Models;
using AIPMS.Domain.Teams;

namespace AIPMS.Application.Features.Teams.Abstractions;

public interface ITeamFormationPolicyProvider
{
    // BE-12 owns persistent period policies. This port must fail closed when no policy exists.
    Task<TeamFormationPolicy?> GetAsync(long registrationPeriodId, CancellationToken cancellationToken);
}
