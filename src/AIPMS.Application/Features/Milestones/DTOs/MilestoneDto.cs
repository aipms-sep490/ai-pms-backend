using System;
using System.Collections.Generic;

namespace AIPMS.Application.Features.Milestones.DTOs;

public sealed record MilestoneDependencyDto(
    long Id,
    long MilestoneId,
    long DependsOnMilestoneId,
    string DependencyType,
    DateTime CreatedAt,
    string? DependsOnTitle = null);

public sealed record MilestoneDto(
    long Id,
    long ProjectId,
    string Title,
    string? Description,
    DateOnly? StartDate,
    DateOnly? DueDate,
    string Status,
    int SortOrder,
    long CreatedBy,
    string CreatedByFullName,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    string? ConcurrencyToken = null,
    IReadOnlyList<MilestoneDependencyDto>? Dependencies = null);
