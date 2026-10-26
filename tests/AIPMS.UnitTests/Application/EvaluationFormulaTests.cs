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
}
