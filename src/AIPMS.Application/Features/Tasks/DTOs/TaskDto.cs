using System;
using System.Collections.Generic;

namespace AIPMS.Application.Features.Tasks.DTOs;

public sealed record TaskDto(
    long Id,
    long MilestoneId,
    long? ParentTaskId,
    string Title,
    string? Description,
    string Status,
    string? Priority,
    DateTime? StartAt,
    DateTime? DueAt,
    DateTime? CompletedAt,
    long CreatedBy,
    string CreatedByFullName,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    IReadOnlyList<TaskAssigneeDto> Assignees,
    IReadOnlyList<TaskDependencyDto> Dependencies);

public sealed record TaskAssigneeDto(
    long Id,
    long TaskId,
    long UserId,
    string UserFullName,
    long AssignedBy,
    DateTime AssignedAt);

public sealed record TaskDependencyDto(
    long Id,
    long TaskId,
    long DependsOnTaskId,
    string DependencyType,
    DateTime CreatedAt);

public sealed record TaskStatusHistoryDto(
    long Id,
    long TaskId,
    string? OldStatus,
    string NewStatus,
    long ChangedBy,
    string ChangedByFullName,
    string? Reason,
    DateTime ChangedAt);
