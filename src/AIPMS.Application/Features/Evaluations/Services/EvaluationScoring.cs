using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Evaluations.Models;

namespace AIPMS.Application.Features.Evaluations.Services;

public static class EvaluationScoring
{
    public static decimal FinalTotal(IReadOnlyList<EvaluationScoreRecord> criteria)
    {
        var preview = Preview(criteria, 10m);
        if (preview.Total is not decimal total)
            throw new ConflictException("Every weighted criterion needs an explicit score before finalization. Missing criteria: "
                + string.Join(", ", preview.MissingCriterionIds));
        return total;
    }

    public static EvaluationPreview Preview(IReadOnlyList<EvaluationScoreRecord> criteria, decimal scale)
    {
        if (scale is not (10m or 100m)) throw new ArgumentOutOfRangeException(nameof(scale));
        if (criteria.Count == 0 || criteria.Select(c => c.RubricCriterionId).Distinct().Count() != criteria.Count
            || criteria.Sum(c => c.WeightPercent) != 100m || !criteria.Any(c => c.IsRequired)
            || criteria.Any(c => c.WeightPercent <= 0 || c.MaxScore <= 0))
            throw new ConflictException("The rubric is not valid for scoring.");
        foreach (var criterion in criteria)
        {
            if (criterion.Score is not decimal score) continue;
            if (score < 0 || score > criterion.MaxScore || decimal.Round(score, 2) != score)
                throw new ConflictException("Each score must be between zero and its criterion maximum, with at most two decimal places.");
        }
        var missing = criteria.Where(c => !c.Score.HasValue).Select(c => c.RubricCriterionId).ToArray();
        var required = criteria.Where(c => c.IsRequired && !c.Score.HasValue).Select(c => c.RubricCriterionId).ToArray();
        if (missing.Length != 0) return new(null, missing, required);
        // A stable summation order and one final rounding keep the preview reproducible.
        var total = criteria.OrderBy(c => c.RubricCriterionId)
            .Sum(c => c.Score!.Value / c.MaxScore * c.WeightPercent) * scale / 100m;
        return new(decimal.Round(total, 2, MidpointRounding.AwayFromZero), missing, required);
    }
}
