namespace AIPMS.Application.Features.Evaluations;
public sealed record ScoreInput(decimal Score,decimal MaxScore,decimal WeightPercent);
public interface IEvaluationScoreCalculator { decimal Calculate(IEnumerable<ScoreInput> scores); }
public sealed class EvaluationScoreCalculator : IEvaluationScoreCalculator
{
    // Leader-approved rule: weighted score on a 10-point scale, rounded to two
    // decimals with midpoint values rounded away from zero.
    public decimal Calculate(IEnumerable<ScoreInput> scores)
    {
        var rows = scores.ToArray();
        if (rows.Length == 0) return 0m;
        if (rows.Any(x => x.MaxScore <= 0 || x.WeightPercent < 0))
            throw new ArgumentOutOfRangeException(nameof(scores));
        var total = rows.Sum(x => x.Score / x.MaxScore * x.WeightPercent) / 10m;
        return Math.Round(total, 2, MidpointRounding.AwayFromZero);
    }
}

