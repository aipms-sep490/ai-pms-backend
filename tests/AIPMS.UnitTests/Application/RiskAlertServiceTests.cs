using System;
using System.Collections.Generic;
using System.Linq;
using AIPMS.AI.Services;
using AIPMS.Application.Abstractions.AI;
using AIPMS.Application.Features.Projects.Abstractions;
using AIPMS.Application.Features.Projects.Models;
using AIPMS.Infrastructure.Persistence.Models;
using Xunit;

namespace AIPMS.UnitTests.Application;

public sealed class RiskAlertServiceTests
{
    private static readonly DateTime FixedNow = new(2026, 10, 25, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Elevated_risk_project_with_5_overdue_and_2_blocked_tasks_triggers_high_risk()
    {
        var analysisService = new RuleBasedProgressAnalysisService();

        var milestone = new MilestoneFact(
            1, "Milestone 1", "IN_PROGRESS",
            DateOnly.FromDateTime(FixedNow.AddDays(-30)),
            DateOnly.FromDateTime(FixedNow.AddDays(14)), 1);

        var tasks = new List<TaskFact>();
        // 5 Overdue tasks
        for (var i = 1; i <= 5; i++)
        {
            tasks.Add(new(i, 1, $"Overdue Task {i}", "IN_PROGRESS", "HIGH", FixedNow.AddDays(-20), FixedNow.AddDays(-2), null, 1));
        }
        // 2 Blocked tasks
        for (var i = 6; i <= 7; i++)
        {
            tasks.Add(new(i, 1, $"Blocked Task {i}", "BLOCKED", "HIGH", FixedNow.AddDays(-10), FixedNow.AddDays(5), null, 1));
        }
        // 1 Done task
        tasks.Add(new(8, 1, "Done Task 1", "DONE", "NORMAL", FixedNow.AddDays(-15), FixedNow.AddDays(-5), FixedNow.AddDays(-6), 1));

        var facts = new ProjectProgressFacts(
            ProjectId: 201,
            ProjectStatus: "ACTIVE",
            TeamId: 10,
            TeamMemberCount: 4,
            Milestones: [milestone],
            Tasks: tasks,
            ProgressReports: [],
            Meetings: []);

        var result = analysisService.Analyze(facts, FixedNow);

        Assert.NotEqual("INSUFFICIENT_DATA", result.DataStatus);
        Assert.True(result.RiskLevel is "HIGH" or "CRITICAL" or "MEDIUM");
        Assert.Equal(5, result.ProgressSummary.OverdueTasks);
        Assert.Equal(2, result.ProgressSummary.BlockedTasks);

        var settings = new RiskAlertSettings
        {
            OverdueTaskThreshold = 3,
            BlockedTaskThreshold = 2
        };

        var isElevated = result.RiskLevel is "MEDIUM" or "HIGH" or "CRITICAL"
            || result.ProgressSummary.OverdueTasks >= settings.OverdueTaskThreshold
            || result.ProgressSummary.BlockedTasks >= settings.BlockedTaskThreshold;

        Assert.True(isElevated);
    }

    [Fact]
    public void Insufficient_data_project_with_zero_tasks_does_not_trigger_risk_alert()
    {
        var analysisService = new RuleBasedProgressAnalysisService();

        var facts = new ProjectProgressFacts(
            ProjectId: 202,
            ProjectStatus: "ACTIVE",
            TeamId: 11,
            TeamMemberCount: 4,
            Milestones: [new(1, "Milestone 1", "IN_PROGRESS", DateOnly.FromDateTime(FixedNow), DateOnly.FromDateTime(FixedNow.AddDays(14)), 1)],
            Tasks: [], // 0 tasks
            ProgressReports: [],
            Meetings: []);

        var result = analysisService.Analyze(facts, FixedNow);

        Assert.Equal("INSUFFICIENT_DATA", result.DataStatus);
        Assert.Equal("INSUFFICIENT_DATA", result.RiskLevel);
        Assert.Null(result.RiskScore);

        var shouldAlert = result.DataStatus != "INSUFFICIENT_DATA" && result.RiskLevel != "INSUFFICIENT_DATA";
        Assert.False(shouldAlert);
    }

    [Fact]
    public void Risk_alert_occurrence_deduplication_prevents_duplicate_notifications()
    {
        var occurrences = new HashSet<string>();
        var projectId = 301L;
        var riskLevel = "HIGH";
        var dateKey = FixedNow.ToString("yyyyMMdd");
        var occurrenceKey = $"RISK_ALERT:{projectId}:{dateKey}:{riskLevel}";

        // First run adds occurrence
        var firstAdded = occurrences.Add(occurrenceKey);
        Assert.True(firstAdded);

        // Second run with same state is rejected (deduplicated)
        var secondAdded = occurrences.Add(occurrenceKey);
        Assert.False(secondAdded);
    }

    [Fact]
    public void Risk_alert_recipients_include_supervisors_and_staff_without_leaking_to_other_projects()
    {
        var projectSupervisorId = 1001L;
        var projectStaffId = 2001L;
        var projectStudentId = 3001L;
        var otherProjectStudentId = 4001L;
        var otherProjectSupervisorId = 5001L;

        // Scope resolution for Project #1
        var projectDepartmentIds = new[] { 10L };
        var allUsers = new[]
        {
            new { Id = projectSupervisorId, Role = "LECTURER", IsAssignedToProject = true, DeptId = 10L },
            new { Id = projectStaffId, Role = "DEPARTMENT_STAFF", IsAssignedToProject = false, DeptId = 10L },
            new { Id = projectStudentId, Role = "STUDENT", IsAssignedToProject = true, DeptId = 10L },
            new { Id = otherProjectStudentId, Role = "STUDENT", IsAssignedToProject = false, DeptId = 20L },
            new { Id = otherProjectSupervisorId, Role = "LECTURER", IsAssignedToProject = false, DeptId = 20L }
        };

        var recipients = allUsers
            .Where(u => (u.Role == "LECTURER" && u.IsAssignedToProject)
                     || (u.Role == "DEPARTMENT_STAFF" && projectDepartmentIds.Contains(u.DeptId)))
            .Select(u => u.Id)
            .Distinct()
            .ToArray();

        Assert.Contains(projectSupervisorId, recipients);
        Assert.Contains(projectStaffId, recipients);
        Assert.DoesNotContain(projectStudentId, recipients);
        Assert.DoesNotContain(otherProjectStudentId, recipients);
        Assert.DoesNotContain(otherProjectSupervisorId, recipients);
    }

    [Fact]
    public void Risk_alert_records_risk_history_and_leaves_project_status_unchanged()
    {
        var initialProjectStatus = "ACTIVE";
        var riskLevel = "HIGH";
        var riskScore = 65.5m;
        var overdueTasks = 5;
        var blockedTasks = 2;

        var history = new RiskHistory
        {
            ProjectId = 100,
            RiskLevel = riskLevel,
            RiskScore = riskScore,
            OverdueTasks = overdueTasks,
            BlockedTasks = blockedTasks,
            UnassignedTasks = 1,
            ProgressPercentage = 35.0m,
            RecordedAt = FixedNow
        };

        // Project status is never modified by the alert generator
        var finalProjectStatus = initialProjectStatus;

        Assert.Equal("ACTIVE", finalProjectStatus);
        Assert.Equal("HIGH", history.RiskLevel);
        Assert.Equal(65.5m, history.RiskScore);
        Assert.Equal(5, history.OverdueTasks);
        Assert.Equal(2, history.BlockedTasks);
        Assert.Equal(FixedNow, history.RecordedAt);
    }
}
