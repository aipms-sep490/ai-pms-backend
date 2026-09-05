using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Tasks.DTOs;
using Task = System.Threading.Tasks.Task;

namespace AIPMS.Application.Features.Tasks.Abstractions;

public interface ITaskRepository
{
    Task<TaskDto?> GetByIdAsync(long id, CancellationToken cancellationToken);

    Task<PagedResult<TaskDto>> GetTasksAsync(
        long projectId,
        long? milestoneId,
        string? status,
        string? priority,
        long? assigneeUserId,
        string? search,
        DateTime? dueFrom,
        DateTime? dueTo,
        bool? isOverdue,
        bool? isBlocked,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    Task<TaskDto> CreateAsync(
        long milestoneId,
        long? parentTaskId,
        string title,
        string? description,
        string? priority,
        DateTime? startAt,
        DateTime? dueAt,
        IReadOnlyList<long> assigneeUserIds,
        long createdByUserId,
        CancellationToken cancellationToken);

    Task<TaskDto> UpdateAsync(
        long id,
        long milestoneId,
        long? parentTaskId,
        string title,
        string? description,
        string? priority,
        DateTime? startAt,
        DateTime? dueAt,
        CancellationToken cancellationToken);

    Task DeleteAsync(long id, CancellationToken cancellationToken);

    Task<bool> HasHistoricalDataAsync(long id, CancellationToken cancellationToken);

    Task SetAssigneesAsync(
        long taskId,
        IEnumerable<long> userIds,
        long assignedByUserId,
        CancellationToken cancellationToken);

    Task AddDependencyAsync(
        long taskId,
        long dependsOnTaskId,
        string dependencyType,
        CancellationToken cancellationToken);

    Task RemoveDependencyAsync(
        long taskId,
        long dependsOnTaskId,
        CancellationToken cancellationToken);

    Task UpdateStatusAsync(
        long taskId,
        string newStatus,
        string? reason,
        long actorUserId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<TaskStatusHistoryDto>> GetStatusHistoryAsync(
        long taskId,
        CancellationToken cancellationToken);

    Task<bool> IsUserActiveTeamMemberAsync(
        long projectId,
        long userId,
        CancellationToken cancellationToken);

    Task<bool> TaskBelongsToProjectAsync(
        long taskId,
        long projectId,
        CancellationToken cancellationToken);

    Task<bool> MilestoneBelongsToProjectAsync(
        long milestoneId,
        long projectId,
        CancellationToken cancellationToken);

    Task<bool> IsProjectLeaderOrSupervisorAsync(
        long projectId,
        long userId,
        CancellationToken cancellationToken);

    Task<bool> IsTaskAssigneeAsync(
        long taskId,
        long userId,
        CancellationToken cancellationToken);

    Task<long?> GetParentTaskIdAsync(long taskId, CancellationToken cancellationToken);

    Task<IEnumerable<long>> GetDependsOnTaskIdsAsync(long taskId, CancellationToken cancellationToken);

    Task<(IReadOnlyList<TaskDto> Overdue, IReadOnlyList<TaskDto> Blocked)> GetOverdueAndBlockedAsync(
        long projectId,
        CancellationToken cancellationToken);
}
