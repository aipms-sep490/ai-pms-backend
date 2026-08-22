namespace AIPMS.Application.Features.Supervisors.DTOs;

public sealed record SupervisorDto(
    long Id,
    long UserId,
    string FullName,
    string Email,
    string? Title,
    string? Bio,
    int? MaxActiveProjects,
    bool IsAvailable);
