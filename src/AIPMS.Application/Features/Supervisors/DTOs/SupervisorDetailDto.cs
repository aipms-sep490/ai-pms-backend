namespace AIPMS.Application.Features.Supervisors.DTOs;

public sealed record SupervisorDetailDto(
    long Id,
    long UserId,
    string FullName,
    string Email,
    string? Phone,
    string? EmployeeCode,
    string? Title,
    string? Bio,
    int? MaxActiveProjects,
    bool IsAvailable,
    IReadOnlyList<SupervisorExpertiseDto> Expertises);
