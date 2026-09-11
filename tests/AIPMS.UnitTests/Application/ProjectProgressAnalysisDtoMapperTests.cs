using System;
using AIPMS.Application.Features.Projects.DTOs;
using Xunit;

namespace AIPMS.UnitTests.Application;

public sealed class ProjectProgressAnalysisDtoMapperTests
{
    [Fact]
    public void ToDto_MapsAllPropertiesCorrectly()
    {
        var now = DateTime.UtcNow;
        var summary = new ProgressSummaryDto(2, 1, 5, 3, 1, 0, 1, 60.0);
        var snapshot = new FeatureSnapshotDto(0.0, 0.0, 0.2, 0.5, 0.0, 0, null, null, 2, 0.2, null);
        var factors = new[] { new RiskFactorDto("BLOCKED_TASKS", "BlockedTaskRatio", 0.2, "MEDIUM", "20% blocked") };
        var recommendations = new[] { "Escalate blockers" };

        var dto = ProjectProgressAnalysisDtoMapper.ToDto(
            101,
            now,
            now,
            "SUFFICIENT",
            "MEDIUM",
            30.0,
            0.82,
            "INSUFFICIENT_DATA",
            summary,
            snapshot,
            factors,
            recommendations,
            "PROVISIONAL_RULE_BASELINE_1.0",
            "FEATURE_SET_1.0",
            "RULE_BASED",
            "Limitations note");

        Assert.Equal(101, dto.ProjectId);
        Assert.Equal("SUFFICIENT", dto.DataStatus);
        Assert.Equal("MEDIUM", dto.RiskLevel);
        Assert.Equal(30.0, dto.RiskScore);
        Assert.Equal("INSUFFICIENT_DATA", dto.Trend);
        Assert.Single(dto.Reasons);
        Assert.Single(dto.Recommendations);
        Assert.Equal("Limitations note", dto.Limitations);
    }
}
