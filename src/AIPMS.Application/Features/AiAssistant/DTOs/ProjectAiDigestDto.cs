using System;
using System.Collections.Generic;
using AIPMS.Application.Features.AiAssistant.Models;
using AIPMS.Application.Features.Projects.DTOs;

namespace AIPMS.Application.Features.AiAssistant.DTOs;

/// <summary>Read-only project digest built from the scoped progress analysis.</summary>
public sealed record ProjectAiDigestDto(
    long ProjectId,
    DateTime GeneratedAtUtc,
    string DataStatus,
    string RiskLevel,
    double? RiskScore,
    ProgressDigestSummary ProgressSummary,
    IReadOnlyList<ProgressRiskSignalDto> Signals,
    IReadOnlyList<string> NextActions,
    IReadOnlyList<EvidenceReferenceDto> Evidence,
    string? Limitations);

public sealed record ProgressDigestSummary(
    int? TotalMilestones,
    int? CompletedMilestones,
    int TotalTasks,
    int DoneTasks,
    int BlockedTasks,
    int OverdueTasks,
    double ProgressPercentage);
