using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Supervisors.DTOs;

namespace AIPMS.Application.Features.Supervisors.Abstractions;

public interface ISupervisorRecommendationService
{
    Task<SupervisorRecommendationPreview> Preview(long projectId, SupervisorRecommendationRequest request, CancellationToken ct);
    Task<SupervisorRecommendationRun> Create(long projectId, SupervisorRecommendationRequest request, CancellationToken ct);
    Task<PagedResult<SupervisorRecommendationRunSummary>> History(long projectId, int page, int pageSize, CancellationToken ct);
    Task<SupervisorRecommendationRun> Get(long projectId, long runId, CancellationToken ct);
}
