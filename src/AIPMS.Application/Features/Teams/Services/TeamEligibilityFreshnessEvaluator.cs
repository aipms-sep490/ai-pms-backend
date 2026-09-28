using System;
using AIPMS.Application.Features.Teams.Abstractions;
using AIPMS.Application.Features.Teams.Models;
using AIPMS.Domain.Teams;

namespace AIPMS.Application.Features.Teams.Services;

public sealed class TeamEligibilityFreshnessEvaluator : ITeamEligibilityFreshnessEvaluator
{
    public FreshnessStatus EvaluateFreshness(
        TeamEligibilitySnapshotData snapshot,
        TeamEligibilityEvaluationContext currentContext,
        DateTime utcNow)
    {
        // 1. Snapshot TeamId / ProjectPeriodId / ProjectId match current context
        if (snapshot.TeamId != currentContext.TeamId
            || snapshot.ProjectPeriodId != currentContext.ProjectPeriodId
            || snapshot.ProjectId != currentContext.ProjectId)
        {
            return FreshnessStatus.Stale;
        }

        // 2. RoundType and RevisionHistoryId match active round
        if (!string.Equals(snapshot.RoundType, currentContext.RoundType, StringComparison.Ordinal)
            || snapshot.RevisionHistoryId != currentContext.RevisionHistoryId)
        {
            return FreshnessStatus.Stale;
        }

        // 3. Snapshot.RuleVersion == TeamEligibilityRuleSet.Version
        if (!string.Equals(snapshot.RuleVersion, TeamEligibilityRuleSet.Version, StringComparison.Ordinal))
        {
            return FreshnessStatus.Stale;
        }

        // 4. Snapshot.PolicyVersion == current policy version
        if (!string.Equals(snapshot.PolicyVersion, currentContext.PolicyVersion, StringComparison.Ordinal))
        {
            return FreshnessStatus.Stale;
        }

        // 5. Snapshot.RosterHash == current roster hash
        if (!string.Equals(snapshot.RosterHash, currentContext.Hashes.RosterHash, StringComparison.Ordinal))
        {
            return FreshnessStatus.Stale;
        }

        // 6. Snapshot.AcademicScopeHash == current scope hash
        if (!string.Equals(snapshot.AcademicScopeHash, currentContext.Hashes.AcademicScopeHash, StringComparison.Ordinal))
        {
            return FreshnessStatus.Stale;
        }

        // 7. Snapshot.ProjectContextHash == current project-context hash
        if (!string.Equals(snapshot.ProjectContextHash, currentContext.Hashes.ProjectContextHash, StringComparison.Ordinal))
        {
            return FreshnessStatus.Stale;
        }

        // 8. Snapshot.Fingerprint == recomputed canonical fingerprint
        if (!string.Equals(snapshot.Fingerprint, currentContext.Hashes.Fingerprint, StringComparison.Ordinal))
        {
            return FreshnessStatus.Stale;
        }

        // 9. Snapshot.TemporalStateHash == recomputed temporal hash at utcNow
        if (!string.Equals(snapshot.TemporalStateHash, currentContext.Hashes.TemporalStateHash, StringComparison.Ordinal))
        {
            return FreshnessStatus.Stale;
        }

        // 10. Snapshot.ValidUntilAt == null OR utcNow < Snapshot.ValidUntilAt
        if (snapshot.ValidUntilAt.HasValue && utcNow >= snapshot.ValidUntilAt.Value)
        {
            return FreshnessStatus.Stale;
        }

        return FreshnessStatus.Current;
    }
}
