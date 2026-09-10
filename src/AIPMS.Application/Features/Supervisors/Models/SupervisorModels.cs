namespace AIPMS.Application.Features.Supervisors.Models;

public sealed record SupervisorAccount(long UserId, long? DepartmentId, bool IsActive,
    bool HasActiveAcademicScope, IReadOnlyList<string> Roles);

public sealed record SupervisorExpertiseModel(string Name, string? ProficiencyLevel);

public sealed record SupervisorProfileModel(long Id, long UserId, string FullName,
    long DepartmentId, string DepartmentName, string? Bio, bool IsAvailable,
    IReadOnlyList<SupervisorExpertiseModel> Expertise);

public sealed record SupervisorSearch(long? DepartmentId, string? Search, string? Expertise,
    bool? IsAvailable, int Page, int PageSize);
