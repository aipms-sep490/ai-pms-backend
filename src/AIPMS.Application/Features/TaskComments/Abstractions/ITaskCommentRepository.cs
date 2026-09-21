using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.TaskComments.DTOs;
using AIPMS.Application.Features.TaskComments.Models;
namespace AIPMS.Application.Features.TaskComments.Abstractions;
public interface ITaskCommentRepository
{
    Task<T> InTransactionAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct);
    Task<TaskCommentAccess?> GetAccessAsync(long taskId, long actorId, CancellationToken ct);
    Task<PagedResult<TaskCommentDto>> ListAsync(TaskCommentSearch search, CancellationToken ct);
    Task<TaskCommentDto> CreateAsync(long taskId, long actorId, string content, DateTime now, CancellationToken ct);
}
