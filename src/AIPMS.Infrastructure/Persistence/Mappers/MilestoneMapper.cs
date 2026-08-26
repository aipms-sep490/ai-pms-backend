using System;
using AIPMS.Application.Features.Milestones.DTOs;
using MilestoneEntity = AIPMS.Infrastructure.Persistence.Generated.Models.Milestone;

namespace AIPMS.Infrastructure.Persistence.Mappers;

internal static class MilestoneMapper
{
    public static MilestoneDto ToDto(this MilestoneEntity milestone) =>
        new(
            milestone.Id,
            milestone.ProjectId,
            milestone.Title,
            milestone.Description,
            milestone.StartDate,
            milestone.DueDate,
            milestone.Status,
            milestone.SortOrder,
            milestone.CreatedBy,
            milestone.CreatedByNavigation?.FullName ?? string.Empty,
            milestone.CreatedAt,
            milestone.UpdatedAt);
}
