using AIPMS.Application.Features.Contributions.DTOs;
using AIPMS.Application.Features.Contributions.Services;
using Xunit;

namespace AIPMS.UnitTests.Application;

public sealed class ContributionScoringTests
{
    [Fact]
    public void Summary_marks_insufficient_when_evidence_is_too_small()
    {
        var members = new[] { new ContributionMemberDto(1, "A", 1, 0, 0, 0, 0, 1, 0), new ContributionMemberDto(2, "B", 0, 0, 0, 0, 0, 0, 0) };
        var result = ContributionScoring.Summarize(members);
        Assert.Equal("INSUFFICIENT_DATA", result.DataStatus);
        Assert.Null(result.ActivityVariance);
    }

    [Fact]
    public void Summary_calculates_population_variance_for_sufficient_activity()
    {
        var members = new[] { new ContributionMemberDto(1, "A", 4, 2, 1, 1, 0, 6, 2), new ContributionMemberDto(2, "B", 2, 1, 0, 1, 0, 3, 1) };
        var result = ContributionScoring.Summarize(members);
        Assert.Equal("SUFFICIENT", result.DataStatus);
        Assert.Equal(2.25, result.ActivityVariance);
    }
}
