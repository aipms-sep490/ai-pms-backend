using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Teams.Models;

namespace AIPMS.Application.Features.Teams.Abstractions;

public interface ITeamLeaderChangeRequestRepository
{
    Task<T> InTransactionAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct);
    Task LockTeamAsync(long teamId, CancellationToken ct);
    Task LockRequestAsync(long requestId, CancellationToken ct);
    Task<TeamLeaderChangeContext?> GetContextAsync(long teamId, long newLeaderUserId, CancellationToken ct);
    Task<bool> HasPendingAsync(long teamId, CancellationToken ct);
    Task<TeamLeaderChangeRequestModel> CreateAsync(long teamId, long projectId,
        long requestedBy, long currentLeaderUserId, long newLeaderUserId, long mentorProfileId,
        string? message, DateTime now, CancellationToken ct);
    Task<TeamLeaderChangeRequestModel?> GetAsync(long requestId, CancellationToken ct);
    Task<TeamLeaderChangeRequestModel> RespondAsync(long requestId, string status,
        string? message, DateTime now, CancellationToken ct);
    Task<PagedResult<TeamLeaderChangeRequestModel>> SearchAsync(
        TeamLeaderChangeRequestSearch search, CancellationToken ct);
    Task<bool> HasActiveMentorAsync(long projectId, long mentorProfileId, CancellationToken ct);
}
