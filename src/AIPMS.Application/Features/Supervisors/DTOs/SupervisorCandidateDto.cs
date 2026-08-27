namespace AIPMS.Application.Features.Supervisors.DTOs;

public sealed record SupervisorCandidateDto(
    long SupervisorId,
    string FullName,
    int CurrentActiveProjects,
    int? MaxActiveProjects,
    int? AvailableCapacity,
    IReadOnlyList<SupervisorExpertiseDto> Expertises,
    bool AiAvailable = false,
    string? AiRationale = null);
