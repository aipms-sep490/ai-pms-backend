using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Evaluations.Models;
using AIPMS.Domain.Exceptions;

namespace AIPMS.Application.Features.Evaluations.Services;

public static class RubricRules
{
    public static void EnsureEditable(RubricRecord rubric)
    {
        if (rubric.Status != RubricStatuses.Draft || rubric.IsReferenced)
            throw new ConflictException("This rubric version is protected. Create a new draft version to change its content.");
    }

    public static void ValidateScope(string? scope, long? majorId, string? projectMode, string? stage, DateOnly? effectiveFrom, DateOnly? effectiveTo)
    {
        if (scope == "COMMON" && majorId.HasValue)
            throw new DomainException("A common rubric cannot be scoped to a specific major.", "RUBRIC_COMMON_HAS_MAJOR");

        if (scope == "MAJOR_SPECIFIC" && !majorId.HasValue)
            throw new DomainException("A major-specific rubric must specify a major ID.", "RUBRIC_MAJOR_REQUIRED");

        if (effectiveFrom.HasValue && effectiveTo.HasValue && effectiveFrom.Value > effectiveTo.Value)
            throw new DomainException("EffectiveFrom date cannot be after EffectiveTo date.", "RUBRIC_EFFECTIVE_RANGE_INVALID");

        if (projectMode is not null && projectMode is not ("SINGLE_MAJOR" or "INTERDISCIPLINARY"))
            throw new DomainException("Invalid project mode.", "RUBRIC_INVALID_PROJECT_MODE");

        if (stage is not null && stage is not ("ON_GOING" or "COLD" or "DEFENSE"))
            throw new DomainException("Invalid evaluation stage.", "RUBRIC_INVALID_STAGE");

        if (scope is not null && scope is not ("COMMON" or "MAJOR_SPECIFIC" or "INDIVIDUAL"))
            throw new DomainException("Invalid evaluation scope.", "RUBRIC_INVALID_SCOPE");
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
