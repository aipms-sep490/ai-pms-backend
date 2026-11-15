using AIPMS.Application.Features.Deliverables.DTOs;
using AIPMS.Application.Features.Deliverables.Models;

namespace AIPMS.Application.Features.DepartmentDocuments.Abstractions;

public interface IDepartmentDocumentService
{
    Task<IReadOnlyList<DepartmentDocumentDto>> List(long majorId, long? projectPeriodId, CancellationToken ct);
    Task<DepartmentDocumentDto> Upload(long majorId, long? projectPeriodId, string code, string title, UploadContent file, CancellationToken ct);
    Task<DepartmentDocumentDto> Publish(long id, string token, CancellationToken ct);
    Task<DepartmentDocumentDto> Retire(long id, string token, CancellationToken ct);
    Task<FileDownload> Download(long id, CancellationToken ct);
}
