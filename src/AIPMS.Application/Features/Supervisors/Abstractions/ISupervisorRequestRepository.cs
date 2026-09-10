using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Supervisors.Models;

namespace AIPMS.Application.Features.Supervisors.Abstractions;

public interface ISupervisorRequestRepository
{
    Task<T> InTransactionAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct);
    Task LockRequestAsync(long requestId, CancellationToken ct);
    Task LockSupervisorAndProjectAsync(long profileId, long projectId, CancellationToken ct);
    Task<bool> IsTeamLeaderAsync(long projectId, long userId, CancellationToken ct);
    Task<SupervisorRequestModel?> GetAsync(long requestId, CancellationToken ct);
    Task<PagedResult<SupervisorRequestModel>> SearchAsync(SupervisorRequestSearch search, CancellationToken ct);
    Task<bool> HasPendingAsync(long projectId, long profileId, CancellationToken ct);
    Task<SupervisorWorkload> GetWorkloadAsync(long profileId, long semesterId, CancellationToken ct);
    Task<SupervisorRequestModel> CreateAsync(long projectId, long profileId, long actorId,
        string? message, DateTime now, CancellationToken ct);
    Task<SupervisorRequestModel> RespondAsync(long requestId, string status, string? message, DateTime now, CancellationToken ct);
    Task<long> AssignAndActivateAsync(SupervisorRequestModel request, long actorId, DateTime now, CancellationToken ct);
    Task<IReadOnlyList<SupervisorRequestModel>> GetOtherPendingAsync(long projectId, long requestId, CancellationToken ct);
}
