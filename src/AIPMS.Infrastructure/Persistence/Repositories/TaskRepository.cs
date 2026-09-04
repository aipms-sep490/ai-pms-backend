using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Tasks.Abstractions;
using AIPMS.Application.Features.Tasks.DTOs;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Generated.Models;
using AIPMS.Infrastructure.Persistence.Mappers;
using Microsoft.EntityFrameworkCore;
using Task = System.Threading.Tasks.Task;
using TaskEntity = AIPMS.Infrastructure.Persistence.Generated.Models.Task;

namespace AIPMS.Infrastructure.Persistence.Repositories;

public sealed class TaskRepository(AipmsDbContext context) : ITaskRepository
{
    public async Task<TaskDto?> GetByIdAsync(long id, CancellationToken cancellationToken)
    {
        var entity = await context.Tasks
            .AsNoTracking()
            .Include(static t => t.CreatedByNavigation)
            .Include(static t => t.TaskAssignees)
                .ThenInclude(static ta => ta.User)
            .Include(static t => t.TaskDependencyTasks)
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

        return entity?.ToDto();
    }

    public async Task<PagedResult<TaskDto>> GetTasksAsync(
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
        CancellationToken cancellationToken)
    {
        var query = context.Tasks
            .AsNoTracking()
            .Include(static t => t.CreatedByNavigation)
            .Include(static t => t.TaskAssignees)
                .ThenInclude(static ta => ta.User)
            .Include(static t => t.TaskDependencyTasks)
            .Where(t => t.Milestone.ProjectId == projectId);

        if (milestoneId.HasValue)
        {
            query = query.Where(t => t.MilestoneId == milestoneId.Value);
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(t => t.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(priority))
        {
            query = query.Where(t => t.Priority == priority);
        }

        if (assigneeUserId.HasValue)
        {
            query = query.Where(t => t.TaskAssignees.Any(ta => ta.UserId == assigneeUserId.Value));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var searchLower = search.ToLower();
            query = query.Where(t => t.Title.ToLower().Contains(searchLower) 
                                  || (t.Description != null && t.Description.ToLower().Contains(searchLower)));
        }

        if (dueFrom.HasValue)
        {
            query = query.Where(t => t.DueAt >= dueFrom.Value);
        }

        if (dueTo.HasValue)
        {
            query = query.Where(t => t.DueAt <= dueTo.Value);
        }

        if (isOverdue.HasValue)
        {
            var utcNow = DateTime.UtcNow;
            if (isOverdue.Value)
            {
                query = query.Where(t => t.DueAt < utcNow && t.Status != "DONE" && t.Status != "CANCELLED");
            }
            else
            {
                query = query.Where(t => t.DueAt == null || t.DueAt >= utcNow || t.Status == "DONE" || t.Status == "CANCELLED");
            }
        }

        if (isBlocked.HasValue)
        {
            if (isBlocked.Value)
            {
                query = query.Where(t => t.Status == "BLOCKED");
            }
            else
            {
                query = query.Where(t => t.Status != "BLOCKED");
            }
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderBy(static t => t.DueAt == null ? 1 : 0) // Nulls last
            .ThenBy(static t => t.DueAt)
            .ThenBy(static t => t.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var dtos = items.Select(static t => t.ToDto()).ToArray();

        return new PagedResult<TaskDto>(dtos, totalCount, page, pageSize);
    }

    public async Task<TaskDto> CreateAsync(
        long milestoneId,
        long? parentTaskId,
        string title,
        string? description,
        string? priority,
        DateTime? startAt,
        DateTime? dueAt,
        IReadOnlyList<long> assigneeUserIds,
        long createdByUserId,
        CancellationToken cancellationToken)
    {
        var utcNow = DateTime.UtcNow;
        var task = new TaskEntity
        {
            MilestoneId = milestoneId,
            ParentTaskId = parentTaskId,
            Title = title.Trim(),
            Description = description?.Trim(),
            Status = "TODO",
            Priority = priority,
            StartAt = startAt,
            DueAt = dueAt,
            CreatedBy = createdByUserId,
            CreatedAt = utcNow,
            UpdatedAt = utcNow
        };

        if (assigneeUserIds.Count > 0)
        {
            foreach (var userId in assigneeUserIds)
            {
                task.TaskAssignees.Add(new TaskAssignee
                {
                    UserId = userId,
                    AssignedBy = createdByUserId,
                    AssignedAt = utcNow
                });
            }
        }

        context.Tasks.Add(task);
        await context.SaveChangesAsync(cancellationToken);

        return (await GetByIdAsync(task.Id, cancellationToken))!;
    }

    public async Task<TaskDto> UpdateAsync(
        long id,
        long milestoneId,
        long? parentTaskId,
        string title,
        string? description,
        string? priority,
        DateTime? startAt,
        DateTime? dueAt,
        CancellationToken cancellationToken)
    {
        var entity = await context.Tasks
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException($"Task with ID {id} not found.");

        entity.MilestoneId = milestoneId;
        entity.ParentTaskId = parentTaskId;
        entity.Title = title.Trim();
        entity.Description = description?.Trim();
        entity.Priority = priority;
        entity.StartAt = startAt;
        entity.DueAt = dueAt;
        entity.UpdatedAt = DateTime.UtcNow;

        await context.SaveChangesAsync(cancellationToken);

        return (await GetByIdAsync(entity.Id, cancellationToken))!;
    }

    public async Task DeleteAsync(long id, CancellationToken cancellationToken)
    {
        var entity = await context.Tasks
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (entity is not null)
        {
            context.Tasks.Remove(entity);
            await context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<bool> HasHistoricalDataAsync(long id, CancellationToken cancellationToken)
    {
        var hasHistory = await context.TaskStatusHistories.AnyAsync(h => h.TaskId == id, cancellationToken);
        if (hasHistory) return true;

        var hasDependencies = await context.TaskDependencies.AnyAsync(d => d.TaskId == id || d.DependsOnTaskId == id, cancellationToken);
        if (hasDependencies) return true;

        var hasAssignees = await context.TaskAssignees.AnyAsync(a => a.TaskId == id, cancellationToken);
        if (hasAssignees) return true;

        return false;
    }

    public async Task SetAssigneesAsync(
        long taskId,
        IEnumerable<long> userIds,
        long assignedByUserId,
        CancellationToken cancellationToken)
    {
        var existing = await context.TaskAssignees
            .Where(ta => ta.TaskId == taskId)
            .ToListAsync(cancellationToken);

        var newUserIds = userIds.Distinct().ToList();
        var toRemove = existing.Where(ta => !newUserIds.Contains(ta.UserId)).ToList();
        var existingUserIds = existing.Select(ta => ta.UserId).ToList();
        var toAdd = newUserIds.Where(u => !existingUserIds.Contains(u)).ToList();

        context.TaskAssignees.RemoveRange(toRemove);
        var utcNow = DateTime.UtcNow;
        foreach (var userId in toAdd)
        {
            context.TaskAssignees.Add(new TaskAssignee
            {
                TaskId = taskId,
                UserId = userId,
                AssignedBy = assignedByUserId,
                AssignedAt = utcNow
            });
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task AddDependencyAsync(
        long taskId,
        long dependsOnTaskId,
        string dependencyType,
        CancellationToken cancellationToken)
    {
        context.TaskDependencies.Add(new TaskDependency
        {
            TaskId = taskId,
            DependsOnTaskId = dependsOnTaskId,
            DependencyType = dependencyType,
            CreatedAt = DateTime.UtcNow
        });

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveDependencyAsync(
        long taskId,
        long dependsOnTaskId,
        CancellationToken cancellationToken)
    {
        var dep = await context.TaskDependencies
            .FirstOrDefaultAsync(d => d.TaskId == taskId && d.DependsOnTaskId == dependsOnTaskId, cancellationToken);
        if (dep is not null)
        {
            context.TaskDependencies.Remove(dep);
            await context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task UpdateStatusAsync(
        long taskId,
        string newStatus,
        string? reason,
        long actorUserId,
        CancellationToken cancellationToken)
    {
        var task = await context.Tasks
            .FirstOrDefaultAsync(t => t.Id == taskId, cancellationToken)
            ?? throw new KeyNotFoundException($"Task with ID {taskId} not found.");

        var oldStatus = task.Status;
        task.Status = newStatus;
        if (newStatus == "DONE")
        {
            task.CompletedAt = DateTime.UtcNow;
        }
        else if (oldStatus == "DONE")
        {
            task.CompletedAt = null;
        }
        task.UpdatedAt = DateTime.UtcNow;

        context.TaskStatusHistories.Add(new TaskStatusHistory
        {
            TaskId = taskId,
            OldStatus = oldStatus,
            NewStatus = newStatus,
            ChangedBy = actorUserId,
            Reason = reason,
            ChangedAt = DateTime.UtcNow
        });

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<TaskStatusHistoryDto>> GetStatusHistoryAsync(
        long taskId,
        CancellationToken cancellationToken)
    {
        var history = await context.TaskStatusHistories
            .AsNoTracking()
            .Include(static h => h.ChangedByNavigation)
            .Where(h => h.TaskId == taskId)
            .OrderByDescending(static h => h.ChangedAt)
            .ThenByDescending(static h => h.Id)
            .ToListAsync(cancellationToken);

        return history.Select(static h => h.ToDto()).ToArray();
    }

    public async Task<bool> IsUserActiveTeamMemberAsync(
        long projectId,
        long userId,
        CancellationToken cancellationToken)
    {
        return await context.Projects
            .AsNoTracking()
            .Where(p => p.Id == projectId)
            .SelectMany(p => p.Team.TeamMembers)
            .AnyAsync(m => m.UserId == userId && m.LeftAt == null, cancellationToken);
    }

    public async Task<bool> TaskBelongsToProjectAsync(
        long taskId,
        long projectId,
        CancellationToken cancellationToken)
    {
        return await context.Tasks
            .AsNoTracking()
            .AnyAsync(t => t.Id == taskId && t.Milestone.ProjectId == projectId, cancellationToken);
    }

    public async Task<bool> MilestoneBelongsToProjectAsync(
        long milestoneId,
        long projectId,
        CancellationToken cancellationToken)
    {
        return await context.Milestones
            .AsNoTracking()
            .AnyAsync(m => m.Id == milestoneId && m.ProjectId == projectId, cancellationToken);
    }

    public async Task<bool> IsProjectLeaderOrSupervisorAsync(
        long projectId,
        long userId,
        CancellationToken cancellationToken)
    {
        var isLeader = await context.Projects
            .AsNoTracking()
            .Where(p => p.Id == projectId)
            .SelectMany(p => p.Team.TeamMembers)
            .AnyAsync(m => m.UserId == userId && m.IsLeader == true && m.LeftAt == null, cancellationToken);

        if (isLeader) return true;

        var isSupervisor = await context.SupervisorAssignments
            .AsNoTracking()
            .AnyAsync(sa => sa.ProjectId == projectId 
                         && sa.SupervisorProfile.UserId == userId 
                         && sa.EndedAt == null, cancellationToken);

        return isSupervisor;
    }

    public async Task<bool> IsTaskAssigneeAsync(
        long taskId,
        long userId,
        CancellationToken cancellationToken)
    {
        return await context.TaskAssignees
            .AsNoTracking()
            .AnyAsync(ta => ta.TaskId == taskId && ta.UserId == userId, cancellationToken);
    }

    public async Task<long?> GetParentTaskIdAsync(long taskId, CancellationToken cancellationToken)
    {
        var task = await context.Tasks
            .AsNoTracking()
            .Select(static t => new { t.Id, t.ParentTaskId })
            .FirstOrDefaultAsync(t => t.Id == taskId, cancellationToken);
        return task?.ParentTaskId;
    }

    public async Task<IEnumerable<long>> GetDependsOnTaskIdsAsync(long taskId, CancellationToken cancellationToken)
    {
        var ids = await context.TaskDependencies
            .AsNoTracking()
            .Where(d => d.TaskId == taskId)
            .Select(static d => d.DependsOnTaskId)
            .ToListAsync(cancellationToken);
        return ids;
    }

    public async Task<(IReadOnlyList<TaskDto> Overdue, IReadOnlyList<TaskDto> Blocked)> GetOverdueAndBlockedAsync(
        long projectId,
        CancellationToken cancellationToken)
    {
        var tasks = await context.Tasks
            .AsNoTracking()
            .Where(t => t.Milestone.ProjectId == projectId)
            .Include(static t => t.CreatedByNavigation)
            .Include(static t => t.TaskAssignees)
                .ThenInclude(static ta => ta.User)
            .Include(static t => t.TaskDependencyTasks)
            .ToListAsync(cancellationToken);

        var utcNow = DateTime.UtcNow;

        var overdue = tasks
            .Where(t => t.DueAt < utcNow && t.Status != "DONE" && t.Status != "CANCELLED")
            .Select(static t => t.ToDto())
            .ToList();

        var blocked = tasks
            .Where(static t => t.Status == "BLOCKED")
            .Select(static t => t.ToDto())
            .ToList();

        return (overdue, blocked);
    }
}
