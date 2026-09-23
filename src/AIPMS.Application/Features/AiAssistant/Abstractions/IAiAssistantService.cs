using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Features.AiAssistant.DTOs;

namespace AIPMS.Application.Features.AiAssistant.Abstractions;

public interface IAiAssistantService
{
    Task<ReportSummaryDto> SummarizeReportAsync(
        long projectId,
        long reportId,
        CancellationToken cancellationToken = default);

    Task<ProjectAssistantResponseDto> AskAsync(
        long projectId,
        string query,
        CancellationToken cancellationToken = default);
}
