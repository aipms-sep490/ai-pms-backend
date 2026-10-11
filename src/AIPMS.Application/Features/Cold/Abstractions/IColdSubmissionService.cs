using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Deliverables.DTOs;
using AIPMS.Application.Features.Deliverables.Models;

namespace AIPMS.Application.Features.Cold.Abstractions;

public interface IColdSubmissionService
{
    Task<ColdTemplateDto> CreateTemplate(CreateColdTemplateRequest input, CancellationToken ct);
    Task<PagedResult<ColdTemplateDto>> Templates(long majorId, long periodId, int page, int pageSize, CancellationToken ct);
    Task<ColdChecklistDto> Apply(long projectId, ApplyColdChecklistRequest input, CancellationToken ct);
    Task<ColdChecklistDto> Checklist(long projectId, CancellationToken ct);
    Task<ColdSubmissionDto> SaveDraft(long itemId, SaveColdDraftRequest input, CancellationToken ct);
    Task<ColdSubmissionDto> Upload(long id, string token, UploadContent content, CancellationToken ct);
    Task<ColdSubmissionDto> RemoveFile(long id, long fileId, string token, CancellationToken ct);
    Task<ColdSubmissionDto> Submit(long id, string token, CancellationToken ct);
    Task<ColdChecklistDto> Lock(long projectId, string token, CancellationToken ct);
    Task<PagedResult<ColdSubmissionDto>> History(long itemId, int page, int pageSize, CancellationToken ct);
    Task<FileDownload> Download(long id, long fileId, CancellationToken ct);
}
