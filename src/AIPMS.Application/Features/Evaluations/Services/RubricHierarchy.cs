using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Evaluations.Models;

namespace AIPMS.Application.Features.Evaluations.Services;

public static class RubricHierarchy
{
    // Bound request size, not the number of levels in the academic model.
    public const int MaxNodes = 1000;

    public static IReadOnlyList<RubricCriterionRecord> WithEffectiveWeights(IReadOnlyList<RubricCriterionRecord> criteria)
    {
        var ids = criteria.Select(c => c.Id).ToHashSet();
        if (ids.Count != criteria.Count
            || criteria.Any(c => c.ParentId.HasValue && !ids.Contains(c.ParentId.Value)))
            throw new ConflictException("Rubric contains duplicate IDs or an orphan criterion.");
        var children = criteria.ToLookup(c => c.ParentId);
        var pending = new Queue<(RubricCriterionRecord Node, decimal ParentWeight)>();
        foreach (var root in children[null]) pending.Enqueue((root, 100m));
        var result = new List<RubricCriterionRecord>();
        while (pending.TryDequeue(out var item))
        {
            var effective = item.ParentWeight * (item.Node.WeightPercent / 100m);
            result.Add(item.Node with { EffectiveWeightPercent = effective });
            foreach (var child in children[item.Node.Id]) pending.Enqueue((child, effective));
        }
        if (result.Count != criteria.Count) throw new ConflictException("Rubric contains a criterion cycle.");
        return result;
    }

    public static IReadOnlyList<RubricCriterionRecord> Leaves(IReadOnlyList<RubricCriterionRecord> criteria)
    {
        var parents = criteria.Where(c => c.ParentId.HasValue).Select(c => c.ParentId!.Value).ToHashSet();
        var weighted = WithEffectiveWeights(criteria);
        var children = weighted.ToLookup(c => c.ParentId);
        var pending = new Stack<RubricCriterionRecord>(children[null].OrderByDescending(c => c.SortOrder).ThenByDescending(c => c.Id));
        var result = new List<RubricCriterionRecord>();
        while (pending.TryPop(out var node))
        {
            if (!parents.Contains(node.Id)) result.Add(node);
            else foreach (var child in children[node.Id].OrderByDescending(c => c.SortOrder).ThenByDescending(c => c.Id))
                pending.Push(child);
        }
        return result;
    }

    public static IReadOnlyList<RubricCriterionInput> ToInputs(IReadOnlyList<RubricCriterionRecord> criteria)
    {
        var ordered = WithEffectiveWeights(criteria);
        var children = ordered.ToLookup(c => c.ParentId);
        var mapped = new Dictionary<long, RubricCriterionInput>();
        foreach (var c in ordered.Reverse())
            mapped.Add(c.Id, new(c.Name, c.Description, c.WeightPercent, c.MaxScore, c.SortOrder, c.IsRequired)
            {
                Children = children[c.Id].OrderBy(x => x.SortOrder).ThenBy(x => x.Id).Select(x => mapped[x.Id]).ToArray()
            });
        return children[null].OrderBy(c => c.SortOrder).ThenBy(c => c.Id).Select(c => mapped[c.Id]).ToArray();
    }
}
