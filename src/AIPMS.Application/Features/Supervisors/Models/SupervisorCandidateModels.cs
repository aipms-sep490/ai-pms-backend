namespace AIPMS.Application.Features.Supervisors.Models;

public sealed record SupervisorCandidateProject(long Id, long AcademicSemesterId,
    string Status, bool HasActiveSemester, bool HasActiveAssignment,
    IReadOnlyList<long> DepartmentIds, IReadOnlyList<long>? RequiredMajorIds = null,
    IReadOnlyList<SupervisorMajorScope>? MajorScopes = null, IReadOnlyList<long>? OccupiedMentorMajors = null);

public sealed record SupervisorMajorScope(long Id, long DepartmentId, string Code, string Name);

public sealed record SupervisorSelectionPolicy(long PeriodId, int? MaxProjectsPerSupervisor);

public sealed record SupervisorCandidateSearch(long ProjectId, long AcademicSemesterId,
    IReadOnlyList<long> DepartmentIds, int SemesterLimit, string? Search,
    string? Expertise, int Page, int PageSize, string AssignmentType = "PRIMARY", long? MajorId = null);

public sealed record SupervisorCandidateModel(SupervisorProfileModel Profile,
    int? ProfileLimit, int ActiveProjects, int SemesterActiveProjects);
