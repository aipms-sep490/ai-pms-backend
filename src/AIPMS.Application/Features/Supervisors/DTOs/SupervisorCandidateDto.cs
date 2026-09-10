namespace AIPMS.Application.Features.Supervisors.DTOs;

public sealed record SupervisorCandidateDto(long Id, long UserId, string FullName,
    long DepartmentId, string DepartmentName, string? Bio,
    IReadOnlyList<SupervisorExpertiseDto> Expertise,
    int ActiveProjects, int SemesterActiveProjects, int? ProfileLimit,
    int SemesterLimit, int RemainingSlots, long SelectionPeriodId);
