namespace AIPMS.Application.Features.Projects.DTOs;

public sealed record ProjectMajorRequirementInput(long MajorId, int MinMembers, int MaxMembers, string Responsibility);
public sealed record ProjectMajorRequirementDto(long Id, long MajorId, int MinMembers, int MaxMembers,
    string Responsibility, string ConcurrencyToken);
public sealed record ReplaceProjectRequirementsRequest(IReadOnlyList<ProjectMajorRequirementInput> Requirements, string ConcurrencyToken);
public sealed record ProjectRequirementsDto(string ConcurrencyToken, IReadOnlyList<ProjectMajorRequirementDto> Requirements);
public sealed record ProjectProposalSnapshotDto(string Title, string? Description, string? ProblemStatement,
    string? Objectives, string? ExpectedOutput, string ProposalSource, long? TopicId,
    IReadOnlyList<long> MajorIds, IReadOnlyList<ProjectTagDto> Tags);
public sealed record ProjectReviewSnapshotDto(long Id, int SubmissionNumber, long ProjectPeriodId, long SubmittedBy,
    DateTime SubmittedAt, RegistrationEvidence? Evidence, bool ProposalAvailable, IReadOnlyList<DepartmentDecisionDto> Decisions,
    string AcademicScopeProvenance = "UNKNOWN");
public sealed record ProjectReviewHistoryDto(int Page, int PageSize, int TotalCount, IReadOnlyList<ProjectReviewSnapshotDto> Items);
