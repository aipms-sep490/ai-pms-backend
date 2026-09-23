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
        var weighted = RubricHierarchy.WithEffectiveWeights(criteria);
        var parentIds = criteria.Where(c => c.ParentId.HasValue).Select(c => c.ParentId!.Value).ToHashSet();
        var leaves = weighted.Where(c => !parentIds.Contains(c.Id)).ToArray();
        if (leaves.Length == 0 || !leaves.Any(c => c.IsRequired))
            throw new ConflictException("Publishing requires at least one required criterion.");
        if (weighted.Any(c => c.WeightPercent <= 0 || c.WeightPercent > 100 || c.SortOrder < 0
            || decimal.Round(c.WeightPercent, 2) != c.WeightPercent || c.EffectiveWeightPercent <= 0)
            || leaves.Any(c => c.MaxScore is null or <= 0 or > 999999.99m
                || decimal.Round(c.MaxScore.Value, 2) != c.MaxScore)
            || criteria.Where(c => parentIds.Contains(c.Id)).Any(c => c.MaxScore is not null || c.IsRequired)
            || criteria.GroupBy(c => c.ParentId).Any(group => group.Sum(c => c.WeightPercent) != 100m
                || group.Select(c => c.SortOrder).Distinct().Count() != group.Count()))
            throw new ConflictException("Criteria must have valid score ranges, unique ordering and weights totalling exactly 100 percent.");
    }

    public static void EnsureCurrent(RubricRecord rubric, string token)
    {
        if (!Guid.TryParse(token, out var supplied) || !Guid.TryParse(rubric.ConcurrencyToken, out var current)
            || supplied != current)
            throw new ConflictException("The rubric has changed. Reload it before retrying.");
    }
}
