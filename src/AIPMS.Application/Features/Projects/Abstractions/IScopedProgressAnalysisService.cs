using AIPMS.Application.Features.Projects.DTOs;

namespace AIPMS.Application.Features.Projects.Abstractions;

public interface IScopedProgressAnalysisService
{
    Task<ProjectProgressAnalysisDto> AnalyzeAsync(long projectId, bool record, CancellationToken ct);
}
