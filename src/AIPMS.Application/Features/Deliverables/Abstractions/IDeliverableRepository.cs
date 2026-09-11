using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Deliverables.DTOs;
using AIPMS.Application.Features.Deliverables.Models;

namespace AIPMS.Application.Features.Deliverables.Abstractions;

public interface IDeliverableRepository
{
    Task<T> InTransactionAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct, Func<Task>? onRollback = null);
    Task LockProjectAsync(long projectId, CancellationToken ct);
    Task<DeliverableProject?> GetProjectAsync(long projectId, long actorId, CancellationToken ct);
    Task<bool> HasExecutionWindowAsync(long semesterId, DateTime now, CancellationToken ct);
    Task<bool> MilestoneBelongsAsync(long milestoneId, long projectId, CancellationToken ct);
    Task<DeliverableDto?> GetAsync(long id, CancellationToken ct);
    Task<PagedResult<DeliverableDto>> SearchAsync(DeliverableSearch search, CancellationToken ct);
    Task<DeliverableDto> SaveAsync(long? id, long projectId, SaveDeliverableRequest data, long actorId, DateTime now, CancellationToken ct);
    Task DeleteAsync(long id, CancellationToken ct);
    Task<DeliverableVersionDto?> GetVersionAsync(long id, CancellationToken ct);
    Task<PagedResult<DeliverableVersionDto>> VersionsAsync(long deliverableId, int page, int pageSize, CancellationToken ct);
    Task<DeliverableVersionDto> SubmitAsync(long id, int number, long actorId, string? note, string storageKey,
        ValidatedUpload file, DateTime now, CancellationToken ct);
    Task<DeliverableFeedbackDto> ReviewAsync(long versionId, long assignmentId, long actorId,
        string decision, string feedback, DateTime now, CancellationToken ct);
    Task<PagedResult<DeliverableFeedbackDto>> FeedbackAsync(long versionId, int page, int pageSize, CancellationToken ct);
    Task<FileParent?> GetParentAsync(string type, long id, CancellationToken ct);
    Task<StoredProjectFile?> GetFileAsync(long id, CancellationToken ct);
    Task<PagedResult<ProjectFileDto>> FilesAsync(FileSearch search, CancellationToken ct);
    Task<ProjectFileDto> AttachAsync(FileParent parent, string key, ValidatedUpload file, long actorId, DateTime now, CancellationToken ct);
    Task DeleteFileAsync(long id, CancellationToken ct);
}
