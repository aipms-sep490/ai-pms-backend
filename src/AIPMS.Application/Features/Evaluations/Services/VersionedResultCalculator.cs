using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Evaluations.DTOs;
using AIPMS.Application.Features.Evaluations.Models;

namespace AIPMS.Application.Features.Evaluations.Services;

public static class VersionedResultCalculator
{
    public const string Version = "DECIMAL_V2";
    public static void Validate(SchemePolicyInput policy, IReadOnlyList<SchemeComponentInput> components,
        IReadOnlyCollection<long> majors)
    {
        if (policy.CalculationVersion is not (Version or "LEGACY_V1") || policy.DecimalPlaces != 2
            || policy.RoundingMode is not ("AWAY_FROM_ZERO" or "TO_EVEN")
            || policy.Formula is not ("COMPONENT_WEIGHTS" or "DEMO_50_30_20")
            || policy.IsDemo && policy.ApprovalStatus != "DEMO"
            || !policy.IsDemo && (policy.ApprovalStatus != "APPROVED" || string.IsNullOrWhiteSpace(policy.ApprovalReference))
            || policy.ApprovalReference?.Length > 1000
            || policy.CalculationVersion == "LEGACY_V1" && (policy.Formula != "COMPONENT_WEIGHTS"
                || policy.RoundingMode != "AWAY_FROM_ZERO" || components.Any(c => c.EvaluatorWeights is not null)))
            throw Invalid("Specify a supported calculation policy; official use requires an approval source. Demo is not approval.");
        foreach (var c in components)
        {
            var weights = c.EvaluatorWeights;
            if (weights is not null && (weights.Count != c.RequiredEvaluators || weights.Any(w => w is null
                || w.EvaluatorId <= 0 || w.WeightPercent <= 0 || w.WeightPercent > 100 || decimal.Round(w.WeightPercent, 4) != w.WeightPercent)
                || weights.Select(w => w.EvaluatorId).Distinct().Count() != weights.Count || weights.Sum(w => w.WeightPercent) != 100))
                throw Invalid("Explicit evaluator weights require unique evaluator IDs, exactly the required slots and 100 percent total.");
        }
        components = components.Where(c => c.ProjectWeightPercent > 0 || c.StudentWeightPercent > 0).ToArray();
        if (policy.Formula == "DEMO_50_30_20" && (!policy.IsDemo || components.Count != 1 + majors.Count * 2
            || components.Count(c => c.Scope == "COMMON") != 1
            || components.Any(c => c.Scope == "COMMON" && (c.ProjectWeightPercent != 50 || c.StudentWeightPercent != 50)
                || c.Scope == "MAJOR_SPECIFIC" && (c.ProjectWeightPercent <= 0 || c.StudentWeightPercent != 30)
                || c.Scope == "INDIVIDUAL" && (c.ProjectWeightPercent != 0 || c.StudentWeightPercent != 20))
            || majors.Any(m => components.Count(c => c.Scope == "MAJOR_SPECIFIC" && c.MajorId == m) != 1
                || components.Count(c => c.Scope == "INDIVIDUAL" && c.MajorId == m) != 1)))
            throw Invalid("The demo formula requires one Common component and one Major/Individual component per major.");
    }

    public static decimal EvaluationTotal(IReadOnlyList<EvaluationScoreRecord> criteria, string version)
    {
        var legacy = EvaluationScoring.FinalTotal(criteria);
        if (version == "LEGACY_V1") return legacy;
        if (version != Version) throw new ConflictException("Unsupported calculation version.");
        return criteria.OrderBy(c => c.RubricCriterionId).Sum(c => c.Score!.Value / c.MaxScore * c.WeightPercent) / 10m;
    }

    public static decimal Round(decimal score, SchemePolicyInput policy) => decimal.Round(score, policy.DecimalPlaces,
        policy.RoundingMode == "TO_EVEN" ? MidpointRounding.ToEven : MidpointRounding.AwayFromZero);

    public static decimal? Total(IReadOnlyList<ResultComponentScoreDto> components, SchemePolicyInput policy, bool student)
    {
        if (components.Count == 0 || components.Any(c => c.Score is null || c.FinalizedEvaluators != c.RequiredEvaluators)) return null;
        if (policy.Formula == "DEMO_50_30_20")
        {
            var common = components.Where(c => c.Scope == "COMMON").ToArray();
            var major = components.Where(c => c.Scope == "MAJOR_SPECIFIC").ToArray();
            var individual = components.Where(c => c.Scope == "INDIVIDUAL").ToArray();
            if (common.Length != 1 || major.Length == 0 || student && (major.Length != 1 || individual.Length != 1)) return null;
            return student ? common[0].Score * .5m + major[0].Score * .3m + individual[0].Score * .2m
                : common[0].Score * .5m + major.Average(c => c.Score!.Value) * .5m;
        }
        if (components.Sum(c => c.WeightPercent) != 100m) return null;
        return components.Sum(c => c.Score!.Value * c.WeightPercent / 100m);
    }

    private static ValidationException Invalid(string message) => new(new Dictionary<string, string[]> { ["policy"] = [message] });
}
