using AIPMS.Application.Features.ExecutionCapabilities.DTOs;

namespace AIPMS.Application.Features.ExecutionCapabilities.Abstractions;

public interface IProjectCapabilityService
{
    Task<ProjectCapabilitiesDto> GetAsync(long projectId, CancellationToken ct);
}
