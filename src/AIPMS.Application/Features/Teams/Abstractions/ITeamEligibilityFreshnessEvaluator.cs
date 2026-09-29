using System;
using AIPMS.Application.Features.Teams.Models;

namespace AIPMS.Application.Features.Teams.Abstractions;

public enum FreshnessStatus
{
    Current,
    Stale
}

public interface ITeamEligibilityFreshnessEvaluator
{
    FreshnessStatus EvaluateFreshness(
        TeamEligibilitySnapshotData snapshot,
        TeamEligibilityEvaluationContext currentContext,
        DateTime utcNow);
}
