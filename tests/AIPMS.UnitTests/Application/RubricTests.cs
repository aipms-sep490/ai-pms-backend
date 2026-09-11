using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Evaluations.DTOs;
using AIPMS.Application.Features.Evaluations.Models;
using AIPMS.Application.Features.Evaluations.Services;
using AIPMS.Application.Features.Evaluations.Validators;

namespace AIPMS.UnitTests.Application;

public sealed class RubricTests
{
    private static RubricCriterionRecord Criterion(decimal weight, int order = 0) =>
        new(1, 1, "Criterion", null, weight, 10m, order, true);

    [Fact]
    public void Publish_uses_exact_decimal_weights()
    {
        RubricRules.EnsurePublishable([Criterion(33.33m), Criterion(33.33m, 1), Criterion(33.34m, 2)]);
        Assert.Throws<ConflictException>(() => RubricRules.EnsurePublishable(
            [Criterion(33.33m), Criterion(33.33m, 1), Criterion(33.33m, 2)]));
    }

    [Theory]
    [InlineData("EMPTY")]
    [InlineData("NO_REQUIRED")]
    [InlineData("ZERO_MAX")]
    [InlineData("ZERO_WEIGHT")]
    [InlineData("ORDER")]
    [InlineData("PRECISION")]
    public void Invalid_criteria_cannot_be_published(string scenario)
    {
        RubricCriterionRecord[] criteria = scenario switch
        {
            "EMPTY" => [],
            "NO_REQUIRED" => [Criterion(100) with { IsRequired = false }],
            "ZERO_MAX" => [Criterion(100) with { MaxScore = 0 }],
            "ZERO_WEIGHT" => [Criterion(100), Criterion(0, 1)],
            "ORDER" => [Criterion(50), Criterion(50)],
            _ => [Criterion(100) with { MaxScore = 10.001m }]
        };
        Assert.Throws<ConflictException>(() => RubricRules.EnsurePublishable(criteria));
    }

    [Theory]
    [InlineData("PUBLISHED", false)]
    [InlineData("RETIRED", false)]
    [InlineData("DRAFT", true)]
    public void Publication_retirement_and_any_reference_protect_content(string status, bool referenced)
    {
        var rubric = new RubricRecord(1, 1, 1, "R", "Rubric", null, status, 1, 1,
            Guid.NewGuid().ToString("N"), referenced, DateTime.UtcNow, DateTime.UtcNow, []);
        Assert.Throws<ConflictException>(() => RubricRules.EnsureEditable(rubric));
    }

    [Theory]
    [InlineData("PRECISION")]
    [InlineData("DUPLICATE_ORDER")]
    [InlineData("NULL_ITEM")]
    [InlineData("NULL_LIST")]
    public void Invalid_draft_inputs_fail_before_persistence(string scenario)
    {
        var criterion = new RubricCriterionInput("Criterion", null, 40m, 10m, 0, true);
        var criteria = scenario switch
        {
            "PRECISION" => new[] { criterion with { WeightPercent = 40.001m } },
            "DUPLICATE_ORDER" => [criterion, criterion],
            "NULL_ITEM" => [null!],
            _ => null!
        };
        Assert.False(new CreateRubricRequestValidator().Validate(new CreateRubricRequest(1, 1, "R", "Rubric", null, criteria)).IsValid);
    }

    [Fact]
    public void Draft_may_be_incomplete_but_cannot_silently_round_scores()
    {
        var validator = new CreateRubricRequestValidator();
        Assert.True(validator.Validate(new CreateRubricRequest(1, 1, "R", "Rubric", null, [])).IsValid);
        Assert.True(validator.Validate(new CreateRubricRequest(1, 1, "R", "Rubric", null,
            [new("Criterion", null, 40m, 10m, 0, true)])).IsValid);
        Assert.False(validator.Validate(new CreateRubricRequest(1, 1, "R", "Rubric", null,
            [new("Criterion", null, 100m, 10.001m, 0, true)])).IsValid);
    }
}
