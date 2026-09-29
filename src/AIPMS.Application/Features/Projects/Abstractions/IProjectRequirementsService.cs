using AIPMS.Application.Features.Projects.DTOs;

namespace AIPMS.Application.Features.Projects.Abstractions;

public interface IProjectRequirementsService
{
    Task<ProjectRequirementsDto> GetAsync(long projectId, CancellationToken ct);
    Task<ProjectRequirementsDto> ReplaceAsync(long projectId, ReplaceProjectRequirementsRequest request, CancellationToken ct);
    Task<ProjectReviewHistoryDto> GetHistoryAsync(long projectId, int page, int pageSize, CancellationToken ct);
}
