using System.Threading;
using System.Threading.Tasks;

namespace AIPMS.Application.Features.Teams.Abstractions;

public interface ITeamEligibilityAccessService
{
    Task ValidateCanReadEligibilityAsync(long teamId, CancellationToken cancellationToken);
}
