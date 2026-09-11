using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Evaluations.Models;

namespace AIPMS.Application.Features.Evaluations.Abstractions;

public interface IRubricRepository
{
    Task<RubricActor?> GetActorAsync(long userId, CancellationToken ct);
    Task<RubricScope?> GetScopeAsync(long departmentId, long semesterId, CancellationToken ct);
    Task<PagedResult<RubricRecord>> ListAsync(RubricActor actor, RubricFilter filter, CancellationToken ct);
    Task<RubricRecord?> GetAsync(long id, RubricActor actor, bool forUpdate, CancellationToken ct);
    Task<RubricRecord> CreateAsync(long departmentId, long semesterId, string code, string name,
        string? description, IReadOnlyList<RubricCriterionInput> criteria, long actorId, DateTime now,
        long? sourceId, CancellationToken ct);
    Task<RubricRecord> UpdateAsync(long id, string name, string? description,
        IReadOnlyList<RubricCriterionInput> criteria, DateTime now, CancellationToken ct);
    Task<RubricRecord> SetStatusAsync(long id, string status, DateTime now, CancellationToken ct);
    Task DeleteAsync(long id, CancellationToken ct);
    Task<T> InTransactionAsync<T>(Func<Task<T>> action, CancellationToken ct);
}
