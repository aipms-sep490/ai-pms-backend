using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Deliverables.DTOs;

namespace AIPMS.Application.Features.Deliverables.Abstractions;

public interface IDeliverableRepository
{
    Task<bool> ProjectExistsAsync(long projectId, CancellationToken cancellationToken);
    Task<bool> IsProjectActiveAsync(long projectId, CancellationToken cancellationToken);
    Task<bool> IsActiveTeamMemberAsync(long userId, long projectId, CancellationToken cancellationToken);
    Task<DeliverableDto?> GetByIdAsync(long id, CancellationToken cancellationToken);
    Task<PagedResult<DeliverableDto>> GetPagedAsync(long projectId, int pageNumber, int pageSize, CancellationToken cancellationToken);
    Task<long> CreateAsync(long projectId, long? milestoneId, string title, string? description, string? deliverableType, DateTime? dueAt, long createdBy, CancellationToken cancellationToken);
    Task UpdateAsync(long id, string title, string? description, string? deliverableType, DateTime? dueAt, CancellationToken cancellationToken);
    Task DeleteAsync(long id, CancellationToken cancellationToken);
    Task<SubmissionTarget?> GetSubmissionTargetAsync(long deliverableId, CancellationToken cancellationToken);
    Task<int> GetNextVersionNumberAsync(long deliverableId, CancellationToken cancellationToken);
    Task<long?> GetActiveSupervisorAssignmentIdAsync(long projectId, CancellationToken cancellationToken);
    Task<long> AddVersionAndFileAsync(VersionSubmission submission, CancellationToken cancellationToken);
    Task<IReadOnlyList<DeliverableVersionDto>> GetHistoryAsync(long deliverableId, CancellationToken cancellationToken);
    Task<FileDownloadTarget?> GetFileForDownloadAsync(long fileId, CancellationToken cancellationToken);
    Task<(long ProjectId, long DeliverableId)?> GetVersionParentAsync(long versionId, CancellationToken cancellationToken);
    Task DeleteUnsubmittedFileAsync(long fileId, CancellationToken cancellationToken);
    Task<bool> IsCurrentUserAssignedSupervisorAsync(long userId, long projectId, CancellationToken cancellationToken);
    Task AddFeedbackAsync(long deliverableVersionId, long assignmentId, long projectId, string feedbackText, CancellationToken cancellationToken);
}

public sealed record SubmissionTarget(long DeliverableId, long ProjectId, DateTime? DueAt, string DeliverableStatus);
public sealed record VersionSubmission(long DeliverableId, int VersionNumber, long SubmittedBy, string? Note, string OriginalFileName, string MimeType, long Size, string Checksum, string StorageKey, long? ReviewerAssignmentId);
public sealed record FileDownloadTarget(long FileId, long ProjectId, string StorageKey, string OriginalFileName, string? MimeType, bool IsImmutable);
