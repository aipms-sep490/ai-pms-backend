using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Evaluations.DTOs;
using AIPMS.Application.Features.Evaluations.Models;

namespace AIPMS.Application.Features.Evaluations.Services;

public static class VersionedResultCalculator
{
    public const string Version = "DECIMAL_V2";
    public const string PolV4Version = "POL_V4_DECIMAL";
    public const decimal DefaultPolV4FailingFloor = 4.00m;
    public static readonly IReadOnlyList<GateProcessWeightInput> DefaultIndividualGateWeights =
        [new(1, 20m), new(2, 35m), new(3, 45m)];

    public static string CalculationRuleFor(SchemePolicyInput? policy) => policy?.CalculationVersion switch
    {
        "LEGACY_V1" => EvaluationSchemeRules.CalculationRule,
        PolV4Version => PolV4Version,
        _ => Version
    };

    public static decimal? EffectiveFailingFloor(SchemePolicyInput? policy) =>
        policy?.FailingComponentThreshold ?? (policy?.CalculationVersion == PolV4Version ? DefaultPolV4FailingFloor : null);

    public static IReadOnlyList<GateProcessWeightInput> EffectiveGateWeights(SchemePolicyInput? policy) =>
        policy?.IndividualGateWeights is { Count: > 0 } configured ? configured : DefaultIndividualGateWeights;

    public static void Validate(SchemePolicyInput policy, IReadOnlyList<SchemeComponentInput> components,
        IReadOnlyCollection<long> majors)
    {
        if (policy.CalculationVersion is not (Version or PolV4Version or "LEGACY_V1") || policy.DecimalPlaces != 2
            || policy.RoundingMode is not ("AWAY_FROM_ZERO" or "TO_EVEN")
            || policy.Formula is not ("COMPONENT_WEIGHTS" or "DEMO_50_30_20" or "POL_SINGLE_30_50_20" or "POL_INTER_40_40_20")
            || policy.IsDemo && policy.ApprovalStatus != "DEMO"
            || !policy.IsDemo && (policy.ApprovalStatus != "APPROVED" || string.IsNullOrWhiteSpace(policy.ApprovalReference))
            || policy.ApprovalReference?.Length > 1000
            || policy.CalculationVersion == "LEGACY_V1" && (policy.Formula != "COMPONENT_WEIGHTS"
                || policy.RoundingMode != "AWAY_FROM_ZERO" || components.Any(c => c.EvaluatorWeights is not null))
            || policy.FailingComponentThreshold.HasValue && (policy.FailingComponentThreshold.Value is < 0 or > 10
                || decimal.Round(policy.FailingComponentThreshold.Value, 2) != policy.FailingComponentThreshold.Value))
            throw Invalid("Specify a supported calculation policy; official use requires an approval source. Demo is not approval.");
        if (policy.IndividualGateWeights is { Count: > 0 } gateWeights)
        {
            if (gateWeights.Any(w => w is null || w.GateNumber is < 1 or > 3 || w.WeightPercent <= 0 || w.WeightPercent > 100
                || decimal.Round(w.WeightPercent, 2) != w.WeightPercent)
                || gateWeights.Select(w => w.GateNumber).Distinct().Count() != gateWeights.Count
                || gateWeights.Sum(w => w.WeightPercent) != 100m)
                throw Invalid("Individual gate weights must use unique gate numbers 1..3 summing to 100 percent.");
        }
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
        if (policy.Formula == "POL_SINGLE_30_50_20")
        {
            if (majors.Count != 1)
                throw Invalid("POL_SINGLE_30_50_20 applies only to single-major projects.");
            var coldWeight = components.Where(c => c.Stage == "COLD" && c.Scope != "INDIVIDUAL").Sum(c => c.StudentWeightPercent);
            var defenseWeight = components.Where(c => c.Stage == "DEFENSE" && c.Scope != "INDIVIDUAL").Sum(c => c.StudentWeightPercent);
            var individualWeight = components.Where(c => c.Scope == "INDIVIDUAL").Sum(c => c.StudentWeightPercent);
            if (coldWeight != 30m || defenseWeight != 50m || individualWeight != 20m)
                throw Invalid("POL_SINGLE_30_50_20 requires student weights of 30% COLD, 50% DEFENSE, and 20% INDIVIDUAL.");
        }
        if (policy.Formula == "POL_INTER_40_40_20")
        {
            if (majors.Count < 2)
                throw Invalid("POL_INTER_40_40_20 requires an interdisciplinary project with at least two majors.");
            var commonComponents = components.Where(c => c.Scope == "COMMON").ToArray();
            var commonWeight = commonComponents.Sum(c => c.StudentWeightPercent);
            if (commonWeight != 40m)
                throw Invalid("POL_INTER_40_40_20 requires COMMON student weights summing to 40%.");
            var commonCold = commonComponents.Where(c => c.Stage == "COLD").Sum(c => c.StudentWeightPercent);
            var commonDefense = commonComponents.Where(c => c.Stage == "DEFENSE").Sum(c => c.StudentWeightPercent);
            if (commonCold > 0 && commonDefense > 0 && (commonCold != 12m || commonDefense != 28m))
                throw Invalid("POL_INTER_40_40_20 COMMON split must be 12% COLD (30% of 40%) and 28% DEFENSE (70% of 40%).");
            foreach (var major in majors)
            {
                var majorWeight = components.Where(c => c.Scope == "MAJOR_SPECIFIC" && c.MajorId == major).Sum(c => c.StudentWeightPercent);
                var indivWeight = components.Where(c => c.Scope == "INDIVIDUAL" && c.MajorId == major).Sum(c => c.StudentWeightPercent);
                if (majorWeight != 40m || indivWeight != 20m)
                    throw Invalid($"POL_INTER_40_40_20 requires 40% MAJOR_SPECIFIC and 20% INDIVIDUAL student weight for major {major}.");
            }
        }
    }

