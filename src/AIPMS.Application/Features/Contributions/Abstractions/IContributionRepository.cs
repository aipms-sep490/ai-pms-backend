using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Features.Contributions.DTOs;

namespace AIPMS.Application.Features.Contributions.Abstractions;

public interface IContributionRepository
{
    Task<T> InProjectTransactionAsync<T>(long projectId, Func<CancellationToken, Task<T>> action, CancellationToken ct);
    Task<bool> CanRebuildAsync(long projectId, long actorId, CancellationToken ct);
    Task<bool> IsActiveUserAsync(long actorId, CancellationToken ct);
    Task<ContributionSummaryDto> GetSummaryAsync(long projectId, bool storedOnly, CancellationToken ct);
    Task<IReadOnlyList<ContributionEvidenceDto>> GetEvidenceAsync(long projectId, long userId, CancellationToken ct);
    Task<ContributionRebuildResult> RebuildSnapshotAsync(long projectId, DateTime snapshotAt, CancellationToken ct);
}
