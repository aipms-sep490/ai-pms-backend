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
        DateTime analysisTimeUtc,
        System.Threading.CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(facts);

        var totalMilestones = facts.Milestones.Count;
        var nonCancelledMilestones = facts.Milestones
            .Where(static m => !m.Status.Equals("CANCELLED", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var nonCancelledMilestonesCount = nonCancelledMilestones.Count;
        var completedMilestones = nonCancelledMilestones
            .Count(static m => m.Status.Equals("COMPLETED", StringComparison.OrdinalIgnoreCase));

        var totalTasks = facts.Tasks.Count;
        var doneTasks = facts.Tasks.Count(static t => t.Status.Equals("DONE", StringComparison.OrdinalIgnoreCase));
        var blockedTasks = facts.Tasks.Count(static t => t.Status.Equals("BLOCKED", StringComparison.OrdinalIgnoreCase));

        var activeTasks = facts.Tasks.Where(t => ActiveTaskStatuses.Contains(t.Status)).ToList();
        var totalActiveTasks = activeTasks.Count;

        var unassignedTasksList = activeTasks
            .Where(static t => t.AssigneeCount == 0)
            .ToList();
        var unassignedTaskCount = unassignedTasksList.Count;

        // Progress percentage calculation matching BE-05 formula
        var progressPercentage = totalTasks == 0
            ? 0.0
            : Math.Round((doneTasks * 100.0) / totalTasks, 2);

        // Check Minimum Record Sufficiency
        if (totalTasks == 0 || totalMilestones == 0)
        {
            var emptySummary = new ProgressSummaryDto(
                totalMilestones,
                completedMilestones,
                totalTasks,
                doneTasks,
                blockedTasks,
                0,
                unassignedTaskCount,
                progressPercentage);

            var emptyFeatureSnapshot = new FeatureSnapshotDto(
                OverdueTaskRatio: null,
                AverageTaskDelayDays: null,
                BlockedTaskRatio: null,
                MilestoneCompletionRate: null,
                MilestoneDelayDays: null,
                MilestoneNearDueCount: null,
                ReportSubmissionDelayDays: null,
                MissingReportCount: null,
                MeetingFrequencyCount: 0,
                UnassignedTaskRatio: null,
                ContributionVariance: null);

            return ProjectProgressAnalysisDtoMapper.ToDto(
                facts.ProjectId,
                analysisTimeUtc,
                analysisTimeUtc,
                "INSUFFICIENT_DATA",
                "INSUFFICIENT_DATA",
                null,
                0.0,
                "INSUFFICIENT_DATA",
                emptySummary,
                emptyFeatureSnapshot,
                Array.Empty<RiskFactorDto>(),
                new[] { "Add milestones, tasks, and deadlines before requesting a progress risk analysis." },
                RuleBaselineConfig.RuleVersion,
                RuleBaselineConfig.FeatureVersion,
                RuleBaselineConfig.ModelVersion,
                "Insufficient task and milestone data to perform a reliable risk assessment.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        // P2 FIX #2: Milestone Completion Rate denominator excludes CANCELLED milestones
        double? milestoneCompletionRate = null;
        if (nonCancelledMilestonesCount > 0)
        {
            milestoneCompletionRate = Math.Round((double)completedMilestones / nonCancelledMilestonesCount, 4);
        }

        // Status-based task ratios
        double? blockedTaskRatio;
        double? unassignedTaskRatio;
        if (totalActiveTasks > 0)
        {
            blockedTaskRatio = Math.Round((double)blockedTasks / totalActiveTasks, 4);
            unassignedTaskRatio = Math.Round((double)unassignedTaskCount / totalActiveTasks, 4);
        }
        else
        {
            blockedTaskRatio = 0.0;
            unassignedTaskRatio = 0.0;
        }

        // P2 FIX #3: Deadline-based task features require actual due date evidence
        var activeTasksWithDueAt = activeTasks.Where(static t => t.DueAt.HasValue).ToList();
        double? overdueTaskRatio = null;
        double? averageTaskDelayDays = null;
        var overdueTasksList = new List<TaskFact>();

        if (totalActiveTasks == 0)
        {
            overdueTaskRatio = 0.0;
            averageTaskDelayDays = 0.0;
        }
        else if (activeTasksWithDueAt.Count > 0)
        {
            overdueTasksList = activeTasksWithDueAt
                .Where(t => t.DueAt!.Value < analysisTimeUtc)
                .ToList();
            var overdueTaskCount = overdueTasksList.Count;
            overdueTaskRatio = Math.Round((double)overdueTaskCount / activeTasksWithDueAt.Count, 4);

            if (overdueTaskCount > 0)
            {
                var totalDelay = overdueTasksList
                    .Sum(t => Math.Max(0.0, (analysisTimeUtc - t.DueAt!.Value).TotalDays));
                averageTaskDelayDays = Math.Round(totalDelay / overdueTaskCount, 2);
            }
            else
            {
                averageTaskDelayDays = 0.0;
            }
        }

        // P2 FIX #2 & #3: Milestone deadlines exclude CANCELLED milestones and require due date evidence
        var todayDate = DateOnly.FromDateTime(analysisTimeUtc);
        var nonCancelledWithDueDate = nonCancelledMilestones
            .Where(static m => m.DueDate.HasValue)
            .ToList();

        double? milestoneDelayDays = null;
        int? milestoneNearDueCount = null;
        var overdueMilestonesList = new List<MilestoneFact>();

        if (nonCancelledMilestonesCount > 0 && nonCancelledWithDueDate.Count > 0)
        {
            overdueMilestonesList = nonCancelledWithDueDate
                .Where(m => !m.Status.Equals("COMPLETED", StringComparison.OrdinalIgnoreCase)
                         && m.DueDate!.Value < todayDate)
                .ToList();

            if (overdueMilestonesList.Count > 0)
            {
                var totalMilestoneDelay = overdueMilestonesList
                    .Sum(m => Math.Max(0, todayDate.DayNumber - m.DueDate!.Value.DayNumber));
                milestoneDelayDays = Math.Round((double)totalMilestoneDelay / overdueMilestonesList.Count, 2);
            }
            else
            {
                milestoneDelayDays = 0.0;
            }

            var nearDueThresholdDate = todayDate.AddDays(RuleBaselineConfig.MilestoneNearDueThresholdDays);
            milestoneNearDueCount = nonCancelledWithDueDate
                .Count(m => !m.Status.Equals("COMPLETED", StringComparison.OrdinalIgnoreCase)
                         && m.DueDate!.Value >= todayDate
                         && m.DueDate!.Value <= nearDueThresholdDate);
        }

        // P2 FIX #1: Authoritative reporting schedule / deadline policy is unavailable in BE-03A.
        // PeriodEnd is only an interval boundary and NOT a submission deadline.
        // MissingReportCount and ReportSubmissionDelayDays remain null to avoid false signals.
        double? reportDelayDays = null;
        int? missingReportCountFeature = null;

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
            ReportSubmissionDelayDays: reportDelayDays,
            MissingReportCount: missingReportCountFeature,
            MeetingFrequencyCount: meetingFrequencyCount,
            UnassignedTaskRatio: unassignedTaskRatio,
            ContributionVariance: null);

        var progressSummary = new ProgressSummaryDto(
            totalMilestones,
            completedMilestones,
            totalTasks,
            doneTasks,
            blockedTasks,
            overdueTasksList.Count,
            unassignedTaskCount,
            progressPercentage);

        // Feature availability tracking across all 11 defined feature slots
        int availableFeatureCount = 0;
        if (overdueTaskRatio.HasValue) availableFeatureCount++;
        if (averageTaskDelayDays.HasValue) availableFeatureCount++;
        if (blockedTaskRatio.HasValue) availableFeatureCount++;
        if (milestoneCompletionRate.HasValue) availableFeatureCount++;
        if (milestoneDelayDays.HasValue) availableFeatureCount++;
        if (milestoneNearDueCount.HasValue) availableFeatureCount++;
        if (reportDelayDays.HasValue) availableFeatureCount++;
        if (missingReportCountFeature.HasValue) availableFeatureCount++;
        if (featureSnapshot.MeetingFrequencyCount.HasValue) availableFeatureCount++;
        if (unassignedTaskRatio.HasValue) availableFeatureCount++;
        if (featureSnapshot.ContributionVariance.HasValue) availableFeatureCount++;

        var confidence = Math.Round((double)availableFeatureCount / 11.0, 2);

        // Limitations explanation
        var limitationsList = new List<string>();

        if (totalActiveTasks > 0)
        {
            if (activeTasksWithDueAt.Count == 0)
            {
                limitationsList.Add("Active tasks do not have due dates; deadline-based delay features were not evaluated.");
            }
            else if (activeTasksWithDueAt.Count < totalActiveTasks)
            {
                limitationsList.Add("Some active tasks do not have due dates; deadline-based delay features reflect partial evidence.");
            }
        }

        if (nonCancelledMilestonesCount > 0)
        {
            if (nonCancelledWithDueDate.Count == 0)
            {
                limitationsList.Add("Milestones do not have due dates; milestone deadline features were not evaluated.");
            }
            else if (nonCancelledWithDueDate.Count < nonCancelledMilestonesCount)
            {
                limitationsList.Add("Some milestones do not have due dates; milestone deadline features reflect partial evidence.");
            }
        }
        else if (totalMilestones > 0 && nonCancelledMilestonesCount == 0)
        {
            limitationsList.Add("All milestones are CANCELLED; milestone completion and deadline features are unavailable.");
        }

        limitationsList.Add("Progress report schedule policy is unavailable (INSUFFICIENT_DATA); missing/late report features were not evaluated.");
        limitationsList.Add("ContributionVariance is marked INSUFFICIENT_DATA pending BE-13.");

        var limitationsNote = string.Join(" ", limitationsList);

        // DataStatus is SUFFICIENT only when enough evidence exists:
        // non-cancelled milestones exist, deadline evidence is not completely absent, and at least 6 features available
        bool hasSufficientDeadlineEvidence =
            (totalActiveTasks == 0 || activeTasksWithDueAt.Count > 0) &&
            (nonCancelledMilestonesCount == 0 || nonCancelledWithDueDate.Count > 0);

        bool isDataSufficient = nonCancelledMilestonesCount > 0
                             && hasSufficientDeadlineEvidence
                             && availableFeatureCount >= 6;

        var dataStatus = isDataSufficient ? "SUFFICIENT" : "INSUFFICIENT_DATA";

        // Generate Explainable Factors & Mapped Recommendations
        var factors = new List<RiskFactorDto>();
        var recommendations = new List<string>();

        if (overdueTaskRatio.HasValue && overdueTaskRatio.Value >= RuleBaselineConfig.MediumOverdueRatioThreshold)
        {
            var severity = overdueTaskRatio.Value >= RuleBaselineConfig.CriticalOverdueRatioThreshold
                ? "CRITICAL"
                : (overdueTaskRatio.Value >= RuleBaselineConfig.HighOverdueRatioThreshold ? "HIGH" : "MEDIUM");
            factors.Add(new RiskFactorDto(
                "OVERDUE_TASKS",
                "OverdueTaskRatio",
                overdueTaskRatio.Value,
                severity,
                $"{Math.Round(overdueTaskRatio.Value * 100, 1)}% of evaluated active tasks have missed their due date."));
            recommendations.Add("Review overdue tasks and assign recovery owners in the upcoming sprint.");
        }

        if (blockedTaskRatio.HasValue && blockedTaskRatio.Value >= 0.15)
        {
            var severity = blockedTaskRatio.Value >= RuleBaselineConfig.CriticalBlockedRatioThreshold
                ? "CRITICAL"
                : (blockedTaskRatio.Value >= RuleBaselineConfig.HighBlockedRatioThreshold ? "HIGH" : "MEDIUM");
            factors.Add(new RiskFactorDto(
                "BLOCKED_TASKS",
                "BlockedTaskRatio",
                blockedTaskRatio.Value,
                severity,
                $"{Math.Round(blockedTaskRatio.Value * 100, 1)}% of active tasks are currently in BLOCKED status."));
            recommendations.Add("Escalate technical dependencies and blockers with the assigned supervisor.");
        }

        if (unassignedTaskRatio.HasValue && unassignedTaskRatio.Value >= 0.15)
        {
            factors.Add(new RiskFactorDto(
                "UNASSIGNED_TASKS",
                "UnassignedTaskRatio",
                unassignedTaskRatio.Value,
                "MEDIUM",
                $"{Math.Round(unassignedTaskRatio.Value * 100, 1)}% of active tasks do not have an assigned owner."));
            recommendations.Add("Assign team members to unassigned backlog tasks.");
        }

        if (milestoneDelayDays.HasValue && overdueMilestonesList.Count > 0)
        {
            factors.Add(new RiskFactorDto(
                "MILESTONE_OVERDUE",
                "MilestoneDelayDays",
                milestoneDelayDays.Value,
                "HIGH",
                $"{overdueMilestonesList.Count} milestone(s) are overdue past their deadline."));
            recommendations.Add("Re-plan current milestone deliverables against project deadlines.");
        }
        else if (milestoneNearDueCount.HasValue && milestoneNearDueCount.Value > 0 && milestoneCompletionRate.HasValue && milestoneCompletionRate.Value < 1.0)
        {
            factors.Add(new RiskFactorDto(
                "MILESTONE_DUE_SOON_LOW_COMPLETION",
                "MilestoneNearDueCount",
                milestoneNearDueCount.Value,
                milestoneCompletionRate.Value < 0.5 ? "HIGH" : "MEDIUM",
                $"{milestoneNearDueCount.Value} upcoming milestone(s) are due within {RuleBaselineConfig.MilestoneNearDueThresholdDays} days while completion rate is {Math.Round(milestoneCompletionRate.Value * 100, 1)}%."));
            recommendations.Add("Expedite remaining deliverables for upcoming milestone due dates.");
        }
        else if (isDataSufficient && milestoneCompletionRate.HasValue && milestoneCompletionRate.Value < RuleBaselineConfig.MediumMilestoneCompletionThreshold && nonCancelledMilestonesCount > 0)
        {
            factors.Add(new RiskFactorDto(
                "MILESTONE_PROGRESS_SLOW",
                "MilestoneCompletionRate",
                milestoneCompletionRate.Value,
                "MEDIUM",
                $"Milestone completion rate is currently at {Math.Round(milestoneCompletionRate.Value * 100, 1)}%."));
            recommendations.Add("Accelerate key deliverable reviews to complete pending milestone targets.");
        }

        if (recommendations.Count == 0)
        {
            if (dataStatus.Equals("INSUFFICIENT_DATA", StringComparison.OrdinalIgnoreCase))
            {
                recommendations.Add("Add milestones, tasks, and deadlines before requesting a progress risk analysis.");
            }
            else
            {
                recommendations.Add("Continue monitoring task execution according to current plan.");
            }
        }

        // Determine Risk Score & Risk Level ignoring unavailable features
        double? riskScore = null;
        string riskLevel;

        if (!isDataSufficient)
        {
            // Unavailable features must not be treated as healthy zero.
            // Check if strong real signals are observed despite partial/insufficient data.
            if (blockedTaskRatio.HasValue && blockedTaskRatio.Value >= RuleBaselineConfig.CriticalBlockedRatioThreshold)
            {
                riskLevel = "CRITICAL";
                riskScore = 80.0;
            }
            else if (blockedTaskRatio.HasValue && blockedTaskRatio.Value >= RuleBaselineConfig.HighBlockedRatioThreshold)
            {
                riskLevel = "HIGH";
                riskScore = 55.0;
            }
            else
            {
                riskLevel = "INSUFFICIENT_DATA";
                riskScore = null;
            }
        }
        else
        {
            var rawScore = ((overdueTaskRatio ?? 0.0) * RuleBaselineConfig.OverdueWeight)
                         + ((blockedTaskRatio ?? 0.0) * RuleBaselineConfig.BlockedWeight)
                         + ((1.0 - (milestoneCompletionRate ?? 0.0)) * RuleBaselineConfig.MilestoneWeight)
                         + ((unassignedTaskRatio ?? 0.0) * RuleBaselineConfig.UnassignedWeight);
            riskScore = Math.Round(rawScore, 1);

            if (riskScore >= RuleBaselineConfig.CriticalRiskScoreThreshold
                || (overdueTaskRatio.HasValue && overdueTaskRatio.Value >= RuleBaselineConfig.CriticalOverdueRatioThreshold)
                || (blockedTaskRatio.HasValue && blockedTaskRatio.Value >= RuleBaselineConfig.CriticalBlockedRatioThreshold))
            {
                riskLevel = "CRITICAL";
            }
            else if (riskScore >= RuleBaselineConfig.HighRiskScoreThreshold
                     || (overdueTaskRatio.HasValue && overdueTaskRatio.Value >= RuleBaselineConfig.HighOverdueRatioThreshold)
                     || (blockedTaskRatio.HasValue && blockedTaskRatio.Value >= RuleBaselineConfig.HighBlockedRatioThreshold))
            {
                riskLevel = "HIGH";
            }
            else if (riskScore >= RuleBaselineConfig.MediumRiskScoreThreshold
                     || (overdueTaskRatio.HasValue && overdueTaskRatio.Value >= RuleBaselineConfig.MediumOverdueRatioThreshold)
                     || (milestoneCompletionRate.HasValue && milestoneCompletionRate.Value < RuleBaselineConfig.MediumMilestoneCompletionThreshold)
                     || (unassignedTaskRatio.HasValue && unassignedTaskRatio.Value >= 0.25))
            {
                riskLevel = "MEDIUM";
            }
            else
            {
                riskLevel = "LOW";
            }
        }

        return ProjectProgressAnalysisDtoMapper.ToDto(
            facts.ProjectId,
            analysisTimeUtc,
            analysisTimeUtc,
            dataStatus,
            riskLevel,
            riskScore,
            confidence,
            "INSUFFICIENT_DATA",
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
