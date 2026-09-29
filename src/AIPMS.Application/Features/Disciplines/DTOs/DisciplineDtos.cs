namespace AIPMS.Application.Features.Disciplines.DTOs;

public sealed record ResponsibilityInput(string Content, int SortOrder);
public sealed record ResponsibilityDto(long Id, long MajorId, string Content, int SortOrder, string ConcurrencyToken);
public sealed record ResponsibilityListDto(string? ConcurrencyToken, bool IsSnapshot, bool IsAvailable, IReadOnlyList<ResponsibilityDto> Items);
public sealed record ReplaceResponsibilitiesRequest(string ConcurrencyToken, IReadOnlyList<ResponsibilityInput> Items);
public sealed record TaskDisciplineInput(long MajorId, string Role);
public sealed record TaskDisciplinesDto(string ConcurrencyToken, string Classification, IReadOnlyList<TaskDisciplineInput> Items);
public sealed record ReplaceTaskDisciplinesRequest(string ConcurrencyToken, IReadOnlyList<TaskDisciplineInput> Items);
public sealed record CreateProjectEvidenceRequest(string SourceType, long SourceId, long? MajorId, string? Notes = null);
public sealed record ProjectEvidenceDto(long Id, long ProjectId, string SourceType, long SourceId, long? MajorId,
    string Classification, string VerificationStatus, long SubmittedBy, DateTime SubmittedAt, string? Notes);
