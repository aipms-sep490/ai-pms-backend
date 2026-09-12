namespace AIPMS.Application.Features.WorkflowContext.DTOs;

public sealed record WorkflowActionDto(string Code, bool Allowed, IReadOnlyList<string> Reasons);
public sealed record WorkflowUserDto(long Id, string Email, string FullName, string Status,
    string? StudentCode, string? EmployeeCode, IReadOnlyList<string> Roles,
    IReadOnlyList<string> GrantedPermissions, IReadOnlyList<string> EffectiveRoles, bool RequiresTokenRefresh);
public sealed record AcademicReferenceDto(long Id, string Code, string Name, bool IsActive);
public sealed record UserAcademicContextDto(AcademicReferenceDto? Organization, AcademicReferenceDto? Department,
    AcademicReferenceDto? Major, bool HasActiveDepartmentScope, bool HasEligibleStudentProfile, IReadOnlyList<string> Issues);
public sealed record WorkflowSemesterDto(long Id, long OrganizationId, string Code, string Name, string Status,
    DateOnly StartDate, DateOnly EndDate, bool IsCurrent);
public sealed record WorkflowPeriodDto(long Id, string Code, string Name, string PeriodType, string Status,
    DateTimeOffset StartAtUtc, DateTimeOffset EndAtUtc, bool IsOpen);
public sealed record WorkflowTeamSummaryDto(long Id, string Code, string Name, string Status, bool IsLeader,
    long? ProjectId, string? ProjectStatus);
public sealed record UserWorkflowContextDto(DateTimeOffset AsOfUtc, WorkflowUserDto User,
    UserAcademicContextDto Academic, IReadOnlyList<WorkflowSemesterDto> CurrentSemesters,
    WorkflowSemesterDto? SelectedSemester, IReadOnlyList<string> SemesterSelectionIssues,
    IReadOnlyList<WorkflowPeriodDto> Periods, WorkflowTeamSummaryDto? CurrentTeam, IReadOnlyList<WorkflowActionDto> Actions);
public sealed record TeamWorkflowActionsDto(DateTimeOffset AsOfUtc, long TeamId, long AcademicSemesterId,
    string Status, Guid? AcademicScopeConcurrencyToken, bool CanRegister, IReadOnlyList<string> EligibilityIssues,
    IReadOnlyList<WorkflowActionDto> Actions);
public sealed record ProjectWorkflowActionsDto(DateTimeOffset AsOfUtc, long ProjectId, string Status,
    string ConcurrencyToken, long? SubmissionSnapshotId, long? ActorDepartmentId,
    IReadOnlyList<WorkflowActionDto> Actions);
