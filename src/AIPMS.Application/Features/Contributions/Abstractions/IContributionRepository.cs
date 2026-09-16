using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Features.Contributions.DTOs;

namespace AIPMS.Application.Features.Contributions.Abstractions;

public interface IContributionRepository
{
    Task<IReadOnlyList<ContributionMemberDto>> GetProjectSummaryAsync(long projectId, CancellationToken ct);
}
