using AIPMS.Application.Features.Deliverables.DTOs;
using AIPMS.Application.Features.Evaluations.DTOs;

namespace AIPMS.Application.Features.Evaluations.Abstractions;

public interface IEvaluationAssignmentAccessService
{
    Task<EvaluationAssignmentDetailDto> GetAsync(long assignmentId, CancellationToken ct = default);
    Task<EvaluationAssignmentEvidenceDto> EvidenceAsync(long assignmentId, CancellationToken ct = default);
    Task<FileDownload> DownloadEvidenceFileAsync(long assignmentId, long fileId, CancellationToken ct = default);
}
