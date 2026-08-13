using AIPMS.AI.Services;
using AIPMS.Application.Abstractions.AI;

namespace AIPMS.UnitTests.AI;

public sealed class RuleBasedProgressAnalysisServiceTests
{
    [Fact]
    public void Analyze_WhenDelayIsMaterial_ReturnsHighRiskWithActions()
    {
        var service = new RuleBasedProgressAnalysisService();
        var input = new ProgressAnalysisInput(10, 4, 2, 0.4m);

        var result = service.Analyze(input);

        Assert.Equal("HIGH", result.RiskLevel);
        Assert.Contains(result.Recommendations, item => item.Contains("overdue", StringComparison.OrdinalIgnoreCase));
    }
}
