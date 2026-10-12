using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.AiAssistant.DTOs;

namespace AIPMS.Application.Features.AiAssistant.Abstractions;

public interface IAiRunStore
{
    Task<long> BeginAsync(long projectId, string kind, long? reportId, CancellationToken ct);
    Task CompleteAsync(long id, ProjectAssistantResponseDto? answer, ReportSummaryDto? report, string? error, CancellationToken ct);
    Task CompleteProgressAsync(long id, AIPMS.Application.Features.Projects.DTOs.ProjectProgressAnalysisDto result, CancellationToken ct);
    Task<PagedResult<AiRunDto>> ListAsync(long projectId, int page, int pageSize, CancellationToken ct);
    Task<AiRunDto> GetAsync(long projectId, long id, CancellationToken ct);
}
