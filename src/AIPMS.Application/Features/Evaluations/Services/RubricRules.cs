using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Evaluations.Models;

namespace AIPMS.Application.Features.Evaluations.Services;

public static class RubricRules
{
    public static void EnsureEditable(RubricRecord rubric)
    {
        if (rubric.Status != RubricStatuses.Draft || rubric.IsReferenced)
            throw new ConflictException("This rubric version is protected. Create a new draft version to change its content.");
    }

    public static void EnsurePublishable(IReadOnlyList<RubricCriterionRecord> criteria)
    {
        if (criteria.Count == 0 || !criteria.Any(c => c.IsRequired))
            throw new ConflictException("Publishing requires at least one required criterion.");
        if (criteria.Any(c => c.WeightPercent <= 0 || c.WeightPercent > 100 || c.MaxScore <= 0
            || c.MaxScore > 999999.99m || c.SortOrder < 0
            || decimal.Round(c.WeightPercent, 2) != c.WeightPercent || decimal.Round(c.MaxScore, 2) != c.MaxScore)
            || criteria.Select(c => c.SortOrder).Distinct().Count() != criteria.Count
            || criteria.Sum(c => c.WeightPercent) != 100m)
            throw new ConflictException("Criteria must have valid score ranges, unique ordering and weights totalling exactly 100 percent.");
    }

    public static void EnsureCurrent(RubricRecord rubric, string token)
    {
        if (!Guid.TryParse(token, out var supplied) || !Guid.TryParse(rubric.ConcurrencyToken, out var current)
            || supplied != current)
            throw new ConflictException("The rubric has changed. Reload it before retrying.");
    }
}
