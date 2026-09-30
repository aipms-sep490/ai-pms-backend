using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Evaluations.DTOs;
using AIPMS.Application.Features.Evaluations.Services;

namespace AIPMS.UnitTests.Application;

public sealed class EvaluationSchemeRulesTests
{
    private static SchemeComponentInput Common(decimal project = 50, decimal student = 50) =>
        new("Common", "COMMON", null, 1, project, student, 1);

    [Fact]
    public void Project_weights_are_configurable_but_must_total_one_hundred()
    {
        var valid = new[] { Common(70, 50), new SchemeComponentInput("Major", "MAJOR_SPECIFIC", 2, 2, 30, 50, 1) };
        EvaluationSchemeRules.Validate(valid, [2], 5);
        Assert.Throws<ValidationException>(() => EvaluationSchemeRules.Validate([valid[0], valid[1] with { ProjectWeightPercent = 40 }], [2], 5));
    }

    [Fact]
    public void Individual_component_is_excluded_from_project_weight()
    {
        var input = new[] { Common(100, 50), new SchemeComponentInput("Individual", "INDIVIDUAL", 2, 2, 1, 50, 1) };
        Assert.Throws<ValidationException>(() => EvaluationSchemeRules.Validate(input, [2], 5));
    }

    [Fact]
    public void Component_total_is_mean_of_evaluators_then_weighted()
    {
        var total = EvaluationSchemeRules.Total([(50m, (IReadOnlyList<decimal>)[8m, 10m], 2), (50m, (IReadOnlyList<decimal>)[6m], 1)]);
        Assert.Equal(7.5m, total);
    }

    [Theory]
    [InlineData("COMMON", 2)]
    [InlineData("UNKNOWN", 2)]
    [InlineData("MAJOR_SPECIFIC", 99)]
    [InlineData("INDIVIDUAL", 99)]
    public void Scope_must_match_the_frozen_required_majors(string scope, long major)
    {
        Assert.Throws<ValidationException>(() => EvaluationSchemeRules.Validate(
            [Common(100, 50), new("Target", scope, major, 1, 0, 50, 1)], [2], 5));
    }

    [Fact]
    public void Every_major_has_its_own_complete_student_weights()
    {
        SchemeComponentInput[] components = [Common(100, 40), new("Major A", "INDIVIDUAL", 2, 1, 0, 60, 1)];
        Assert.Throws<ValidationException>(() => EvaluationSchemeRules.Validate(components, [2, 3], 5));
        EvaluationSchemeRules.Validate([.. components, new("Major B", "INDIVIDUAL", 3, 1, 0, 60, 1)], [2, 3], 5);
    }

    [Fact]
    public void Missing_scores_are_blockers_and_are_never_reweighted()
    {
        Assert.Throws<ConflictException>(() => EvaluationSchemeRules.Total([(100m, (IReadOnlyList<decimal>)[8], 2)]));
        Assert.Throws<ConflictException>(() => EvaluationSchemeRules.Total([(50m, (IReadOnlyList<decimal>)[8], 1)]));
        Assert.Throws<ConflictException>(() => EvaluationSchemeRules.Total([]));
    }

    [Fact]
    public void Equal_evaluator_mean_is_not_rounded_until_final_sum()
    {
        Assert.Equal(1.01m, EvaluationSchemeRules.Total([(100m, (IReadOnlyList<decimal>)[1m, 1.01m], 2)]));
        Assert.Equal(6.6m, EvaluationSchemeRules.Total([(40m, (IReadOnlyList<decimal>)[8,10], 2),
            (30m, (IReadOnlyList<decimal>)[6], 1), (30m, (IReadOnlyList<decimal>)[4], 1)]));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(10.01)]
    [InlineData(5.001)]
    public void Threshold_must_fit_persisted_precision(decimal value) =>
        Assert.Throws<ValidationException>(() => EvaluationSchemeRules.Validate([Common(100,100)], [2], value));
}
