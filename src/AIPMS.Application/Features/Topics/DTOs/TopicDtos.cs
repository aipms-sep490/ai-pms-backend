namespace AIPMS.Application.Features.Topics.DTOs;

public sealed record TopicMajorRequirementRequest(long MajorId, int MinMembers, int MaxMembers, string Responsibility);
public sealed record TopicContentRequest(string Title, string? Description, string? ProblemStatement,
    string? Objectives, string? ExpectedOutput, string? Domain, IReadOnlyList<string> Technologies,
    IReadOnlyList<string> Keywords, string ProjectMode, long? PrimaryMajorId,
    IReadOnlyList<TopicMajorRequirementRequest> Requirements);
public sealed record CreateTopicRequest(long ProjectPeriodId, string Code, long LeadDepartmentId, TopicContentRequest Content);
public sealed record UpdateTopicRequest(Guid ConcurrencyToken, TopicContentRequest Content);
public sealed record PublishTopicRequest(Guid ConcurrencyToken);
public sealed record CloseTopicRequest(Guid ConcurrencyToken, string Reason);
public sealed record TopicMajorRequirementDto(long MajorId, string MajorCode, string MajorName, long DepartmentId,
    string DepartmentName, int MinMembers, int MaxMembers, string Responsibility);
public sealed record TopicDto(long Id, string Code, string Status, long ProjectPeriodId, long AcademicSemesterId,
    long OrganizationId, long LeadDepartmentId, string LeadDepartmentName, string Title, string? Description,
    string? ProblemStatement, string? Objectives, string? ExpectedOutput, string? Domain,
    IReadOnlyList<string> Technologies, IReadOnlyList<string> Keywords, string ProjectMode, long? PrimaryMajorId,
    IReadOnlyList<TopicMajorRequirementDto> Requirements, long CreatedBy, long UpdatedBy, DateTime CreatedAt,
    DateTime UpdatedAt, long? PublishedBy, DateTime? PublishedAt, long? ClosedBy, DateTime? ClosedAt,
    string? CloseReason, Guid ConcurrencyToken, bool? MatchesMyMajor);
