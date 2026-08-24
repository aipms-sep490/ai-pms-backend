namespace AIPMS.Application.Features.Evaluations;
public sealed record ScoreInput(decimal Score,decimal MaxScore,decimal WeightPercent);
public interface IEvaluationScoreCalculator { decimal Calculate(IEnumerable<ScoreInput> scores); }
public sealed class EvaluationScoreCalculator : IEvaluationScoreCalculator
{ public decimal Calculate(IEnumerable<ScoreInput> scores) { var rows=scores.ToArray();if(rows.Length==0)return 0;var weights=rows.Sum(x=>x.WeightPercent);if(weights<=0)return 0;var total=rows.Sum(x=>x.Score/x.MaxScore*x.WeightPercent)/weights*10m;return Math.Round(total,2,MidpointRounding.AwayFromZero); } }
