using System;
using System.Collections.Generic;
using System.Linq;
using AIPMS.AI.Configuration;
using AIPMS.Application.Abstractions.AI;
using AIPMS.Application.Features.Projects.DTOs;
using AIPMS.Application.Features.Projects.Models;

namespace AIPMS.AI.Services;

public sealed class RuleBasedProgressAnalysisService : IProgressAnalysisService
{
    private static readonly HashSet<string> ActiveTaskStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "TODO", "IN_PROGRESS", "BLOCKED", "IN_REVIEW"
    };

    public ProjectProgressAnalysisDto Analyze(
        ProjectProgressFacts facts,
        DateTime analysisTimeUtc)
    {
        ArgumentNullException.ThrowIfNull(facts);

        var totalMilestones = facts.Milestones.Count;
        var completedMilestones = facts.Milestones.Count(static m => m.Status.Equals("COMPLETED", StringComparison.OrdinalIgnoreCase));

        var totalTasks = facts.Tasks.Count;
        var doneTasks = facts.Tasks.Count(static t => t.Status.Equals("DONE", StringComparison.OrdinalIgnoreCase));
        var blockedTasks = facts.Tasks.Count(static t => t.Status.Equals("BLOCKED", StringComparison.OrdinalIgnoreCase));

        var activeTasks = facts.Tasks.Where(t => ActiveTaskStatuses.Contains(t.Status)).ToList();
        var totalActiveTasks = activeTasks.Count;

        var overdueTasksList = activeTasks
            .Where(t => t.DueAt.HasValue && t.DueAt.Value < analysisTimeUtc)
            .ToList();
        var overdueTaskCount = overdueTasksList.Count;

        var unassignedTasksList = activeTasks
            .Where(static t => t.AssigneeCount == 0)
            .ToList();
        var unassignedTaskCount = unassignedTasksList.Count;

        // Progress percentage calculation matching BE-05 formula
        var progressPercentage = totalTasks == 0
            ? 0.0
            : Math.Round((doneTasks * 100.0) / totalTasks, 2);

        var progressSummary = new ProgressSummaryDto(
            totalMilestones,
            completedMilestones,
            totalTasks,
            doneTasks,
            blockedTasks,
            overdueTaskCount,
            unassignedTaskCount,
            progressPercentage);

        // Check Data Sufficiency
        if (totalTasks == 0 || totalMilestones == 0)
        {
            var emptyFeatureSnapshot = new FeatureSnapshotDto(
                OverdueTaskRatio: totalActiveTasks == 0 ? 0.0 : 0.0,
                AverageTaskDelayDays: 0.0,
                BlockedTaskRatio: totalActiveTasks == 0 ? 0.0 : 0.0,
                MilestoneCompletionRate: totalMilestones == 0 ? 0.0 : 0.0,
                MilestoneDelayDays: 0.0,
                MilestoneNearDueCount: 0,
                ReportSubmissionDelayDays: null,
                MissingReportCount: null,
                MeetingFrequencyCount: 0,
                UnassignedTaskRatio: totalActiveTasks == 0 ? 0.0 : 0.0,
                ContributionVariance: null);

            return new ProjectProgressAnalysisDto(
                facts.ProjectId,
                analysisTimeUtc,
                analysisTimeUtc,
                "INSUFFICIENT_DATA",
                "INSUFFICIENT_DATA",
                null,
                0.0,
                "NOT_AVAILABLE",
                progressSummary,
                emptyFeatureSnapshot,
                Array.Empty<RiskFactorDto>(),
                new[] { "Add milestones, tasks, and deadlines before requesting a progress risk analysis." },
                RuleBaselineConfig.RuleVersion,
                RuleBaselineConfig.FeatureVersion,
                RuleBaselineConfig.ModelVersion,
                "Insufficient task and milestone data to perform a reliable risk assessment.");
        }

        // Calculate Feature Ratios
        var overdueTaskRatio = totalActiveTasks == 0 ? 0.0 : Math.Round((double)overdueTaskCount / totalActiveTasks, 4);
        var blockedTaskRatio = totalActiveTasks == 0 ? 0.0 : Math.Round((double)blockedTasks / totalActiveTasks, 4);
        var unassignedTaskRatio = totalActiveTasks == 0 ? 0.0 : Math.Round((double)unassignedTaskCount / totalActiveTasks, 4);
        var milestoneCompletionRate = totalMilestones == 0 ? 0.0 : Math.Round((double)completedMilestones / totalMilestones, 4);

        double averageTaskDelayDays = 0.0;
        if (overdueTaskCount > 0)
        {
            var totalDelay = overdueTasksList
                .Sum(t => Math.Max(0.0, (analysisTimeUtc - t.DueAt!.Value).TotalDays));
            averageTaskDelayDays = Math.Round(totalDelay / overdueTaskCount, 2);
        }

        var todayDate = DateOnly.FromDateTime(analysisTimeUtc);
        var overdueMilestonesList = facts.Milestones
            .Where(m => !m.Status.Equals("COMPLETED", StringComparison.OrdinalIgnoreCase)
                     && m.DueDate.HasValue
                     && m.DueDate.Value < todayDate)
            .ToList();

        double milestoneDelayDays = 0.0;
        if (overdueMilestonesList.Count > 0)
        {
            var totalMilestoneDelay = overdueMilestonesList
                .Sum(m => Math.Max(0.0, (todayDate.DayNumber - m.DueDate!.Value.DayNumber)));
            milestoneDelayDays = Math.Round((double)totalMilestoneDelay / overdueMilestonesList.Count, 2);
        }

        var nearDueThresholdDate = todayDate.AddDays(RuleBaselineConfig.MilestoneNearDueThresholdDays);
        var milestoneNearDueCount = facts.Milestones
            .Count(m => !m.Status.Equals("COMPLETED", StringComparison.OrdinalIgnoreCase)
                     && m.DueDate.HasValue
                     && m.DueDate.Value >= todayDate
                     && m.DueDate.Value <= nearDueThresholdDate);

        var meetingLookbackCutoff = analysisTimeUtc.AddDays(-RuleBaselineConfig.MeetingLookbackDays);
        var meetingFrequencyCount = facts.Meetings
            .Count(m => !m.Status.Equals("CANCELLED", StringComparison.OrdinalIgnoreCase)
                     && m.StartAt >= meetingLookbackCutoff
                     && m.StartAt <= analysisTimeUtc);

        var featureSnapshot = new FeatureSnapshotDto(
            overdueTaskRatio,
            averageTaskDelayDays,
            blockedTaskRatio,
            milestoneCompletionRate,
            milestoneDelayDays,
            milestoneNearDueCount,
            ReportSubmissionDelayDays: null,
            MissingReportCount: null,
            meetingFrequencyCount,
            unassignedTaskRatio,
            ContributionVariance: null);

        // Compute Weighted Risk Score (0 - 100)
        var rawScore = (overdueTaskRatio * RuleBaselineConfig.OverdueWeight)
                     + (blockedTaskRatio * RuleBaselineConfig.BlockedWeight)
                     + ((1.0 - milestoneCompletionRate) * RuleBaselineConfig.MilestoneWeight)
                     + (unassignedTaskRatio * RuleBaselineConfig.UnassignedWeight);
        var riskScore = Math.Round(rawScore, 1);

        // Determine Risk Level
        string riskLevel;
        if (riskScore >= RuleBaselineConfig.CriticalRiskScoreThreshold
            || overdueTaskRatio >= RuleBaselineConfig.CriticalOverdueRatioThreshold
            || blockedTaskRatio >= RuleBaselineConfig.CriticalBlockedRatioThreshold)
        {
            riskLevel = "CRITICAL";
        }
        else if (riskScore >= RuleBaselineConfig.HighRiskScoreThreshold
                 || overdueTaskRatio >= RuleBaselineConfig.HighOverdueRatioThreshold
                 || blockedTaskRatio >= RuleBaselineConfig.HighBlockedRatioThreshold)
        {
            riskLevel = "HIGH";
        }
        else if (riskScore >= RuleBaselineConfig.MediumRiskScoreThreshold
                 || overdueTaskRatio >= RuleBaselineConfig.MediumOverdueRatioThreshold
                 || milestoneCompletionRate < RuleBaselineConfig.MediumMilestoneCompletionThreshold)
        {
            riskLevel = "MEDIUM";
        }
        else
        {
            riskLevel = "LOW";
        }

        // Generate Explainable Factors & Mapped Recommendations
        var factors = new List<RiskFactorDto>();
        var recommendations = new List<string>();

        if (overdueTaskRatio >= RuleBaselineConfig.MediumOverdueRatioThreshold)
        {
            var severity = overdueTaskRatio >= RuleBaselineConfig.CriticalOverdueRatioThreshold ? "CRITICAL" : "HIGH";
            factors.Add(new RiskFactorDto(
                "OVERDUE_TASKS",
                "OverdueTaskRatio",
                overdueTaskRatio,
                severity,
                $"{Math.Round(overdueTaskRatio * 100, 1)}% of active tasks have missed their due date."));
            recommendations.Add("Review overdue tasks and assign recovery owners in the upcoming sprint.");
        }

        if (blockedTaskRatio >= 0.15)
        {
            var severity = blockedTaskRatio >= RuleBaselineConfig.CriticalBlockedRatioThreshold ? "CRITICAL" : "HIGH";
            factors.Add(new RiskFactorDto(
                "BLOCKED_TASKS",
                "BlockedTaskRatio",
                blockedTaskRatio,
                severity,
                $"{Math.Round(blockedTaskRatio * 100, 1)}% of active tasks are currently in BLOCKED status."));
            recommendations.Add("Escalate technical dependencies and blockers with the assigned supervisor.");
        }

        if (unassignedTaskRatio >= 0.15)
        {
            factors.Add(new RiskFactorDto(
                "UNASSIGNED_TASKS",
                "UnassignedTaskRatio",
                unassignedTaskRatio,
                "MEDIUM",
                $"{Math.Round(unassignedTaskRatio * 100, 1)}% of active tasks do not have an assigned owner."));
            recommendations.Add("Assign team members to unassigned backlog tasks.");
        }

        if (overdueMilestonesList.Count > 0)
        {
            factors.Add(new RiskFactorDto(
                "MILESTONE_OVERDUE",
                "MilestoneDelayDays",
                milestoneDelayDays,
                "HIGH",
                $"{overdueMilestonesList.Count} milestone(s) are overdue past their deadline."));
            recommendations.Add("Re-plan current milestone deliverables against project deadlines.");
        }
        else if (milestoneCompletionRate < RuleBaselineConfig.MediumMilestoneCompletionThreshold && totalMilestones > 0)
        {
            factors.Add(new RiskFactorDto(
                "MILESTONE_PROGRESS_SLOW",
                "MilestoneCompletionRate",
                milestoneCompletionRate,
                "MEDIUM",
                $"Milestone completion rate is currently at {Math.Round(milestoneCompletionRate * 100, 1)}%."));
            recommendations.Add("Accelerate key deliverable reviews to complete pending milestone targets.");
        }

        if (recommendations.Count == 0)
        {
            recommendations.Add("Continue monitoring task execution according to current plan.");
        }

        // Data Quality / Confidence metric (9 non-null features out of 11 = ~0.82)
        var confidence = 0.82;
        var limitationsNote = "ReportSubmissionDelay, MissingReport, and ContributionVariance are currently marked NOT_AVAILABLE due to policy/dependency data gaps.";

        return new ProjectProgressAnalysisDto(
            facts.ProjectId,
            analysisTimeUtc,
            analysisTimeUtc,
            "SUFFICIENT",
            riskLevel,
            riskScore,
            confidence,
            "NOT_AVAILABLE",
            progressSummary,
            featureSnapshot,
            factors,
            recommendations,
            RuleBaselineConfig.RuleVersion,
            RuleBaselineConfig.FeatureVersion,
            RuleBaselineConfig.ModelVersion,
            limitationsNote);
    }

    public ProgressAnalysisResult Analyze(ProgressAnalysisInput input)
    {
        if (input.TotalTasks == 0)
        {
            return new ProgressAnalysisResult(
                "LOW",
                0m,
                0m,
                new[] { "Add tasks and deadlines before requesting a progress-risk analysis." });
        }

        var overdueRatio = decimal.Round((decimal)input.OverdueTasks / input.TotalTasks, 2);
        var blockedRatio = decimal.Round((decimal)input.BlockedTasks / input.TotalTasks, 2);
        var riskLevel = CalculateLegacyRisk(overdueRatio, blockedRatio, input.MilestoneCompletionRate);

        var recommendations = new List<string>();
        if (overdueRatio >= 0.2m)
        {
            recommendations.Add("Review overdue tasks and assign recovery owners.");
        }

        if (blockedRatio >= 0.15m)
        {
            recommendations.Add("Escalate blockers in the next supervisor meeting.");
        }

        if (input.MilestoneCompletionRate < 0.5m)
        {
            recommendations.Add("Re-plan the current milestone against its deadline.");
        }

        if (recommendations.Count == 0)
        {
            recommendations.Add("Continue monitoring the current plan.");
        }

        return new ProgressAnalysisResult(riskLevel, overdueRatio, blockedRatio, recommendations);
    }

    private static string CalculateLegacyRisk(
        decimal overdueRatio,
        decimal blockedRatio,
        decimal milestoneCompletionRate)
    {
        if (overdueRatio >= 0.6m || blockedRatio >= 0.5m)
        {
            return "CRITICAL";
        }

        if (overdueRatio >= 0.4m || blockedRatio >= 0.3m || milestoneCompletionRate < 0.35m)
        {
            return "HIGH";
        }

        return overdueRatio >= 0.2m || blockedRatio >= 0.15m || milestoneCompletionRate < 0.6m
            ? "MEDIUM"
            : "LOW";
    }
}
