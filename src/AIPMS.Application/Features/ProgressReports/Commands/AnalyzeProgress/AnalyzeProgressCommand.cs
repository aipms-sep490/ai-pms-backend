using AIPMS.Application.Abstractions.AI;
using MediatR;

namespace AIPMS.Application.Features.ProgressReports.Commands.AnalyzeProgress;

public sealed record AnalyzeProgressCommand(
    int TotalTasks,
    int OverdueTasks,
    int BlockedTasks,
    decimal MilestoneCompletionRate) : IRequest<ProgressAnalysisResult>;

public sealed class AnalyzeProgressCommandHandler(IProgressAnalysisService progressAnalysisService)
    : IRequestHandler<AnalyzeProgressCommand, ProgressAnalysisResult>
{
    public Task<ProgressAnalysisResult> Handle(
        AnalyzeProgressCommand request,
        CancellationToken cancellationToken)
    {
        var input = new ProgressAnalysisInput(
            request.TotalTasks,
            request.OverdueTasks,
            request.BlockedTasks,
            request.MilestoneCompletionRate);

        return Task.FromResult(progressAnalysisService.Analyze(input));
    }
}
