using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Teams.Models;

namespace AIPMS.Application.Features.Teams.Abstractions;

public interface ITeamInvitationCandidateReader
{
    Task<PagedResult<TeamInvitationCandidate>> SearchAsync(TeamInvitationCandidateScope scope,
        string? search, int page, int pageSize, CancellationToken cancellationToken);
}
