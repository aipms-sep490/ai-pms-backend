namespace AIPMS.Application.Features.Deliverables.DTOs;

public sealed record DeliverableDto(long Id, long ProjectId, long? MilestoneId, string Title, string? Description, string? DeliverableType, DateTime? DueAt, string Status, long CreatedBy, DateTime CreatedAt);
public sealed record DeliverableVersionDto(long Id, long DeliverableId, int VersionNumber, long SubmittedBy, string? SubmissionNote, string Status, DateTime SubmittedAt, IReadOnlyList<FileMetadataDto> Files, IReadOnlyList<SupervisorFeedbackDto> Feedback);
public sealed record FileMetadataDto(long Id, string OriginalFileName, string? MimeType, long FileSizeBytes, string? ChecksumSha256, DateTime CreatedAt, long UploadedBy);
public sealed record SupervisorFeedbackDto(long Id, long SupervisorAssignmentId, string FeedbackText, DateTime CreatedAt);
public sealed record DownloadFileDto(string OriginalFileName, string? MimeType, Stream Content);
