using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Results.DTOs;

namespace AIPMS.Application.Features.Results.Services;

public static class ResultScoring
{
    public const string Rule = "WEIGHTED_EVALUATIONS_10_AWAY_FROM_ZERO_2DP_V1";
    public static decimal Total(IReadOnlyList<ResultContributionDto> inputs, decimal threshold)
    {
        if (inputs.Count == 0 || inputs.Select(i => i.AssignmentId).Distinct().Count() != inputs.Count
            || inputs.Select(i => i.EvaluationId).Distinct().Count() != inputs.Count || inputs.Sum(i => i.WeightPercent) != 100m
            || inputs.Any(i => i.WeightPercent <= 0 || i.Score < 0 || i.Score > 10) || threshold < 0 || threshold > 10)
            throw new ConflictException("The result policy or finalized scores are invalid.");
        return decimal.Round(inputs.OrderBy(i => i.AssignmentId).Sum(i => i.Score * i.WeightPercent / 100m), 2, MidpointRounding.AwayFromZero);
    }
}
