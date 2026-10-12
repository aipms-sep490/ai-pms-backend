using System.Threading.Tasks;
using AIPMS.Application.Features.AiAssistant.Abstractions;
using AIPMS.Application.Features.AiAssistant.DTOs;
using AIPMS.Application.Features.AiAssistant.Services;

namespace AIPMS.Infrastructure.Services.Projects;

internal sealed class RecordedAiAssistantService(AiAssistantService inner, IAiRunStore runs) : IAiAssistantService
{
    public async Task<ProjectAssistantResponseDto> AskAsync(long projectId, string query, CancellationToken cancellationToken = default)
    {
        var id = await runs.BeginAsync(projectId, "PROJECT_ASK", null, cancellationToken);
        try
        {
            var result = (await inner.AskAsync(projectId, query, cancellationToken)) with { RunId = id };
            await runs.CompleteAsync(id, result, null, null, cancellationToken); return result;
        }
        catch { await Failed(id); throw; }
    }
    public async Task<ReportSummaryDto> SummarizeReportAsync(long projectId, long reportId, CancellationToken cancellationToken = default)
    {
        var id = await runs.BeginAsync(projectId, "REPORT_SUMMARY", reportId, cancellationToken);
        try
        {
            var result = (await inner.SummarizeReportAsync(projectId, reportId, cancellationToken)) with { RunId = id };
            await runs.CompleteAsync(id, null, result, null, cancellationToken); return result;
        }
        catch { await Failed(id); throw; }
    }
    private async Task Failed(long id)
    {
        using var bounded = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try { await runs.CompleteAsync(id, null, null, "AI_RUN_INTERRUPTED", bounded.Token); }
        catch { /* Preserve the original failure; a RUNNING row indicates an interrupted attempt. */ }
    }
}
