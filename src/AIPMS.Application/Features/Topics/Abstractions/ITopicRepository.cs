using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Topics.DTOs;
using AIPMS.Application.Features.Topics.Models;

namespace AIPMS.Application.Features.Topics.Abstractions;

public interface ITopicRepository
{
    Task<TopicActor?> GetActorAsync(long userId, IReadOnlyCollection<string> tokenRoles, CancellationToken ct);
    Task<TopicPeriod?> GetPeriodAsync(long periodId, CancellationToken ct);
    Task<IReadOnlyList<TopicMajor>> GetMajorsAsync(IReadOnlyList<long> ids, CancellationToken ct);
    Task<bool> IsActiveDepartmentAsync(long departmentId, long organizationId, CancellationToken ct);
    Task<TopicDto?> GetAsync(long id, TopicActor actor, bool forUpdate, CancellationToken ct);
    Task<TopicDto?> GetByIdForSelectionAsync(long id, TopicActor actor, CancellationToken ct);
    Task<PagedResult<TopicDto>> ListAsync(TopicActor actor, TopicFilter filter, CancellationToken ct);
    Task<TopicDto> CreateAsync(CreateTopicRequest input, TopicActor actor, DateTime now, CancellationToken ct);
    Task<TopicDto> UpdateAsync(long id, TopicContentRequest content, TopicActor actor, DateTime now, CancellationToken ct);
    Task<TopicDto> SetStatusAsync(long id, bool publish, string? reason, TopicActor actor, DateTime now, CancellationToken ct);
    Task<T> InTransactionAsync<T>(Func<Task<T>> action, CancellationToken ct);
}
