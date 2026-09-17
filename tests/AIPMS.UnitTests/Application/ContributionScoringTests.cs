using AIPMS.Application.Features.Contributions.DTOs;
using AIPMS.Application.Features.Contributions.Services;
using AIPMS.Application.Features.Contributions.Queries;
using AIPMS.Application.Features.Contributions.Commands;
using AIPMS.Application.Features.Contributions.Validators;

namespace AIPMS.UnitTests.Application;

public sealed class ContributionScoringTests
{
    private static ContributionMemberDto Member(long id, double credit) => new(id, $"Member {id}", 0, 0, 0, 0, 0, credit, 0);

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(2.99, 0)]
    public void BR112_insufficient_activity_never_produces_variance(double a, double b)
    {
        var result = ContributionScoring.Summarize([Member(1, a), Member(2, b)]);
        Assert.Equal("INSUFFICIENT_DATA", result.DataStatus);
        Assert.Null(result.ActivityVariance);
    }

    [Fact]
    public void BR112_empty_and_single_member_teams_have_no_comparison()
    {
        Assert.Null(ContributionScoring.Summarize([]).ActivityVariance);
        Assert.Null(ContributionScoring.Summarize([Member(1, 100)]).ActivityVariance);
    }

    [Theory]
    [InlineData(6, 3, 2.25)]
    [InlineData(3, 0, 2.25)]
    [InlineData(1.5, 1.5, 0)]
    public void Population_variance_uses_all_members_including_zero_activity(double a, double b, double variance)
    {
        var result = ContributionScoring.Summarize([Member(1, a), Member(2, b)]);
        Assert.Equal("SUFFICIENT", result.DataStatus);
        Assert.Equal(variance, result.ActivityVariance);
    }

    [Fact]
    public void Pagination_preserves_team_statistics_and_orders_members_by_id()
    {
        var summary = ContributionScoring.Summarize([Member(7, 6), Member(3, 0), Member(2, 3)]);
        var paged = ContributionScoring.Page(summary, 2, 1);
        Assert.Equal(3, Assert.Single(paged.Members).UserId);
        Assert.Equal(3, paged.TotalCount);
        Assert.Equal(summary.ActivityVariance, paged.ActivityVariance);
        Assert.Empty(ContributionScoring.Page(summary, 4, 1).Members);
    }

    [Fact]
    public void Validators_reject_invalid_ids_filters_and_overflowing_pagination()
    {
        var summaries = new GetProjectContributionQueryValidator();
        Assert.False(summaries.Validate(new GetProjectContributionQuery(0)).IsValid);
        Assert.False(summaries.Validate(new GetProjectContributionQuery(1, int.MaxValue, 100)).IsValid);
        Assert.False(summaries.Validate(new GetProjectContributionQuery(1, 1, 101)).IsValid);
        Assert.True(summaries.Validate(new GetProjectContributionQuery(1)).IsValid);
        var evidence = new GetContributionEvidenceQueryValidator();
        Assert.False(evidence.Validate(new GetContributionEvidenceQuery(1, 0)).IsValid);
        Assert.False(evidence.Validate(new GetContributionEvidenceQuery(1, 1, SourceType: "PRIVATE_NOTES")).IsValid);
        Assert.True(evidence.Validate(new GetContributionEvidenceQuery(1, 1, SourceType: "FILE")).IsValid);
        Assert.False(new RebuildContributionSnapshotCommandValidator().Validate(new RebuildContributionSnapshotCommand(0)).IsValid);
    }
}
