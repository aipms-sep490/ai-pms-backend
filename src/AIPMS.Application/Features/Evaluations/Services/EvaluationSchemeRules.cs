using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Evaluations.DTOs;

namespace AIPMS.Application.Features.Evaluations.Services;

public static class EvaluationSchemeRules
{
    public const string CalculationRule = "COMPONENT_EQUAL_EVALUATOR_MEAN_WEIGHTED_10_AWAY_2DP_V1";
    public static void Validate(IReadOnlyList<SchemeComponentInput> components, IReadOnlyCollection<long> majors, decimal threshold)
    {
        if (components is null || components.Count is < 1 or > 100 || majors.Count == 0 || threshold is < 0 or > 10
            || decimal.Round(threshold, 2) != threshold)
            throw Invalid("A nonempty scheme, required majors and threshold between 0 and 10 are required.");
        foreach (var c in components)
        {
            if (c is null || string.IsNullOrWhiteSpace(c.Name) || c.Name.Trim().Length > 200 || c.RubricId <= 0
                || c.RequiredEvaluators is < 1 or > 20 || c.ProjectWeightPercent is < 0 or > 100
                || c.StudentWeightPercent is < 0 or > 100 || decimal.Round(c.ProjectWeightPercent, 4) != c.ProjectWeightPercent
                || decimal.Round(c.StudentWeightPercent, 4) != c.StudentWeightPercent
                || c.ProjectWeightPercent + c.StudentWeightPercent == 0)
                throw Invalid("Invalid component name, rubric, evaluator count or weight (maximum four decimal places).");
            if (c.Scope == "COMMON" ? c.MajorId.HasValue : c.Scope is not ("MAJOR_SPECIFIC" or "INDIVIDUAL")
                || !c.MajorId.HasValue || !majors.Contains(c.MajorId.Value))
                throw Invalid("COMMON has no major; MAJOR_SPECIFIC and INDIVIDUAL require a project major.");
            if (c.Scope == "INDIVIDUAL" && c.ProjectWeightPercent != 0)
                throw Invalid("INDIVIDUAL cannot contribute to the project result.");
        }
        if (components.Sum(c => c.ProjectWeightPercent) != 100m)
            throw Invalid("Project weights must sum to 100 percent.");
        foreach (var major in majors)
            if (components.Where(c => c.Scope == "COMMON" || c.MajorId == major).Sum(c => c.StudentWeightPercent) != 100m)
                throw Invalid($"Student weights for major {major} must sum to 100 percent.");
    }
    public static decimal Total(IEnumerable<(decimal Weight, IReadOnlyList<decimal> Scores, int Required)> components)
    {
        var rows = components.ToArray();
        if (rows.Length == 0 || rows.Sum(x => x.Weight) != 100m
            || rows.Any(x => x.Weight < 0 || x.Required < 1 || x.Scores.Count != x.Required || x.Scores.Any(s => s is < 0 or > 10)))
            throw new ConflictException("All required finalized evaluations and exactly 100 percent weight are required.");
        return decimal.Round(rows.Sum(x => x.Weight * x.Scores.Average() / 100m), 2, MidpointRounding.AwayFromZero);
    }
    private static ValidationException Invalid(string message) => new(new Dictionary<string, string[]> { ["scheme"] = [message] });
}
