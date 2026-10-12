using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Deliverables.DTOs;
using AIPMS.Application.Features.Deliverables.Models;

namespace AIPMS.Application.Features.Appeals.Abstractions;
public interface IAppealService
{
    Task<AppealPolicyDto?> Policy(long projectId, CancellationToken ct);
    Task<AppealPolicyDto> RecordPolicy(long projectId, RecordAppealPolicyRequest input, CancellationToken ct);
    Task<AppealWindowDto> OpenWindow(long projectId, OpenAppealWindowRequest input, CancellationToken ct);
    Task<PagedResult<AppealWindowDto>> Windows(long projectId, int page, int pageSize, CancellationToken ct);
    Task<AppealWindowDto> CloseWindow(long projectId, long id, string token, CancellationToken ct);
    Task<CorrectionRequestDto> Create(long projectId, CreateCorrectionRequest input, CancellationToken ct);
    Task<PagedResult<CorrectionRequestDto>> List(long projectId, int page, int pageSize, CancellationToken ct);
    Task<CorrectionRequestDto> Get(long projectId, long id, CancellationToken ct);
    Task<CorrectionRequestDto> Assign(long projectId, long id, AssignAppealReviewerRequest input, CancellationToken ct);
    Task<CorrectionRequestDto> Review(long projectId, long id, ReviewAppealRequest input, CancellationToken ct);
    Task<AppealPreviewDto> Preview(long projectId, long id, CancellationToken ct);
    Task<AppealReviewContextDto> ReviewContext(long projectId, long id, CancellationToken ct);
    Task<CorrectionRequestDto> Decide(long projectId, long id, AppealDecisionRequest input, CancellationToken ct);
    Task<CorrectionRequestDto> Upload(long projectId, long id, string token, UploadContent content, CancellationToken ct);
    Task<FileDownload> Download(long projectId, long id, long fileId, CancellationToken ct);
    Task<IReadOnlyList<AppealFileDto>> Evidence(long projectId, long id, CancellationToken ct);
    Task<FileDownload> DownloadEvidence(long projectId, long id, long fileId, CancellationToken ct);
    Task<PagedResult<AppealEventDto>> History(long projectId, long id, int page, int pageSize, CancellationToken ct);
}
