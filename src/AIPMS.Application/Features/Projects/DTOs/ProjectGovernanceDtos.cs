namespace AIPMS.Application.Features.Projects.DTOs;

public sealed record ProjectGovernanceDto(
    long ProjectId,
    string ProjectStatus,
    GovernanceDepartmentDto? LeadDepartment,
    IReadOnlyList<GovernanceDepartmentDto> ParticipatingDepartments,
    IReadOnlyList<GovernanceSupervisorDto> Supervisors,
    IReadOnlyList<GovernanceEvaluatorDto> Evaluators,
    string? FinalSubmissionStatus,
    string? ResultPublicationStatus,
    GovernanceReadinessDto Readiness,
    IReadOnlyList<string> Blockers,
    GovernanceActorScopeDto ActorScope,
    IReadOnlyList<string> AllowedActions);

public sealed record GovernanceDepartmentDto(long DepartmentId, string Code, string Name, bool IsLead, IReadOnlyList<long> MajorIds);
public sealed record GovernanceSupervisorDto(long AssignmentId, long UserId, string FullName, string AssignmentType, long? MajorId, bool IsActive);
public sealed record GovernanceEvaluatorDto(long AssignmentId, long EvaluatorId, string EvaluationType, string Scope, long? MajorId, long? StudentId, string Status);
public sealed record GovernanceReadinessDto(bool CanSubmitFinal, bool CanEvaluate, bool CanPublishResult, bool IsProjectActive, bool HasPrimarySupervisor);
public sealed record GovernanceActorScopeDto(string ScopeKind, long? DepartmentId, IReadOnlyList<long> MajorIds, bool IsAdmin);
