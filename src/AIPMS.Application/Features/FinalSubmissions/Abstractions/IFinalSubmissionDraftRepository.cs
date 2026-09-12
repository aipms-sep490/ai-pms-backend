using AIPMS.Application.Features.FinalSubmissions.Models;
using AIPMS.Application.Common.Models;

namespace AIPMS.Application.Features.FinalSubmissions.Abstractions;

public interface IFinalSubmissionDraftRepository
{
    Task<T> InTransactionAsync<T>(Func<Task<T>> action, CancellationToken ct);
    Task LockProjectAsync(long projectId, CancellationToken ct);
    Task<FinalDraftProject?> GetProjectAsync(long projectId, long actorId, CancellationToken ct);
    Task<FinalDraftPeriod?> GetPeriodAsync(long periodId, DateTime now, CancellationToken ct);
    Task<PagedResult<FinalDraftPeriod>> GetPeriodsAsync(long semesterId, DateTime now, int page, int pageSize, CancellationToken ct);
    Task<int> CountOpenPeriodsAsync(long semesterId, DateTime now, CancellationToken ct);
    Task<FinalDraftRecord?> GetAsync(long projectId, CancellationToken ct);
    Task<IReadOnlyList<FinalDraftVersion>> GetVersionsAsync(long projectId, IReadOnlyList<long> ids, CancellationToken ct);
    Task<FinalDraftRecord> SaveAsync(long projectId, long periodId, string? notes,
        IReadOnlyList<long> versionIds, long actorId, DateTime now, CancellationToken ct);
}
