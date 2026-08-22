using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Supervisors.DTOs;
using AIPMS.Domain.Entities;

namespace AIPMS.Application.Features.Supervisors.Abstractions;

public interface ISupervisorRepository
{
    Task<PagedResult<SupervisorDto>> GetPagedSupervisorsAsync(
        int pageNumber,
        int pageSize,
        string? search,
        bool? isAvailable,
        string? expertise,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<SupervisorCandidateDto>> GetEligibleCandidatesAsync(
        string? expertise,
        CancellationToken cancellationToken);

    Task<SupervisorDetailDto?> GetByIdAsync(long id, CancellationToken cancellationToken);

    Task<SupervisorProfile?> GetProfileByIdAsync(long id, CancellationToken cancellationToken);

    Task<SupervisorProfile?> GetProfileByUserIdAsync(long userId, CancellationToken cancellationToken);

    Task UpdateProfileAsync(SupervisorProfile profile, CancellationToken cancellationToken);

    Task UpdateExpertisesAsync(long supervisorProfileId, IEnumerable<SupervisorExpertise> expertises, CancellationToken cancellationToken);

    Task<bool> ExistsAsync(long id, CancellationToken cancellationToken);
}
