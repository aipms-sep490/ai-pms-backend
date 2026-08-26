using System;

namespace AIPMS.Application.Features.Milestones.DTOs;

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
    DateTime UpdatedAt);