    public static decimal EvaluationTotal(IReadOnlyList<EvaluationScoreRecord> criteria, string version)
    {
        var legacy = EvaluationScoring.FinalTotal(criteria);
        if (version == "LEGACY_V1") return legacy;
        if (version is not (Version or PolV4Version)) throw new ConflictException("Unsupported calculation version.");
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

    public static (string Outcome, string OutcomeDetail) EvaluateOutcome(
        decimal roundedTotal,
        decimal passThreshold,
        IReadOnlyList<ResultComponentScoreDto> components,
        SchemePolicyInput? policy,
        bool defenseAbsent = false)
    {
        if (defenseAbsent) return ("FAILED", "DEFENSE_ABSENT");
        if (roundedTotal < passThreshold) return ("FAILED", "FAILED_TOTAL");
        var floor = EffectiveFailingFloor(policy);
        if (floor.HasValue)
        {
            var weighted = components.Where(c => c.WeightPercent > 0 && c.Score.HasValue).ToArray();
            if (policy?.Formula == "POL_INTER_40_40_20")
            {
                foreach (var group in weighted.GroupBy(c => c.Scope))
                {
                    var groupWeight = group.Sum(c => c.WeightPercent);
                    var groupScore = group.Sum(c => c.Score!.Value * c.WeightPercent) / groupWeight;
                    if (decimal.Round(groupScore, 2, MidpointRounding.AwayFromZero) < floor.Value)
                        return ("FAILED", "FAILED_COMPONENT_FLOOR");
                }
            }
            else
            {
                if (weighted.Any(c => decimal.Round(c.Score!.Value, 2, MidpointRounding.AwayFromZero) < floor.Value))
                    return ("FAILED", "FAILED_COMPONENT_FLOOR");
            }
        }
        return ("PASSED", "PASSED");
    }

    private static ValidationException Invalid(string message) => new(new Dictionary<string, string[]> { ["policy"] = [message] });
}
