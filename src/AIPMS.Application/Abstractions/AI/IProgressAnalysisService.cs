using System;
using System.Collections.Generic;
using AIPMS.Application.Features.Projects.DTOs;
using AIPMS.Application.Features.Projects.Models;

namespace AIPMS.Application.Abstractions.AI;

public interface IProgressAnalysisService
{
    ProjectProgressAnalysisDto Analyze(
        ProjectProgressFacts facts,
        DateTime analysisTimeUtc);

    ProgressAnalysisResult Analyze(ProgressAnalysisInput input);
}

public sealed record ProgressAnalysisInput(
    int TotalTasks,
    int OverdueTasks,
    int BlockedTasks,
    decimal MilestoneCompletionRate);

public sealed record ProgressAnalysisResult(
    string RiskLevel,
    decimal OverdueTaskRatio,
    decimal BlockedTaskRatio,
    IReadOnlyList<string> Recommendations);
