using System;
using System.Linq;
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
            milestone.UpdatedAt,
            milestone.ConcurrencyToken.ToString("N"),
            milestone.MilestoneDependencies?.Select(d => new MilestoneDependencyDto(
                d.Id,
                d.MilestoneId,
                d.DependsOnMilestoneId,
                d.DependencyType,
                d.CreatedAt,
                d.DependsOnMilestone?.Title
            )).ToArray() ?? Array.Empty<MilestoneDependencyDto>());
}
