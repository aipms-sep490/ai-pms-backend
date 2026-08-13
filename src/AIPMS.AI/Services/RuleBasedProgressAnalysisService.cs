using AIPMS.Application.Abstractions.AI;

namespace AIPMS.AI.Services;

public sealed class RuleBasedProgressAnalysisService : IProgressAnalysisService
{
    public ProgressAnalysisResult Analyze(ProgressAnalysisInput input)
    {
        if (input.TotalTasks == 0)
        {
            return new ProgressAnalysisResult(
                "LOW",
                0,
                0,
                ["Add tasks and deadlines before requesting a progress-risk analysis."]);
        }

        var overdueRatio = decimal.Round((decimal)input.OverdueTasks / input.TotalTasks, 2);
        var blockedRatio = decimal.Round((decimal)input.BlockedTasks / input.TotalTasks, 2);
        var riskLevel = CalculateRisk(overdueRatio, blockedRatio, input.MilestoneCompletionRate);

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

    private static string CalculateRisk(
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
