using System;
using System.Collections.Generic;

namespace AIPMS.Application.Features.Projects.DTOs;

public sealed record ProjectProgressAnalysisDto(
    long ProjectId,
    DateTime GeneratedAtUtc,
    DateTime AnalysisTimeUtc,
    string DataStatus,
    string RiskLevel,
    double? RiskScore,
    double? Confidence,
    string TrendStatus,
    ProgressSummaryDto ProgressSummary,
    FeatureSnapshotDto FeatureSnapshot,
    IReadOnlyList<RiskFactorDto> Factors,
    IReadOnlyList<string> Recommendations,
    string RuleVersion,
    string FeatureVersion,
    string ModelVersion,
    string? Limitations);

public sealed record ProgressSummaryDto(
    int TotalMilestones,
    int CompletedMilestones,
    int TotalTasks,
    int DoneTasks,
    int BlockedTasks,
    int OverdueTasks,
    int UnassignedTasks,
    double ProgressPercentage);

public sealed record FeatureSnapshotDto(
    double? OverdueTaskRatio,
    double? AverageTaskDelayDays,
    double? BlockedTaskRatio,
    double? MilestoneCompletionRate,
    double? MilestoneDelayDays,
    int? MilestoneNearDueCount,
    double? ReportSubmissionDelayDays,
    int? MissingReportCount,
    int? MeetingFrequencyCount,
    double? UnassignedTaskRatio,
    double? ContributionVariance);

public sealed record RiskFactorDto(
    string Code,
    string Feature,
    double ObservedValue,
    string Severity,
    string Explanation);
