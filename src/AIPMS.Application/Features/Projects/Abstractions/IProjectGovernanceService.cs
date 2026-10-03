using AIPMS.Application.Features.Projects.DTOs;

namespace AIPMS.Application.Features.Projects.Abstractions;

public interface IProjectGovernanceService
{
    Task<ProjectGovernanceDto> GetAsync(long projectId, CancellationToken ct = default);
}
