using AIPMS.AI.Services;
using AIPMS.Application.Features.Projects.Models;

namespace AIPMS.UnitTests.AI;

public sealed class ProgressDeadlineCoverageTests
{
    private static readonly DateTime Now = new(2026, 9, 11, 12, 0, 0, DateTimeKind.Utc);
    private readonly RuleBasedProgressAnalysisService service = new();

    private static ProjectProgressFacts Facts(IReadOnlyList<MilestoneFact> milestones,
        IReadOnlyList<TaskFact> tasks) => new(1, "ACTIVE", 1, 3, milestones, tasks, [], []);

    private static MilestoneFact Milestone(long id, string status, DateOnly? due) =>
        new(id, "Milestone", status, null, due, (int)id);

    private static TaskFact Task(long id, DateTime? due) =>
        new(id, 1, "Assigned task", "TODO", "NORMAL", null, due, null, 1);

    [Fact]
    public void Completed_deadline_does_not_mask_missing_outstanding_milestone_dates()
    {
        var result = service.Analyze(Facts(
            [Milestone(1, "COMPLETED", DateOnly.FromDateTime(Now.AddDays(-10))),
             Milestone(2, "IN_PROGRESS", null)],
            [Task(1, Now.AddDays(10)) with { MilestoneId = 2 }]), Now);

        Assert.Equal("INSUFFICIENT_DATA", result.DataStatus);
        Assert.Equal("INSUFFICIENT_DATA", result.RiskLevel);
        Assert.Null(result.RiskScore);
        Assert.Null(result.FeatureSnapshot.MilestoneDelayDays);
        Assert.Null(result.FeatureSnapshot.MilestoneNearDueCount);
        Assert.Equal(0.5, result.FeatureSnapshot.MilestoneCompletionRate);
        Assert.Equal(0.55, result.Confidence);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(50)]
    [InlineData(99)]
    public void Missing_task_deadlines_reduce_confidence_and_prevent_sufficient_status(int missing)
    {
        var tasks = Enumerable.Range(1, 100).Select(i => Task(i, Now.AddDays(10))).ToArray();
        var facts = Facts([Milestone(1, "IN_PROGRESS", DateOnly.FromDateTime(Now.AddDays(20)))], tasks);
        var complete = service.Analyze(facts, Now);
        var partial = service.Analyze(facts with
        {
            Tasks = tasks.Select((t, i) => i < missing ? t with { DueAt = null } : t).ToArray()
        }, Now);

        Assert.Equal("SUFFICIENT", complete.DataStatus);
        Assert.Equal(Math.Round((6.0 + 2.0 * (100 - missing) / 100) / 11, 2), partial.Confidence);
        Assert.True(partial.Confidence <= complete.Confidence);
        if (missing >= 50)
            Assert.True(partial.Confidence < complete.Confidence);
        Assert.Equal("INSUFFICIENT_DATA", partial.DataStatus);
        Assert.Equal("INSUFFICIENT_DATA", partial.RiskLevel);
        Assert.Null(partial.RiskScore);
        Assert.Contains("partial evidence", partial.Limitations);
    }

    [Fact]
    public void Missing_milestone_deadlines_reduce_confidence_and_preserve_observed_overdue_factor()
    {
        var milestones = new[]
        {
            Milestone(1, "IN_PROGRESS", DateOnly.FromDateTime(Now.AddDays(-10))),
            Milestone(2, "IN_PROGRESS", DateOnly.FromDateTime(Now.AddDays(20)))
        };
        var facts = Facts(milestones, [Task(1, Now.AddDays(10))]);
        var complete = service.Analyze(facts, Now);
        var partial = service.Analyze(facts with
        {
            Milestones = [milestones[0], milestones[1] with { DueDate = null }]
        }, Now);

        Assert.True(partial.Confidence < complete.Confidence);
        Assert.Equal("INSUFFICIENT_DATA", partial.DataStatus);
        Assert.NotEqual("LOW", partial.RiskLevel);
        Assert.Equal(10.0, partial.FeatureSnapshot.MilestoneDelayDays);
        Assert.Contains(partial.Factors, f => f.Code == "MILESTONE_OVERDUE");
    }

    [Fact]
    public void No_outstanding_work_has_zero_current_delay_even_without_historical_dates()
    {
        var result = service.Analyze(Facts(
            [Milestone(1, "COMPLETED", null), Milestone(2, "CANCELLED", null)],
            [Task(1, null) with { Status = "DONE" }]), Now);

        Assert.Equal("SUFFICIENT", result.DataStatus);
        Assert.Equal("LOW", result.RiskLevel);
        Assert.Equal(1.0, result.FeatureSnapshot.MilestoneCompletionRate);
        Assert.Equal(0.0, result.FeatureSnapshot.MilestoneDelayDays);
        Assert.Equal(0, result.FeatureSnapshot.MilestoneNearDueCount);
        Assert.Equal(0.0, result.FeatureSnapshot.OverdueTaskRatio);
        Assert.Empty(result.Factors);
    }
}
