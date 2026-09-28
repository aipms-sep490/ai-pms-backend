using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Features.Teams.Models;

namespace AIPMS.Application.Features.Teams.Abstractions;

public interface ITeamEligibilityRepository
{
    Task<(TeamEligibilitySnapshotData Snapshot, bool WasInserted)> SaveCheckAsync(
        TeamEligibilitySaveModel model,
        CancellationToken cancellationToken);

    Task<TeamEligibilitySnapshotData?> GetLatestCheckAsync(
        long teamId,
        string? roundType,
        long? revisionHistoryId,
        CancellationToken cancellationToken);

    Task<TeamEligibilitySnapshotData?> GetCurrentSnapshotAsync(
        long teamId,
        long projectPeriodId,
        long? projectId,
        string roundType,
        long? revisionHistoryId,
        string evaluationKey,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<TeamEligibilitySnapshotData>> GetHistoryAsync(
        long teamId,
        CancellationToken cancellationToken);

    Task<TeamEligibilityContextInput> BuildContextInputAsync(
        long teamId,
        DateTime utcNow,
        CancellationToken cancellationToken);
}
