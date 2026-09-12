using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Evaluations.Models;
using AIPMS.Application.Features.Evaluations.Services;

namespace AIPMS.UnitTests.Application;

public sealed class EvaluationScoringTests
{
    [Fact]
    public void Finalization_requires_all_scores_and_uses_preview_rounding()
    {
        Assert.Throws<ConflictException>(() => EvaluationScoring.FinalTotal([Score(1, 60, 10, 9), Score(2, 40, 20, null, false)]));
        Assert.Equal(8.6m, EvaluationScoring.FinalTotal([Score(1, 60, 10, 9), Score(2, 40, 20, 16, false)]));
        Assert.Equal(8.01m, EvaluationScoring.FinalTotal([Score(1, 50, 100, 80.01m), Score(2, 50, 100, 80.09m)]));
    }

    private static EvaluationScoreRecord Score(long id, decimal weight, decimal max, decimal? score, bool required = true) =>
        new(id, "Criterion", null, weight, max, (int)id, required, score, null);

    [Theory]
    [InlineData(10, "8.6")]
    [InlineData(100, "86")]
    public void Weighted_preview_normalizes_different_maxima(int scale, string expected)
    {
        var preview = EvaluationScoring.Preview([Score(1, 60, 10, 9), Score(2, 40, 20, 16)], scale);
        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), preview.Total);
    }

    [Fact]
    public void Missing_optional_score_does_not_become_zero_or_renormalize_weights()
    {
        var preview = EvaluationScoring.Preview([Score(1, 60, 10, 9), Score(2, 40, 20, null, false)], 10);
        Assert.Null(preview.Total);
        Assert.Equal(new long[] { 2 }, preview.MissingCriterionIds);
        Assert.Empty(preview.MissingRequiredCriterionIds);
    }

    [Fact]
    public void Zero_is_a_real_score_and_missing_required_scores_are_reported()
    {
        Assert.Equal(0m, EvaluationScoring.Preview([Score(1, 100, 10, 0)], 10).Total);
        Assert.Equal(new long[] { 1 }, EvaluationScoring.Preview([Score(1, 100, 10, null)], 10).MissingRequiredCriterionIds);
    }

    [Fact]
    public void Rounds_once_at_end_away_from_zero_and_is_input_order_independent()
    {
        var scores = new[] { Score(1, 50, 100, 80.01m), Score(2, 50, 100, 80.09m) };
        Assert.Equal(8.01m, EvaluationScoring.Preview(scores, 10).Total);
        Assert.Equal(8.01m, EvaluationScoring.Preview(scores.Reverse().ToArray(), 10).Total);
    }

    [Theory]
    [InlineData("-0.01")]
    [InlineData("10.01")]
    [InlineData("9.999")]
    public void Invalid_score_is_rejected(string raw)
    {
        var score = decimal.Parse(raw, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Throws<ConflictException>(() => EvaluationScoring.Preview([Score(1, 100, 10, score)], 10));
    }
}
