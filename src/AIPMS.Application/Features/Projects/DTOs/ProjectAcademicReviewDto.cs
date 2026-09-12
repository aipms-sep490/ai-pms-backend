using AIPMS.Application.Features.Teams.DTOs;

namespace AIPMS.Application.Features.Projects.DTOs;

public sealed record RegistrationPolicyDto(int MinMembers, int MaxMembers, int MinDistinctMajors, string Version);
public sealed record RegisteredMemberDto(long UserId, string FullName, long MajorId, bool IsLeader);

public sealed record RegistrationEvidence(TeamAcademicScopeDto Scope, RegistrationPolicyDto Policy,
    long OrganizationId, DateTime WindowStartAt, DateTime WindowEndAt,
    IReadOnlyList<RegisteredMemberDto> Members, IReadOnlyList<long> DepartmentIds);

public sealed record DepartmentDecisionDto(long DepartmentId, string Decision, long? DecidedBy,
    DateTime? DecidedAt, string? Reason);

public sealed record RegistrationSnapshotDto(long Id, long ProjectPeriodId, long SubmittedBy,
    DateTime SubmittedAt, RegistrationEvidence Evidence, IReadOnlyList<DepartmentDecisionDto> Decisions);

public sealed record ProjectAcademicReviewDto(string ConcurrencyToken, TeamAcademicScopeDto? AcademicScope,
    RegistrationSnapshotDto? LatestSubmission);

public sealed record DepartmentDecisionRequest(long SnapshotId, string ConcurrencyToken, string Decision, string? Reason);
