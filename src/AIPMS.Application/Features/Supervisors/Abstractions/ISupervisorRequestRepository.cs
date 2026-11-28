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
    Task<bool> HasPendingAsync(long projectId, long profileId, CancellationToken ct, string assignmentType = "PRIMARY", long? majorId = null);
    Task<bool> HasConflictingRoleAsync(long projectId, long profileId, string assignmentType, CancellationToken ct) => Task.FromResult(false);
    Task<bool> HasActiveConflictingRoleAssignmentAsync(long projectId, long profileId, string assignmentType, CancellationToken ct) => Task.FromResult(false);
    Task<int> CountPendingForSlotAsync(long projectId, string assignmentType, long? majorId, CancellationToken ct) => Task.FromResult(0);
    Task<SupervisorWorkload> GetWorkloadAsync(long profileId, long semesterId, CancellationToken ct, long? excludeProjectId = null);
    Task<SupervisorRequestModel> CreateAsync(long projectId, long profileId, long actorId,
        string? message, DateTime now, CancellationToken ct);
    Task<SupervisorRequestModel> CreateAsync(long projectId, long profileId, long actorId,
        string? message, DateTime now, CancellationToken ct, string assignmentType, long? majorId, int expiryDays = 7) =>
        CreateAsync(projectId, profileId, actorId, message, now, ct);
    Task<SupervisorRequestModel> RespondAsync(long requestId, string status, string? message, DateTime now, CancellationToken ct);
    Task<long> AssignAndActivateAsync(SupervisorRequestModel request, long actorId, DateTime now, CancellationToken ct);
    Task<IReadOnlyList<SupervisorRequestModel>> GetOtherPendingAsync(long projectId, long requestId, CancellationToken ct);
    Task ExpireOverdueAsync(DateTime now, CancellationToken ct, long? projectId = null, long? requestId = null) => Task.CompletedTask;
}

