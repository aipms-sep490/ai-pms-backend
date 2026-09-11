namespace AIPMS.Application.Features.Deliverables.DTOs;

public sealed record DeliverableDto(long Id, long ProjectId, long? MilestoneId, string Title,
    string? Description, string? DeliverableType, DateTime? DueAt, string Status, long CreatedBy, int LatestVersion);
public sealed record ProjectFileDto(long Id, string ParentType, long ParentId, string FileName,
    string ContentType, long SizeBytes, string Sha256, long UploadedBy, DateTime CreatedAt);
public sealed record DeliverableVersionDto(long Id, long DeliverableId, int VersionNumber, long SubmittedBy,
    string? Note, string Status, DateTime SubmittedAt, IReadOnlyList<ProjectFileDto> Files);
public sealed record DeliverableFeedbackDto(long Id, long ProjectId, long VersionId, long AssignmentId,
    long SupervisorUserId, string Feedback, DateTime CreatedAt);
public sealed record SaveDeliverableRequest(long? MilestoneId, string Title, string? Description,
    string? DeliverableType, DateTime? DueAt);
public sealed record ReviewDeliverableRequest(string Decision, string Feedback);

public sealed record FileDownload(Stream Content, string ContentType, string FileName);
