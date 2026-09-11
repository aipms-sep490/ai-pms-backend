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

    [Fact]
    public void Analyze_HighBlockedTasks_TriggersBlockedRuleAndCriticalRisk()
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
                new(1, 1, "Blocked 1", "BLOCKED", "HIGH", FixedNow, FixedNow.AddDays(5), null, 1),
                new(2, 1, "Blocked 2", "BLOCKED", "HIGH", FixedNow, FixedNow.AddDays(5), null, 1),
                new(3, 1, "Active 3", "TODO", "NORMAL", FixedNow, FixedNow.AddDays(5), null, 1)
            },
            ProgressReports: Array.Empty<ProgressReportFact>(),
            Meetings: Array.Empty<MeetingFact>());

        var result = service.Analyze(facts, FixedNow);

        Assert.Equal("CRITICAL", result.RiskLevel);
        Assert.Contains(result.Factors, f => f.Code == "BLOCKED_TASKS");
        Assert.Contains(result.Recommendations, r => r.Contains("supervisor"));
    }

    [Fact]
    public void Analyze_MilestoneNearDueWithPendingDeliverables_TriggersNearDueRule()
    {
        var service = new RuleBasedProgressAnalysisService();
        var facts = new ProjectProgressFacts(
            ProjectId: 101,
            ProjectStatus: "ACTIVE",
            TeamId: 1,
            TeamMemberCount: 4,
            Milestones: new List<MilestoneFact>
            {
                new(1, "Near Due M1", "IN_PROGRESS", DateOnly.FromDateTime(FixedNow.AddDays(-7)), DateOnly.FromDateTime(FixedNow.AddDays(3)), 1)
            },
            Tasks: new List<TaskFact>
            {
                new(1, 1, "Task 1", "IN_PROGRESS", "NORMAL", FixedNow, FixedNow.AddDays(2), null, 1)
            },
            ProgressReports: Array.Empty<ProgressReportFact>(),
            Meetings: Array.Empty<MeetingFact>());

        var result = service.Analyze(facts, FixedNow);

        Assert.Equal(1, result.FeatureSnapshot.MilestoneNearDueCount);
        Assert.Contains(result.Factors, f => f.Code == "MILESTONE_DUE_SOON_LOW_COMPLETION");
        Assert.Contains(result.Recommendations, r => r.Contains("Expedite"));
    }

    [Fact]
    public void DraftWeeklyReport_PeriodEndPassed_NoReportingDeadline_DoesNotCountMissing()
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
                new(1, 1, "Task 1", "DONE", "NORMAL", FixedNow.AddDays(-20), FixedNow.AddDays(-10), FixedNow.AddDays(-12), 1)
            },
            ProgressReports: new List<ProgressReportFact>
            {
                new(1, "WEEKLY", DateOnly.FromDateTime(FixedNow.AddDays(-14)), DateOnly.FromDateTime(FixedNow.AddDays(-7)), "DRAFT", null)
            },
            Meetings: Array.Empty<MeetingFact>());

        var result = service.Analyze(facts, FixedNow);

        Assert.Null(result.FeatureSnapshot.MissingReportCount);
        Assert.Null(result.FeatureSnapshot.ReportSubmissionDelayDays);
        Assert.DoesNotContain(result.Factors, f => f.Code == "UNSUBMITTED_PROGRESS_REPORT");
        Assert.DoesNotContain(result.Factors, f => f.Code == "LATE_PROGRESS_REPORT_SUBMISSION");
        Assert.Equal("LOW", result.RiskLevel);
        Assert.Contains("Progress report schedule policy is unavailable", result.Limitations);
    }

    [Fact]
    public void ExistingReportsWithoutRequiredSchedule_DoNotInferMissingReportCount()
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
                new(1, 1, "Task 1", "DONE", "NORMAL", FixedNow.AddDays(-20), FixedNow.AddDays(-10), FixedNow.AddDays(-12), 1)
            },
            ProgressReports: new List<ProgressReportFact>
            {
                new(1, "WEEKLY", DateOnly.FromDateTime(FixedNow.AddDays(-28)), DateOnly.FromDateTime(FixedNow.AddDays(-21)), "SUBMITTED", FixedNow.AddDays(-20)),
                new(2, "WEEKLY", DateOnly.FromDateTime(FixedNow.AddDays(-21)), DateOnly.FromDateTime(FixedNow.AddDays(-14)), "SUBMITTED", FixedNow.AddDays(-13)),
                new(3, "WEEKLY", DateOnly.FromDateTime(FixedNow.AddDays(-14)), DateOnly.FromDateTime(FixedNow.AddDays(-7)), "DRAFT", null)
            },
            Meetings: Array.Empty<MeetingFact>());

        var result = service.Analyze(facts, FixedNow);

        Assert.Null(result.FeatureSnapshot.MissingReportCount);
        Assert.Null(result.FeatureSnapshot.ReportSubmissionDelayDays);
    }

    [Fact]
    public void CancelledMilestone_Overdue_IsExcludedFromDeadlineRisk()
    {
        var service = new RuleBasedProgressAnalysisService();
        var facts = new ProjectProgressFacts(
            ProjectId: 101,
            ProjectStatus: "ACTIVE",
            TeamId: 1,
            TeamMemberCount: 4,
            Milestones: new List<MilestoneFact>
            {
                new(1, "M1 Active", "COMPLETED", DateOnly.FromDateTime(FixedNow.AddDays(-20)), DateOnly.FromDateTime(FixedNow.AddDays(-10)), 1),
                new(2, "M2 Cancelled", "CANCELLED", DateOnly.FromDateTime(FixedNow.AddDays(-30)), DateOnly.FromDateTime(FixedNow.AddDays(-15)), 2)
            },
            Tasks: new List<TaskFact>
            {
                new(1, 1, "Task 1", "DONE", "NORMAL", FixedNow.AddDays(-20), FixedNow.AddDays(-10), FixedNow.AddDays(-12), 1)
            },
            ProgressReports: Array.Empty<ProgressReportFact>(),
            Meetings: Array.Empty<MeetingFact>());

        var result = service.Analyze(facts, FixedNow);

        Assert.Equal(0.0, result.FeatureSnapshot.MilestoneDelayDays);
        Assert.Equal(0, result.FeatureSnapshot.MilestoneNearDueCount);
        Assert.DoesNotContain(result.Factors, f => f.Code == "MILESTONE_OVERDUE");
    }

    [Fact]
    public void CancelledMilestone_NearDue_IsExcludedFromNearDueRisk()
    {
        var service = new RuleBasedProgressAnalysisService();
        var facts = new ProjectProgressFacts(
            ProjectId: 101,
            ProjectStatus: "ACTIVE",
            TeamId: 1,
            TeamMemberCount: 4,
            Milestones: new List<MilestoneFact>
            {
                new(1, "M1 Active", "COMPLETED", DateOnly.FromDateTime(FixedNow.AddDays(-20)), DateOnly.FromDateTime(FixedNow.AddDays(-10)), 1),
                new(2, "M2 Cancelled", "CANCELLED", DateOnly.FromDateTime(FixedNow.AddDays(-5)), DateOnly.FromDateTime(FixedNow.AddDays(2)), 2)
            },
            Tasks: new List<TaskFact>
            {
                new(1, 1, "Task 1", "DONE", "NORMAL", FixedNow.AddDays(-20), FixedNow.AddDays(-10), FixedNow.AddDays(-12), 1)
            },
            ProgressReports: Array.Empty<ProgressReportFact>(),
            Meetings: Array.Empty<MeetingFact>());

        var result = service.Analyze(facts, FixedNow);

        Assert.Equal(0, result.FeatureSnapshot.MilestoneNearDueCount);
        Assert.DoesNotContain(result.Factors, f => f.Code == "MILESTONE_DUE_SOON_LOW_COMPLETION");
    }

    [Fact]
    public void CancelledMilestone_DoesNotReduceCompletionRate()
    {
        var service = new RuleBasedProgressAnalysisService();
        var facts = new ProjectProgressFacts(
            ProjectId: 101,
            ProjectStatus: "ACTIVE",
            TeamId: 1,
            TeamMemberCount: 4,
            Milestones: new List<MilestoneFact>
            {
                new(1, "M1 Active", "COMPLETED", DateOnly.FromDateTime(FixedNow.AddDays(-20)), DateOnly.FromDateTime(FixedNow.AddDays(-10)), 1),
                new(2, "M2 Cancelled", "CANCELLED", DateOnly.FromDateTime(FixedNow.AddDays(-30)), DateOnly.FromDateTime(FixedNow.AddDays(-15)), 2)
            },
            Tasks: new List<TaskFact>
            {
                new(1, 1, "Task 1", "DONE", "NORMAL", FixedNow.AddDays(-20), FixedNow.AddDays(-10), FixedNow.AddDays(-12), 1)
            },
            ProgressReports: Array.Empty<ProgressReportFact>(),
            Meetings: Array.Empty<MeetingFact>());

        var result = service.Analyze(facts, FixedNow);

        // Denominator has only 1 non-cancelled milestone (completed 1/1 = 1.0)
        Assert.Equal(1.0, result.FeatureSnapshot.MilestoneCompletionRate);
    }

    [Fact]
    public void AllMilestonesCancelled_CompletionRateIsUnavailable()
    {
        var service = new RuleBasedProgressAnalysisService();
        var facts = new ProjectProgressFacts(
            ProjectId: 101,
            ProjectStatus: "ACTIVE",
            TeamId: 1,
            TeamMemberCount: 4,
            Milestones: new List<MilestoneFact>
            {
                new(1, "M1", "CANCELLED", DateOnly.FromDateTime(FixedNow.AddDays(-20)), DateOnly.FromDateTime(FixedNow.AddDays(-10)), 1),
                new(2, "M2", "CANCELLED", DateOnly.FromDateTime(FixedNow.AddDays(-30)), DateOnly.FromDateTime(FixedNow.AddDays(-15)), 2)
            },
            Tasks: new List<TaskFact>
            {
                new(1, 1, "Task 1", "DONE", "NORMAL", FixedNow.AddDays(-20), FixedNow.AddDays(-10), FixedNow.AddDays(-12), 1)
            },
            ProgressReports: Array.Empty<ProgressReportFact>(),
            Meetings: Array.Empty<MeetingFact>());

        var result = service.Analyze(facts, FixedNow);

        Assert.Null(result.FeatureSnapshot.MilestoneCompletionRate);
        Assert.Equal("INSUFFICIENT_DATA", result.DataStatus);
        Assert.Contains("All milestones are CANCELLED", result.Limitations);
    }

    [Fact]
    public void TaskAndMilestoneWithoutDueDates_DoNotProduceZeroDelayEvidence()
    {
        // Reviewer reproduction case:
        // Milestone: Status = IN_PROGRESS, DueDate = null
        // Task: Status = TODO, Assigned, DueDate = null
        var service = new RuleBasedProgressAnalysisService();
        var facts = new ProjectProgressFacts(
            ProjectId: 101,
            ProjectStatus: "ACTIVE",
            TeamId: 1,
            TeamMemberCount: 4,
            Milestones: new List<MilestoneFact>
            {
                new(1, "M1", "IN_PROGRESS", DateOnly.FromDateTime(FixedNow), null, 1)
            },
            Tasks: new List<TaskFact>
            {
                new(1, 1, "Task 1", "TODO", "NORMAL", FixedNow, null, null, 1)
            },
            ProgressReports: Array.Empty<ProgressReportFact>(),
            Meetings: Array.Empty<MeetingFact>());

        var result = service.Analyze(facts, FixedNow);

        Assert.Null(result.FeatureSnapshot.OverdueTaskRatio);
        Assert.Null(result.FeatureSnapshot.AverageTaskDelayDays);
        Assert.Null(result.FeatureSnapshot.MilestoneDelayDays);
        Assert.Null(result.FeatureSnapshot.MilestoneNearDueCount);
        Assert.Equal("INSUFFICIENT_DATA", result.DataStatus);
        Assert.Equal("INSUFFICIENT_DATA", result.RiskLevel);
        Assert.Null(result.RiskScore);
        Assert.Equal(0.36, result.Confidence); // 4 / 11
        Assert.Empty(result.Factors);
        Assert.Contains("Active tasks do not have due dates", result.Limitations);
        Assert.Contains("Milestones do not have due dates", result.Limitations);
    }

    [Fact]
    public void SomeItemsHaveDeadlines_SomeDoNot_FeatureCoverageReflectsPartialEvidence()
    {
        var service = new RuleBasedProgressAnalysisService();
        var facts = new ProjectProgressFacts(
            ProjectId: 101,
            ProjectStatus: "ACTIVE",
            TeamId: 1,
            TeamMemberCount: 4,
            Milestones: new List<MilestoneFact>
            {
                new(1, "M1", "IN_PROGRESS", DateOnly.FromDateTime(FixedNow), DateOnly.FromDateTime(FixedNow.AddDays(10)), 1),
                new(2, "M2", "IN_PROGRESS", DateOnly.FromDateTime(FixedNow), null, 2)
            },
            Tasks: new List<TaskFact>
            {
                new(1, 1, "Task 1", "TODO", "NORMAL", FixedNow, FixedNow.AddDays(5), null, 1),
                new(2, 1, "Task 2", "TODO", "NORMAL", FixedNow, null, null, 1)
            },
            ProgressReports: Array.Empty<ProgressReportFact>(),
            Meetings: Array.Empty<MeetingFact>());

        var result = service.Analyze(facts, FixedNow);

        Assert.NotNull(result.FeatureSnapshot.OverdueTaskRatio);
        Assert.NotNull(result.FeatureSnapshot.MilestoneDelayDays);
        Assert.Contains("Some active tasks do not have due dates", result.Limitations);
        Assert.Contains("Some milestones do not have due dates", result.Limitations);
    }

    [Fact]
    public void TaskWithoutDueDate_DeadlineFeatureUnavailable()
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
                new(1, 1, "Task 1", "TODO", "NORMAL", FixedNow, null, null, 1)
            },
            ProgressReports: Array.Empty<ProgressReportFact>(),
            Meetings: Array.Empty<MeetingFact>());

        var result = service.Analyze(facts, FixedNow);

        Assert.Null(result.FeatureSnapshot.OverdueTaskRatio);
        Assert.Null(result.FeatureSnapshot.AverageTaskDelayDays);
    }

    [Fact]
    public void MilestoneWithoutDueDate_DeadlineFeatureUnavailable()
    {
        var service = new RuleBasedProgressAnalysisService();
        var facts = new ProjectProgressFacts(
            ProjectId: 101,
            ProjectStatus: "ACTIVE",
            TeamId: 1,
            TeamMemberCount: 4,
            Milestones: new List<MilestoneFact>
            {
                new(1, "M1", "IN_PROGRESS", DateOnly.FromDateTime(FixedNow), null, 1)
            },
            Tasks: new List<TaskFact>
            {
                new(1, 1, "Task 1", "TODO", "NORMAL", FixedNow, FixedNow.AddDays(5), null, 1)
            },
            ProgressReports: Array.Empty<ProgressReportFact>(),
            Meetings: Array.Empty<MeetingFact>());

        var result = service.Analyze(facts, FixedNow);

        Assert.Null(result.FeatureSnapshot.MilestoneDelayDays);
        Assert.Null(result.FeatureSnapshot.MilestoneNearDueCount);
    }

    [Fact]
    public void UnavailableFeatures_AreNotCountedAsHealthyZero()
    {
        var service = new RuleBasedProgressAnalysisService();
        var facts = new ProjectProgressFacts(
            ProjectId: 101,
            ProjectStatus: "ACTIVE",
            TeamId: 1,
            TeamMemberCount: 4,
            Milestones: new List<MilestoneFact>
            {
                new(1, "M1", "IN_PROGRESS", DateOnly.FromDateTime(FixedNow), null, 1)
            },
            Tasks: new List<TaskFact>
            {
                new(1, 1, "Task 1", "TODO", "NORMAL", FixedNow, null, null, 1)
            },
            ProgressReports: Array.Empty<ProgressReportFact>(),
            Meetings: Array.Empty<MeetingFact>());

        var result = service.Analyze(facts, FixedNow);

        // Unavailable deadlines and reports do not produce a false LOW risk with 0 score
        Assert.NotEqual("LOW", result.RiskLevel);
        Assert.Null(result.RiskScore);
    }

    [Fact]
    public void UnavailableReportDeadline_DoesNotIncreaseRisk()
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
                new(1, 1, "Task 1", "DONE", "NORMAL", FixedNow.AddDays(-20), FixedNow.AddDays(-10), FixedNow.AddDays(-12), 1)
            },
            ProgressReports: new List<ProgressReportFact>
            {
                new(1, "WEEKLY", DateOnly.FromDateTime(FixedNow.AddDays(-14)), DateOnly.FromDateTime(FixedNow.AddDays(-2)), "DRAFT", null)
            },
            Meetings: Array.Empty<MeetingFact>());

        var result = service.Analyze(facts, FixedNow);

        // Does NOT escalate risk to HIGH / CRITICAL because of draft report past period end
        Assert.Equal("LOW", result.RiskLevel);
        Assert.DoesNotContain(result.Factors, f => f.Code == "UNSUBMITTED_PROGRESS_REPORT");
    }

    [Fact]
    public void Confidence_UsesActualAvailableFeatureCount()
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
                new(1, 1, "Task 1", "DONE", "NORMAL", FixedNow.AddDays(-20), FixedNow.AddDays(-10), FixedNow.AddDays(-12), 1)
            },
            ProgressReports: Array.Empty<ProgressReportFact>(),
            Meetings: Array.Empty<MeetingFact>());

        var result = service.Analyze(facts, FixedNow);

        // 8 non-null features out of 11: 8/11 = 0.73
        Assert.Equal(0.73, result.Confidence);
    }

    [Fact]
    public void UnavailableDeadlineFeature_DoesNotIncreaseRisk()
    {
        var service = new RuleBasedProgressAnalysisService();
        var facts = new ProjectProgressFacts(
            ProjectId: 101,
            ProjectStatus: "ACTIVE",
            TeamId: 1,
            TeamMemberCount: 4,
            Milestones: new List<MilestoneFact>
            {
                new(1, "M1", "IN_PROGRESS", DateOnly.FromDateTime(FixedNow), null, 1)
            },
            Tasks: new List<TaskFact>
            {
                new(1, 1, "Task 1", "TODO", "NORMAL", FixedNow, null, null, 1)
            },
            ProgressReports: Array.Empty<ProgressReportFact>(),
            Meetings: Array.Empty<MeetingFact>());

        var result = service.Analyze(facts, FixedNow);

        // Unknown deadline does not trigger CRITICAL or HIGH risk
        Assert.NotEqual("CRITICAL", result.RiskLevel);
        Assert.NotEqual("HIGH", result.RiskLevel);
        Assert.Equal("INSUFFICIENT_DATA", result.RiskLevel);
        Assert.Null(result.RiskScore);
    }

    [Fact]
    public void Analyze_UnassignedActiveTasks_TriggersUnassignedRule()
    {
        var service = new RuleBasedProgressAnalysisService();
        var facts = new ProjectProgressFacts(
            ProjectId: 101,
            ProjectStatus: "ACTIVE",
            TeamId: 1,
            TeamMemberCount: 4,
            Milestones: new List<MilestoneFact>
            {
                new(1, "M1", "COMPLETED", DateOnly.FromDateTime(FixedNow.AddDays(-10)), DateOnly.FromDateTime(FixedNow.AddDays(-1)), 1)
            },
            Tasks: new List<TaskFact>
            {
                new(1, 1, "Unassigned 1", "TODO", "NORMAL", FixedNow, FixedNow.AddDays(5), null, 0),
                new(2, 1, "Assigned 2", "TODO", "NORMAL", FixedNow, FixedNow.AddDays(5), null, 1)
            },
            ProgressReports: Array.Empty<ProgressReportFact>(),
            Meetings: Array.Empty<MeetingFact>());

        var result = service.Analyze(facts, FixedNow);

        Assert.Equal(0.5, result.FeatureSnapshot.UnassignedTaskRatio);
        Assert.Contains(result.Factors, f => f.Code == "UNASSIGNED_TASKS");
    }

    [Fact]
    public void Analyze_ContributionVariance_MarkedNullPendingBE13()
    {
        var service = new RuleBasedProgressAnalysisService();
        var facts = new ProjectProgressFacts(
            ProjectId: 101,
            ProjectStatus: "ACTIVE",
            TeamId: 1,
            TeamMemberCount: 4,
            Milestones: new List<MilestoneFact>
            {
                new(1, "M1", "COMPLETED", DateOnly.FromDateTime(FixedNow.AddDays(-10)), DateOnly.FromDateTime(FixedNow.AddDays(-1)), 1)
            },
            Tasks: new List<TaskFact>
            {
                new(1, 1, "Task 1", "DONE", "NORMAL", FixedNow.AddDays(-10), FixedNow.AddDays(-1), FixedNow.AddDays(-2), 1)
            },
            ProgressReports: Array.Empty<ProgressReportFact>(),
            Meetings: Array.Empty<MeetingFact>());

        var result = service.Analyze(facts, FixedNow);

        Assert.Null(result.FeatureSnapshot.ContributionVariance);
        Assert.Contains("ContributionVariance", result.Limitations);
    }

    [Fact]
    public void Analyze_MissingMeetings_SetsZeroCountWithoutFailure()
    {
        var service = new RuleBasedProgressAnalysisService();
        var facts = new ProjectProgressFacts(
            ProjectId: 101,
            ProjectStatus: "ACTIVE",
            TeamId: 1,
            TeamMemberCount: 4,
            Milestones: new List<MilestoneFact>
            {
                new(1, "M1", "COMPLETED", DateOnly.FromDateTime(FixedNow.AddDays(-10)), DateOnly.FromDateTime(FixedNow.AddDays(-1)), 1)
            },
            Tasks: new List<TaskFact>
            {
                new(1, 1, "Task 1", "DONE", "NORMAL", FixedNow.AddDays(-10), FixedNow.AddDays(-1), FixedNow.AddDays(-2), 1)
            },
            ProgressReports: Array.Empty<ProgressReportFact>(),
            Meetings: Array.Empty<MeetingFact>());

        var result = service.Analyze(facts, FixedNow);

        Assert.Equal(0, result.FeatureSnapshot.MeetingFrequencyCount);
    }
}
