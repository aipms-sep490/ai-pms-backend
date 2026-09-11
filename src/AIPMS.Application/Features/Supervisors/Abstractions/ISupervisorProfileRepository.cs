using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Supervisors.Models;

namespace AIPMS.Application.Features.Supervisors.Abstractions;

public interface ISupervisorProfileRepository
{
    Task<SupervisorAccount?> GetAccountAsync(long userId, CancellationToken ct);
    Task<PagedResult<SupervisorProfileModel>> SearchAsync(SupervisorSearch search, CancellationToken ct);
    Task<SupervisorProfileModel?> GetAsync(long profileId, CancellationToken ct);
    Task<SupervisorProfileModel?> GetByUserAsync(long userId, CancellationToken ct);
    Task<SupervisorProfileModel> UpsertAsync(long userId, string? bio, bool available, DateTime now, CancellationToken ct);
    Task<SupervisorProfileModel> ReplaceExpertiseAsync(long profileId,
        IReadOnlyList<SupervisorExpertiseModel> expertise, DateTime now, CancellationToken ct);
    Task<T> InTransactionAsync<T>(Func<Task<T>> action, CancellationToken ct);
}
