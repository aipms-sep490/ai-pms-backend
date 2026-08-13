namespace AIPMS.Application.Abstractions.AI;

public interface IProgressAnalysisService
{
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
