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
using AIPMS.Infrastructure.Services.Projects;
using Microsoft.EntityFrameworkCore;
using Task = System.Threading.Tasks.Task;
using TaskEntity = AIPMS.Infrastructure.Persistence.Generated.Models.Task;

namespace AIPMS.Infrastructure.Persistence.Repositories;

public sealed class TaskRepository(AipmsDbContext context, TimeProvider? clock = null) : ITaskRepository
{
    public async Task<TaskDto?> GetByIdAsync(long id, CancellationToken cancellationToken)
    {
        var entity = await context.Tasks
            .AsNoTracking()
            .Include(t => t.BoardLabels)
            .Include(static t => t.CreatedByNavigation)
            .Include(static t => t.TaskAssignees)
                .ThenInclude(static ta => ta.User)
            .Include(static t => t.TaskDependencyTasks)
            .FirstOrDefaultAsync(t => t.Id == id && t.DeletedAt == null, cancellationToken);

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
        long? majorId = null,
        CancellationToken cancellationToken = default,
        string? disciplineRole = null)
    {
        var query = context.Tasks
            .AsNoTracking()
            .Include(t => t.BoardLabels)
            .Include(static t => t.CreatedByNavigation)
            .Include(static t => t.TaskAssignees)
                .ThenInclude(static ta => ta.User)
            .Include(static t => t.TaskDependencyTasks)
            .Where(t => t.Milestone.ProjectId == projectId && t.DeletedAt == null);

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

        if (majorId.HasValue || disciplineRole is not null)
        {
            query = query.Where(t => context.TaskDisciplines.Any(td => td.TaskId == t.Id
                && (!majorId.HasValue || td.MajorId == majorId.Value)
                && (disciplineRole == null || td.Role == disciplineRole)));
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

        return new PagedResult<TaskDto>(dtos, page, pageSize, totalCount);
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
        entity.IsUserCustomized = true;
        entity.UpdatedAt = DateTime.UtcNow;
        entity.ConcurrencyToken = Guid.NewGuid();

        await context.SaveChangesAsync(cancellationToken);

        return (await GetByIdAsync(entity.Id, cancellationToken))!;
    }

    public async Task DeleteAsync(long id, CancellationToken cancellationToken)
    {
        var entity = await context.Tasks
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (entity is not null)
        {
            if (await HasHistoricalDataAsync(id, cancellationToken))
                throw new AIPMS.Application.Common.Exceptions.ConflictException(
                    "Task evidence, core blueprint gate tasks, and comments must be retained. Cancel the task instead.",
                    AIPMS.Application.Common.Exceptions.WorkflowErrorCodes.RestrictedDelete);
            context.Tasks.Remove(entity);
            try { await context.SaveChangesAsync(cancellationToken); }
            catch (DbUpdateException ex) when (ex.InnerException is Microsoft.Data.SqlClient.SqlException { Number: 547 })
            {
                throw new AIPMS.Application.Common.Exceptions.ConflictException(
                    "The task has related records and cannot be deleted.",
                    AIPMS.Application.Common.Exceptions.WorkflowErrorCodes.RestrictedDelete);
            }
        }
    }

    public async Task<bool> HasHistoricalDataAsync(long id, CancellationToken cancellationToken)
    {
        if (await context.Tasks.AnyAsync(t => t.Id == id && (t.SprintId != null || t.IsCoreGateTask), cancellationToken)
            || await context.Set<AIPMS.Infrastructure.Persistence.Models.SprintTaskEvent>().AnyAsync(e => e.TaskId == id, cancellationToken)) return true;
        if (await context.TaskComments.AnyAsync(c => c.TaskId == id, cancellationToken)
            || await context.Files.AnyAsync(f => f.TaskId == id, cancellationToken)
            || await context.Set<ProjectEvidence>().AnyAsync(e => e.TaskId == id, cancellationToken)) return true;
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
        var aggregate = await context.Tasks.SingleAsync(t => t.Id == taskId, cancellationToken);
        if (aggregate.Status is "IN_REVIEW" or "DONE" or "CANCELLED")
            throw new AIPMS.Application.Common.Exceptions.ConflictException("Không thể thay đổi phân công của công việc đang chờ nghiệm thu, đã hoàn thành hoặc đã hủy.",
                AIPMS.Application.Common.Exceptions.WorkflowErrorCodes.TaskAssigneesLocked);
        aggregate.ConcurrencyToken = Guid.NewGuid();

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
        var aggregate = await context.Tasks.SingleAsync(t => t.Id == taskId, cancellationToken);
        aggregate.ConcurrencyToken = Guid.NewGuid();

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
        var aggregate = await context.Tasks.SingleAsync(t => t.Id == taskId, cancellationToken);
        aggregate.ConcurrencyToken = Guid.NewGuid();

        var dep = await context.TaskDependencies
            .FirstOrDefaultAsync(d => d.TaskId == taskId && d.DependsOnTaskId == dependsOnTaskId, cancellationToken);
        if (dep is not null)
        {
            context.TaskDependencies.Remove(dep);
        }
        await context.SaveChangesAsync(cancellationToken);
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
        var restoringCompletedTask = oldStatus == "CANCELLED" && newStatus != "CANCELLED"
            && await context.TaskStatusHistories.AnyAsync(h => h.TaskId == taskId && (h.OldStatus == "DONE" || h.NewStatus == "DONE"), cancellationToken);
        if ((oldStatus == "DONE" && newStatus != "DONE" || restoringCompletedTask) && string.IsNullOrWhiteSpace(reason))
            throw new AIPMS.Application.Common.Exceptions.ConflictException("Cần ghi lý do khi mở lại, khôi phục hoặc hủy công việc đã hoàn thành.",
                AIPMS.Application.Common.Exceptions.WorkflowErrorCodes.TaskReopenReasonRequired);
        if (newStatus == "DONE" && await context.TaskDependencyImpacts.AnyAsync(x => x.TaskId == taskId && x.Status == "PENDING", cancellationToken))
            throw new AIPMS.Application.Common.Exceptions.ConflictException("Cần kết luận các ảnh hưởng từ công việc tiền nhiệm trước khi xác nhận hoàn thành.",
                AIPMS.Application.Common.Exceptions.WorkflowErrorCodes.TaskDependencyImpactPending);
        if (oldStatus == newStatus) return;
        var now = (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;
        if (newStatus == "DONE" && oldStatus != "DONE" && task.GateRequirementId.HasValue)
        {
            var hasReadyGateEvidence = await context.GateEvidence.AsNoTracking()
                .AnyAsync(e => e.RequirementId == task.GateRequirementId.Value && e.ScanStatus == AIPMS.Application.Abstractions.Security.FileScanStatuses.Ready, cancellationToken);
            var hasReadyTaskFile = await context.Files.AsNoTracking()
                .AnyAsync(f => f.TaskId == taskId && f.ScanStatus == AIPMS.Application.Abstractions.Security.FileScanStatuses.Ready, cancellationToken);
            var hasLedgerEvidence = await context.Set<ProjectEvidence>().AsNoTracking()
                .AnyAsync(e => e.TaskId == taskId && e.VerificationStatus != "REJECTED", cancellationToken);
            if (!hasReadyGateEvidence && !hasReadyTaskFile && !hasLedgerEvidence)
            {
                throw new AIPMS.Domain.Exceptions.DomainException(
                    "Completing a gate-linked mandatory task requires at least one READY evidence attachment.",
                    AIPMS.Application.Common.Exceptions.WorkflowErrorCodes.EvidenceRequired);
            }
        }
        task.Status = newStatus;
        if (newStatus == "BLOCKED")
        {
            task.BlockedSinceAt = now;
            task.BlockerReason = reason?.Trim();
        }
        else if (oldStatus == "BLOCKED")
        {
            task.BlockedSinceAt = null;
            task.BlockerReason = null;
        }
        if (newStatus == "DONE")
        {
            task.CompletedAt = now;
        }
        else if (oldStatus == "DONE")
        {
            task.CompletedAt = null;
        }
        task.UpdatedAt = now;
        task.ConcurrencyToken = Guid.NewGuid();

        var statusHistory = new TaskStatusHistory
        {
            TaskId = taskId,
            OldStatus = oldStatus,
            NewStatus = newStatus,
            ChangedBy = actorUserId,
            Reason = reason,
            ChangedAt = now
        };
        context.TaskStatusHistories.Add(statusHistory);
        if (oldStatus == "DONE" || restoringCompletedTask)
            await TaskDependencyImpactRecorder.StageAsync(context, task, statusHistory, cancellationToken);

        if (task.SprintId is long sprint && await context.Set<AIPMS.Infrastructure.Persistence.Models.Sprint>().AnyAsync(s => s.Id == sprint && s.Status == "ACTIVE", cancellationToken))
            SprintHistory.Record(context, task, sprint, "STATUS", true, actorUserId, now);

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
        return await AIPMS.Infrastructure.Services.Projects.ProjectExecutionAuthority.IsManagerAsync(context, projectId, userId, cancellationToken);
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

    public async Task<IReadOnlyList<TaskDependencyBlocker>> GetBlockingDependenciesAsync(
        long taskId,
        CancellationToken cancellationToken)
    {
        return await context.TaskDependencies
            .AsNoTracking()
            .Where(d => d.TaskId == taskId
                && d.DependencyType == "FINISH_TO_START"
                && d.DependsOnTask.Status != "DONE"
                && d.DependsOnTask.Status != "COMPLETED"
                && d.DependsOnTask.Status != "CANCELLED")
            .OrderBy(d => d.Id)
            .Select(d => new TaskDependencyBlocker(d.DependsOnTaskId, d.DependsOnTask.Title, d.DependsOnTask.Status))
            .ToListAsync(cancellationToken);
    }

    public async Task<(IReadOnlyList<TaskDto> Overdue, IReadOnlyList<TaskDto> Blocked)> GetOverdueAndBlockedAsync(
        long projectId,
        CancellationToken cancellationToken)
    {
        var tasks = await context.Tasks
            .AsNoTracking()
            .Include(t => t.BoardLabels)
            .Where(t => t.Milestone.ProjectId == projectId && t.DeletedAt == null)
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

    public async Task<bool> MajorBelongsToProjectAsync(
        long majorId,
        long projectId,
        CancellationToken cancellationToken = default)
    {
        var scope = await ProjectAcademicScopeReader.ReadAsync(
            context,
            projectId,
            cancellationToken,
            requireFrozenScope: true);

        return scope.MajorIds.Contains(majorId);
    }

    public async Task<TaskDto> ApplyOneStepCreateExtrasAsync(
        long taskId,
        string? initialStatus,
        long? sprintId,
        int? storyPoints,
        IReadOnlyList<string>? labels,
        long? gateRequirementId,
        IReadOnlyList<string>? initialCriteria,
        long actorUserId,
        CancellationToken cancellationToken)
    {
        var entity = await context.Tasks.Include(t => t.BoardLabels).SingleAsync(t => t.Id == taskId, cancellationToken);
        var now = (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;

        if (!string.IsNullOrWhiteSpace(initialStatus))
            entity.Status = initialStatus.Trim().ToUpperInvariant();
        if (sprintId.HasValue)
        {
            var sprint = await context.Set<AIPMS.Infrastructure.Persistence.Models.Sprint>()
                .AsNoTracking().SingleOrDefaultAsync(s => s.Id == sprintId.Value, cancellationToken)
                ?? throw new AIPMS.Application.Common.Exceptions.NotFoundException("Sprint", sprintId.Value);
            if (sprint.Status == "COMPLETED")
                throw new AIPMS.Application.Common.Exceptions.ConflictException("Cannot add tasks to a completed sprint.");
            entity.SprintId = sprintId.Value;
        }
        if (storyPoints.HasValue) entity.StoryPoints = storyPoints.Value;
        if (gateRequirementId.HasValue)
        {
            entity.GateRequirementId = gateRequirementId.Value;
            entity.IsCoreGateTask = true;
        }
        if (labels is { Count: > 0 })
        {
            foreach (var lbl in labels.Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => l.Trim().ToLowerInvariant()).Distinct().Take(10))
                if (entity.BoardLabels.All(b => b.Label != lbl))
                    entity.BoardLabels.Add(new AIPMS.Infrastructure.Persistence.Models.TaskBoardLabel { TaskId = taskId, Label = lbl });
        }
        if (initialCriteria is { Count: > 0 })
        {
            var order = 1;
            foreach (var text in initialCriteria.Where(c => !string.IsNullOrWhiteSpace(c)))
            {
                context.Set<AIPMS.Infrastructure.Persistence.Models.TaskCriterion>().Add(new AIPMS.Infrastructure.Persistence.Models.TaskCriterion
                {
                    TaskId = taskId,
                    Title = text.Trim(),
                    IsCompleted = false,
                    SortOrder = order++,
                    CreatedBy = actorUserId,
                    CreatedAt = now,
                    UpdatedAt = now
                });
            }
        }
        entity.UpdatedAt = now;
        entity.ConcurrencyToken = Guid.NewGuid();
        await context.SaveChangesAsync(cancellationToken);
        return (await GetByIdAsync(taskId, cancellationToken))!;
    }

    public async Task<long?> GetUserMajorIdAsync(long projectId, long userId, CancellationToken cancellationToken)
    {
        var snapJson = await context.Set<AIPMS.Infrastructure.Persistence.Models.ProjectRegistrationSnapshot>()
            .AsNoTracking().Where(s => s.ProjectId == projectId).OrderByDescending(s => s.Id)
            .Select(s => s.SnapshotJson).FirstOrDefaultAsync(cancellationToken);
        var frozen = snapJson is null ? null : ProjectAcademicScopeReader.ParseEvidence(snapJson)?.Members?.SingleOrDefault(m => m.UserId == userId)?.MajorId;
        if (frozen.HasValue) return frozen;
        return await context.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.MajorId).FirstOrDefaultAsync(cancellationToken);
    }

    public async Task SoftDeleteAsync(long id, long actorUserId, DateTime utcNow, CancellationToken cancellationToken)
    {
        var entity = await context.Tasks.SingleOrDefaultAsync(t => t.Id == id && t.DeletedAt == null, cancellationToken);
        if (entity is null) return;
        if (await HasHistoricalDataAsync(id, cancellationToken))
            throw new AIPMS.Application.Common.Exceptions.ConflictException(
                "Task evidence, core blueprint gate tasks, and comments must be retained. Cancel the task instead.",
                AIPMS.Application.Common.Exceptions.WorkflowErrorCodes.RestrictedDelete);
        entity.DeletedAt = utcNow;
        entity.DeletedBy = actorUserId;
        entity.RestoreUntilAt = utcNow.AddSeconds(5);
        entity.UpdatedAt = utcNow;
        entity.ConcurrencyToken = Guid.NewGuid();
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<TaskDto> RestoreAsync(long id, long actorUserId, DateTime utcNow, CancellationToken cancellationToken)
    {
        var entity = await context.Tasks.SingleOrDefaultAsync(t => t.Id == id && t.DeletedAt != null, cancellationToken)
            ?? throw new AIPMS.Application.Common.Exceptions.NotFoundException("DeletedTask", id);
        if (entity.DeletedBy != actorUserId)
            throw new AIPMS.Application.Common.Exceptions.ForbiddenException("Only the user who deleted this task can undo deletion.");
        if (!entity.RestoreUntilAt.HasValue || utcNow > entity.RestoreUntilAt.Value)
            throw new AIPMS.Application.Common.Exceptions.ConflictException("The 5-second undo window has expired.", "UNDO_WINDOW_EXPIRED");
        entity.DeletedAt = null;
        entity.DeletedBy = null;
        entity.RestoreUntilAt = null;
        entity.UpdatedAt = utcNow;
        entity.ConcurrencyToken = Guid.NewGuid();
        await context.SaveChangesAsync(cancellationToken);
        return (await GetByIdAsync(id, cancellationToken))!;
    }
}
