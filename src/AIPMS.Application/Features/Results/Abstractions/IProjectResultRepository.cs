using AIPMS.Application.Features.Results.DTOs;

namespace AIPMS.Application.Features.Results.Abstractions;

public interface IProjectResultRepository
{
    Task<ResultPolicyDto?> PolicyAsync(long projectId, CancellationToken ct);
    Task<bool> AnyFinalizedAsync(long projectId, CancellationToken ct);
    Task<ResultPolicyDto> ConfigureAsync(long projectId, ConfigureResultPolicyRequest input, long actorId, DateTime now, CancellationToken ct);
    Task<ProjectResultDto?> GetAsync(long projectId, CancellationToken ct);
    Task<ProjectResultDto> PublishAsync(ProjectResultDto result, CancellationToken ct);
    Task<bool> IsRequiredAsync(long assignmentId, CancellationToken ct);
}
