using System;
using System.Linq;
using AIPMS.Application.Features.Tasks.DTOs;
using TaskEntity = AIPMS.Infrastructure.Persistence.Generated.Models.Task;
using TaskAssigneeEntity = AIPMS.Infrastructure.Persistence.Generated.Models.TaskAssignee;
using TaskDependencyEntity = AIPMS.Infrastructure.Persistence.Generated.Models.TaskDependency;
using TaskStatusHistoryEntity = AIPMS.Infrastructure.Persistence.Generated.Models.TaskStatusHistory;

namespace AIPMS.Infrastructure.Persistence.Mappers;

internal static class TaskMapper
{
    public static TaskDto ToDto(this TaskEntity task) =>
        new(
            task.Id,
            task.MilestoneId,
            task.ParentTaskId,
            task.Title,
            task.Description,
            task.Status,
            task.Priority,
            task.StartAt,
            task.DueAt,
            task.CompletedAt,
            task.CreatedBy,
            task.CreatedByNavigation?.FullName ?? string.Empty,
            task.CreatedAt,
            task.UpdatedAt,
            task.TaskAssignees.Select(static ta => ta.ToDto()).ToArray(),
            task.TaskDependencyTasks.Select(static td => td.ToDto()).ToArray());

    public static TaskAssigneeDto ToDto(this TaskAssigneeEntity assignee) =>
        new(
            assignee.Id,
            assignee.TaskId,
            assignee.UserId,
            assignee.User?.FullName ?? string.Empty,
            assignee.AssignedBy,
            assignee.AssignedAt);

    public static TaskDependencyDto ToDto(this TaskDependencyEntity dependency) =>
        new(
            dependency.Id,
            dependency.TaskId,
            dependency.DependsOnTaskId,
            dependency.DependencyType,
            dependency.CreatedAt);

    public static TaskStatusHistoryDto ToDto(this TaskStatusHistoryEntity history) =>
        new(
            history.Id,
            history.TaskId,
            history.OldStatus,
            history.NewStatus,
            history.ChangedBy,
            history.ChangedByNavigation?.FullName ?? string.Empty,
            history.Reason,
            history.ChangedAt);
}
