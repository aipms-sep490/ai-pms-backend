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

    public static void EnsureValidHierarchy(IReadOnlyList<RubricCriterionInput> criteria)
    {
        void ValidateNode(RubricCriterionInput node, bool isParent)
        {
            if (isParent && node.Levels.Count > 0)
                throw new DomainException($"Parent criterion '{node.Name}' cannot have quality levels.", "RUBRIC_PARENT_HAS_LEVELS");

            foreach (var level in node.Levels)
            {
                if (level.LevelOrder <= 0)
                    throw new DomainException($"Criterion '{node.Name}' level order must be greater than 0.", "RUBRIC_LEVEL_ORDER_INVALID");
                if (level.ScoreFrom.HasValue && level.ScoreTo.HasValue && level.ScoreFrom.Value > level.ScoreTo.Value)
                    throw new DomainException($"Criterion '{node.Name}' level {level.LevelOrder} has invalid score range: scoreFrom cannot exceed scoreTo.", "RUBRIC_LEVEL_SCORE_RANGE_INVALID");
            }

            foreach (var child in node.Children)
                ValidateNode(child, child.Children.Count > 0);
        }

        foreach (var root in criteria)
            ValidateNode(root, root.Children.Count > 0);
    }

    public static void EnsurePublishable(IReadOnlyList<RubricCriterionRecord> criteria, int requiredLevelCount = 4, bool requireLevels = false)
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

        var parents = criteria.Where(c => parentIds.Contains(c.Id)).ToList();
        foreach (var parent in parents)
        {
            if (parent.Levels.Count > 0)
                throw new DomainException($"Parent criterion '{parent.Name}' cannot have quality levels.", "RUBRIC_PARENT_HAS_LEVELS");
        }

        if (requireLevels || criteria.Any(c => c.Levels.Count > 0))
        {
            EnsureCompleteLevels(leaves, requiredLevelCount);
        }
    }

    public static void EnsureCompleteLevels(IEnumerable<RubricCriterionRecord> leaves, int requiredLevelCount)
    {
        foreach (var leaf in leaves)
        {
            if (leaf.Levels.Count != requiredLevelCount)
                throw new DomainException($"Criterion '{leaf.Name}' requires {requiredLevelCount} levels (order 1..{requiredLevelCount}). Missing quality levels.", "RUBRIC_LEAF_LEVELS_INCOMPLETE");

            for (int expectedOrder = 1; expectedOrder <= requiredLevelCount; expectedOrder++)
            {
                var level = leaf.Levels.FirstOrDefault(l => l.LevelOrder == expectedOrder);
                if (level is null || string.IsNullOrWhiteSpace(level.Descriptor))
                    throw new DomainException($"Criterion '{leaf.Name}' is missing required quality level {expectedOrder} or has an empty descriptor.", "RUBRIC_LEAF_LEVELS_INCOMPLETE");
                if (level.ScoreFrom.HasValue && level.ScoreTo.HasValue && level.ScoreFrom.Value > level.ScoreTo.Value)
                    throw new DomainException($"Criterion '{leaf.Name}' level {expectedOrder} has invalid score range: scoreFrom cannot exceed scoreTo.", "RUBRIC_LEVEL_SCORE_RANGE_INVALID");
            }
        }
    }

    public static void EnsureCurrent(RubricRecord rubric, string token)
    {
        if (!Guid.TryParse(token, out var supplied) || !Guid.TryParse(rubric.ConcurrencyToken, out var current)
            || supplied != current)
            throw new ConflictException("The rubric has changed. Reload it before retrying.");
    }
}
