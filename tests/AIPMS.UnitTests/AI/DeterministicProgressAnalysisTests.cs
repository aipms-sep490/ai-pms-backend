using System;
using System.Collections.Generic;
using AIPMS.AI.Services;
using AIPMS.Application.Features.Projects.Models;
using Xunit;

namespace AIPMS.UnitTests.AI;

public sealed class DeterministicProgressAnalysisTests
{
    private static readonly DateTime FixedNow = new(2026, 8, 27, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Analyze_ZeroTasks_ReturnsInsufficientData()
    {
        var service = new RuleBasedProgressAnalysisService();
        var facts = new ProjectProgressFacts(
            ProjectId: 101,
            ProjectStatus: "ACTIVE",
            TeamId: 1,
            TeamMemberCount: 4,
            Milestones: new List<MilestoneFact>
            {
                new(1, "Milestone 1", "IN_PROGRESS", DateOnly.FromDateTime(FixedNow), DateOnly.FromDateTime(FixedNow.AddDays(14)), 1)
            },
            Tasks: Array.Empty<TaskFact>(),
            ProgressReports: Array.Empty<ProgressReportFact>(),
            Meetings: Array.Empty<MeetingFact>());

        var result = service.Analyze(facts, FixedNow);

        Assert.Equal("INSUFFICIENT_DATA", result.DataStatus);
        Assert.Equal("INSUFFICIENT_DATA", result.RiskLevel);
        Assert.Null(result.RiskScore);
        Assert.NotNull(result.Limitations);
    }

    [Fact]
    public void Analyze_ZeroMilestones_ReturnsInsufficientData()
    {
        var service = new RuleBasedProgressAnalysisService();
        var facts = new ProjectProgressFacts(
            ProjectId: 101,
            ProjectStatus: "ACTIVE",
            TeamId: 1,
            TeamMemberCount: 4,
            Milestones: Array.Empty<MilestoneFact>(),
            Tasks: new List<TaskFact>
            {
                new(1, 1, "Task 1", "TODO", "NORMAL", FixedNow, FixedNow.AddDays(5), null, 1)
            },
            ProgressReports: Array.Empty<ProgressReportFact>(),
            Meetings: Array.Empty<MeetingFact>());

        var result = service.Analyze(facts, FixedNow);

        Assert.Equal("INSUFFICIENT_DATA", result.DataStatus);
        Assert.Equal("INSUFFICIENT_DATA", result.RiskLevel);
        Assert.Null(result.RiskScore);
    }

    [Fact]
    public void Analyze_OverdueTaskRatioOver50Percent_ReturnsCriticalRisk()
    {
        var service = new RuleBasedProgressAnalysisService();
        var facts = new ProjectProgressFacts(
            ProjectId: 101,
            ProjectStatus: "ACTIVE",
            TeamId: 1,
            TeamMemberCount: 4,
            Milestones: new List<MilestoneFact>
            {
                new(1, "M1", "IN_PROGRESS", DateOnly.FromDateTime(FixedNow), DateOnly.FromDateTime(FixedNow.AddDays(10)), 1)
            },
            Tasks: new List<TaskFact>
            {
                new(1, 1, "Overdue 1", "TODO", "HIGH", FixedNow.AddDays(-10), FixedNow.AddDays(-5), null, 1),
                new(2, 1, "Overdue 2", "IN_PROGRESS", "HIGH", FixedNow.AddDays(-10), FixedNow.AddDays(-2), null, 1),
                new(3, 1, "Active 3", "TODO", "NORMAL", FixedNow, FixedNow.AddDays(5), null, 1)
            },
            ProgressReports: Array.Empty<ProgressReportFact>(),
            Meetings: Array.Empty<MeetingFact>());

        var result = service.Analyze(facts, FixedNow);

        Assert.Equal("SUFFICIENT", result.DataStatus);
        Assert.Equal("CRITICAL", result.RiskLevel);
        Assert.NotNull(result.RiskScore);
        Assert.Contains(result.Factors, f => f.Code == "OVERDUE_TASKS");
    }

    [Fact]
    public void Analyze_AllTasksCompleted_ReturnsLowRisk()
    {
        var service = new RuleBasedProgressAnalysisService();
        var facts = new ProjectProgressFacts(
            ProjectId: 101,
            ProjectStatus: "ACTIVE",
            TeamId: 1,
            TeamMemberCount: 4,
            Milestones: new List<MilestoneFact>
            {
                new(1, "M1", "COMPLETED", DateOnly.FromDateTime(FixedNow.AddDays(-20)), DateOnly.FromDateTime(FixedNow.AddDays(-10)), 1)
            },
            Tasks: new List<TaskFact>
            {
                new(1, 1, "Task 1", "DONE", "NORMAL", FixedNow.AddDays(-20), FixedNow.AddDays(-10), FixedNow.AddDays(-11), 1),
                new(2, 1, "Task 2", "DONE", "HIGH", FixedNow.AddDays(-20), FixedNow.AddDays(-10), FixedNow.AddDays(-12), 1)
            },
            ProgressReports: Array.Empty<ProgressReportFact>(),
            Meetings: Array.Empty<MeetingFact>());

        var result = service.Analyze(facts, FixedNow);

        Assert.Equal("SUFFICIENT", result.DataStatus);
        Assert.Equal("LOW", result.RiskLevel);
        Assert.Equal(0.0, result.RiskScore);
        Assert.Empty(result.Factors);
    }

    [Fact]
    public void Analyze_Reproducibility_SameInputGivesIdenticalResult()
    {
        var service = new RuleBasedProgressAnalysisService();
        var facts = new ProjectProgressFacts(
            ProjectId: 101,
            ProjectStatus: "ACTIVE",
            TeamId: 1,
            TeamMemberCount: 4,
            Milestones: new List<MilestoneFact>
            {
                new(1, "M1", "IN_PROGRESS", DateOnly.FromDateTime(FixedNow), DateOnly.FromDateTime(FixedNow.AddDays(10)), 1)
            },
            Tasks: new List<TaskFact>
            {
                new(1, 1, "Task 1", "BLOCKED", "HIGH", FixedNow, FixedNow.AddDays(5), null, 1),
                new(2, 1, "Task 2", "TODO", "NORMAL", FixedNow, FixedNow.AddDays(5), null, 0)
            },
            ProgressReports: Array.Empty<ProgressReportFact>(),
            Meetings: Array.Empty<MeetingFact>());

        var run1 = service.Analyze(facts, FixedNow);
        var run2 = service.Analyze(facts, FixedNow);

        Assert.Equal(run1.RiskLevel, run2.RiskLevel);
        Assert.Equal(run1.RiskScore, run2.RiskScore);
        Assert.Equal(run1.Factors.Count, run2.Factors.Count);
        Assert.Equal(run1.Recommendations.Count, run2.Recommendations.Count);
    }
}
