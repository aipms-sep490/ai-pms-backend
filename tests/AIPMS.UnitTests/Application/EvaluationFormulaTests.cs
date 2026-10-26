using AIPMS.Application.Features.Evaluations.Models;
using AIPMS.Application.Features.Evaluations.Services;
using System.Text.Json;
using AIPMS.Application.Features.Evaluations.DTOs;

namespace AIPMS.UnitTests.Application;

public sealed class EvaluationFormulaTests
{
    [Fact]
    public void EvaluationFormula_SerializesToStructuredComponentsWithoutDescription()
    {
        var formula = new EvaluationFormulaDto(
            new List<EvaluationFormulaComponentDto>
            {
                new("COMMON", 40m),
                new("MAJOR", 40m),
                new("INDIVIDUAL", 20m)
            },
            null);

        var breakdown = new ResultBreakdownDto(
            "Test Scheme",
            1,
            "DECIMAL_V2",
            false,
            false,
            "OFFICIAL",
            "READY",
            8.5m,
            8.5m,
            8.5m,
            [],
            [],
            formula);

        var json = JsonSerializer.Serialize(breakdown, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var doc = JsonDocument.Parse(json);

        Assert.True(doc.RootElement.TryGetProperty("formula", out var formulaProp));
        Assert.True(formulaProp.TryGetProperty("components", out var componentsProp));
        Assert.Equal(3, componentsProp.GetArrayLength());

        var first = componentsProp[0];
        Assert.Equal("COMMON", first.GetProperty("type").GetString());
        Assert.Equal(40m, first.GetProperty("weightPercent").GetDecimal());

        Assert.True(formulaProp.TryGetProperty("description", out var descProp));
        Assert.Equal(JsonValueKind.Null, descProp.ValueKind);
    }

    [Fact]
    public void EvaluationFormula_PreservesDecimalFractionalWeights()
    {
        var formula = new EvaluationFormulaDto(
            new List<EvaluationFormulaComponentDto>
            {
                new("COMMON", 33.33m),
                new("MAJOR", 33.33m),
                new("INDIVIDUAL", 33.34m)
            },
            "Equal 3-way split");

        var json = JsonSerializer.Serialize(formula, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var doc = JsonDocument.Parse(json);

        var components = doc.RootElement.GetProperty("components");
        Assert.Equal(33.33m, components[0].GetProperty("weightPercent").GetDecimal());
        Assert.Equal(33.33m, components[1].GetProperty("weightPercent").GetDecimal());
        Assert.Equal(33.34m, components[2].GetProperty("weightPercent").GetDecimal());
    }

    [Fact]
    public void EvaluationFormula_FractionalWeights_MatchesCalculatorBreakdownTotal()
    {
        var formulaComponents = new List<EvaluationFormulaComponentDto>
        {
            new("COMMON", 33.33m),
            new("MAJOR", 33.33m),
            new("INDIVIDUAL", 33.34m)
        };

        var policy = new SchemePolicyInput(
            IsDemo: false,
            ApprovalStatus: "APPROVED",
            ApprovalReference: "REF-001",
            Formula: "COMPONENT_WEIGHTS",
            CalculationVersion: "DECIMAL_V2",
            RoundingMode: "AWAY_FROM_ZERO",
            DecimalPlaces: 2);

        var scoreComponents = new List<ResultComponentScoreDto>
        {
            new(1, "Common Component", "COMMON", null, 33.33m, 8.0m, 2, 2),
            new(2, "Major Component", "MAJOR_SPECIFIC", 10, 33.33m, 9.0m, 2, 2),
            new(3, "Individual Component", "INDIVIDUAL", 10, 33.34m, 10.0m, 2, 2)
        };

        var total = VersionedResultCalculator.Total(scoreComponents, policy, student: true);
        Assert.NotNull(total);

        // Expected: (8.0 * 33.33 + 9.0 * 33.33 + 10.0 * 33.34) / 100 = (266.64 + 299.97 + 333.40) / 100 = 900.01 / 100 = 9.0001
        var expected = (8.0m * 33.33m + 9.0m * 33.33m + 10.0m * 33.34m) / 100m;
        Assert.Equal(expected, total.Value);

        // Rounded to 2 decimal places with AWAY_FROM_ZERO
        var rounded = VersionedResultCalculator.Round(total.Value, policy);
        Assert.Equal(9.00m, rounded);

        // Formula components weights match score components weights exactly (no loss of precision)
        Assert.Equal(scoreComponents[0].WeightPercent, formulaComponents[0].WeightPercent);
        Assert.Equal(scoreComponents[1].WeightPercent, formulaComponents[1].WeightPercent);
        Assert.Equal(scoreComponents[2].WeightPercent, formulaComponents[2].WeightPercent);
        Assert.Equal(100.00m, formulaComponents.Sum(f => f.WeightPercent));
    }
}
