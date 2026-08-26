using System;
using System.Collections.Generic;

namespace AIPMS.Application.Features.Projects.DTOs;

public sealed record ProjectTimelineDataDto(
    long ProjectId,
    IReadOnlyList<TimelineMilestoneDto> Milestones);

public sealed record TimelineMilestoneDto(
    long Id,
    string Title,
    string? Description,
    DateOnly? StartDate,
    DateOnly? DueDate,
    string Status,
    int SortOrder,
    double ProgressPercentage,
    IReadOnlyList<TimelineTaskDto> Tasks);

public sealed record TimelineTaskDto(
    long Id,
    long? ParentTaskId,
    string Title,
    string? Description,
    string Status,
    string? Priority,
    DateTime? StartAt,
    DateTime? DueAt,
    DateTime? CompletedAt,
    IReadOnlyList<TimelineTaskAssigneeDto> Assignees,
    IReadOnlyList<TimelineTaskDependencyDto> Dependencies);

public sealed record TimelineTaskAssigneeDto(
    long UserId,
    string FullName);

public sealed record TimelineTaskDependencyDto(
    long DependsOnTaskId,
    string DependencyType);
