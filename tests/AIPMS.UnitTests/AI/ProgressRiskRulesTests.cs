using AIPMS.AI.Services;
using AIPMS.Application.Features.Projects.Models;
using AIPMS.Infrastructure.Services.Projects;

namespace AIPMS.UnitTests.AI;

public sealed class ProgressRiskRulesTests
{
    [Theory]
    [InlineData(0, "LOW")] [InlineData(24, "LOW")] [InlineData(25, "MEDIUM")]
    [InlineData(49, "MEDIUM")] [InlineData(50, "HIGH")] [InlineData(74, "HIGH")]
    [InlineData(75, "CRITICAL")] [InlineData(100, "CRITICAL")]
    public void Levels_have_exact_boundaries(int score, string expected) => Assert.Equal(expected, ProgressRiskRules.Level(score));

    [Fact]
    public void Signals_are_counted_once_per_group_and_cap_at_100()
    {
        var baseline = new RuleBasedProgressAnalysisService().Analyze(new ProjectProgressFacts(1, "ACTIVE", 1, 3, [], [], [], []), DateTime.UtcNow);
        var counts = new ProgressRiskCounts(10, 1, 7, 2, 1, 4, 1, 2, 1, 2);
        var result = ProgressRiskRules.Apply(baseline, counts);
        Assert.Equal(100d, result.RiskScore); Assert.Equal("CRITICAL", result.RiskLevel); Assert.Equal(7, result.Signals.Count);
        Assert.Contains("chưa có hạn", result.Limitations);
    }

    [Fact]
    public void Empty_data_has_no_fabricated_risk_or_confidence()
    {
        var baseline = new RuleBasedProgressAnalysisService().Analyze(new ProjectProgressFacts(1, "ACTIVE", 1, 3, [], [], [], []), DateTime.UtcNow);
        var result = ProgressRiskRules.Apply(baseline, new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0));
        Assert.Equal("INSUFFICIENT_DATA", result.DataStatus); Assert.Null(result.RiskScore); Assert.Null(result.Confidence);
    }
}
