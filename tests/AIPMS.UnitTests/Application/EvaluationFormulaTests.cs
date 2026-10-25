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
                new("COMMON", 40),
                new("MAJOR", 40),
                new("INDIVIDUAL", 20)
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
        Assert.Equal(40, first.GetProperty("weightPercent").GetInt32());

        Assert.True(formulaProp.TryGetProperty("description", out var descProp));
        Assert.Equal(JsonValueKind.Null, descProp.ValueKind);
    }
}
