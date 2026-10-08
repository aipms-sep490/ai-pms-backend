namespace AIPMS.Application.Features.Supervisors.DTOs;

public sealed record SupervisorCandidateDto(long Id, long UserId, string FullName,
    long DepartmentId, string DepartmentName, string? Bio,
    IReadOnlyList<SupervisorExpertiseDto> Expertise,
    int ActiveProjects, int SemesterActiveProjects, int? ProfileLimit,
    int SemesterLimit, int RemainingSlots, long SelectionPeriodId,
    string AssignmentType = "PRIMARY", long? MajorId = null, long? ResponsibleDepartmentId = null,
    bool Eligible = true, IReadOnlyList<string>? Reasons = null);

public sealed record SupervisorReplacementCandidateDto(SupervisorCandidateDto Candidate,
    string AssignmentType, long? MajorId, long? ResponsibleDepartmentId, bool Eligible,
    IReadOnlyList<string> Reasons, string ExpertiseMatch = "UNKNOWN");
