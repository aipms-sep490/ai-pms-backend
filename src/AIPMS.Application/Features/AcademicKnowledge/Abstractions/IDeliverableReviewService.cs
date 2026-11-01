using AIPMS.Application.Common.Models;

namespace AIPMS.Application.Features.AcademicKnowledge.Abstractions;

public interface IDeliverableReviewService
{
    Task<DeliverableReviewOptions> OptionsAsync(long projectId, long versionId, CancellationToken ct);
    Task<DeliverableReviewResponseDto> ReviewAsync(long projectId, long versionId, DeliverableReviewRequest request, CancellationToken ct);
    Task<PagedResult<DeliverableReviewResponseDto>> ListAsync(long projectId, long? versionId, int page, int pageSize, CancellationToken ct);
    Task<DeliverableReviewResponseDto> GetAsync(long projectId, long runId, CancellationToken ct);
}
