using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Features.Milestones.Abstractions;
using AIPMS.Application.Features.Milestones.DTOs;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Generated.Models;
using AIPMS.Infrastructure.Persistence.Mappers;
using Microsoft.EntityFrameworkCore;
using Task = System.Threading.Tasks.Task;

namespace AIPMS.Infrastructure.Persistence.Repositories;

public sealed class MilestoneRepository(AipmsDbContext context) : IMilestoneRepository
{
    public async Task<MilestoneDto?> GetByIdAsync(long id, CancellationToken cancellationToken)
    {
        var entity = await context.Milestones
            .AsNoTracking()
            .Include(static m => m.CreatedByNavigation)
            .FirstOrDefaultAsync(m => m.Id == id, cancellationToken);

        return entity?.ToDto();
    }

    public async Task<IReadOnlyList<MilestoneDto>> GetProjectMilestonesAsync(long projectId, CancellationToken cancellationToken)
    {
        var entities = await context.Milestones
            .AsNoTracking()
            .Include(static m => m.CreatedByNavigation)
            .Where(m => m.ProjectId == projectId)
            .OrderBy(static m => m.SortOrder)
            .ThenBy(static m => m.Id)
            .ToListAsync(cancellationToken);

        return entities.Select(static m => m.ToDto()).ToArray();
    }

    public async Task<MilestoneDto> CreateAsync(
        long projectId,
        string title,
        string? description,
        DateOnly? startDate,
        DateOnly? dueDate,
        int sortOrder,
        long createdByUserId,
        CancellationToken cancellationToken)
    {
        var utcNow = DateTime.UtcNow;
        var milestone = new Milestone
        {
            ProjectId = projectId,
            Title = title.Trim(),
            Description = description?.Trim(),
            StartDate = startDate,
            DueDate = dueDate,
            Status = "PLANNED",
            SortOrder = sortOrder,
            CreatedBy = createdByUserId,
            CreatedAt = utcNow,
            UpdatedAt = utcNow
        };

        context.Milestones.Add(milestone);
        await context.SaveChangesAsync(cancellationToken);

        return (await GetByIdAsync(milestone.Id, cancellationToken))!;
    }

    public async Task<MilestoneDto> UpdateAsync(
        long id,
        string title,
        string? description,
        DateOnly? startDate,
        DateOnly? dueDate,
        string status,
        int sortOrder,
        CancellationToken cancellationToken)
    {
        var entity = await context.Milestones
            .FirstOrDefaultAsync(m => m.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException($"Milestone with ID {id} not found.");

        entity.Title = title.Trim();
        entity.Description = description?.Trim();
        entity.StartDate = startDate;
        entity.DueDate = dueDate;
        entity.Status = status;
        entity.SortOrder = sortOrder;
        entity.UpdatedAt = DateTime.UtcNow;

        await context.SaveChangesAsync(cancellationToken);

        return (await GetByIdAsync(entity.Id, cancellationToken))!;
    }

    public async Task DeleteAsync(long id, CancellationToken cancellationToken)
    {
        var entity = await context.Milestones
            .FirstOrDefaultAsync(m => m.Id == id, cancellationToken);
        if (entity is not null)
        {
            context.Milestones.Remove(entity);
            await context.SaveChangesAsync(cancellationToken);
        }
    }

    public Task<bool> HasTasksAsync(long id, CancellationToken cancellationToken) =>
        context.Tasks.AnyAsync(t => t.MilestoneId == id, cancellationToken);

    public async Task ReorderAsync(IEnumerable<(long MilestoneId, int SortOrder)> reorderItems, CancellationToken cancellationToken)
    {
        var ids = reorderItems.Select(static r => r.MilestoneId).ToList();
        var entities = await context.Milestones
            .Where(m => ids.Contains(m.Id))
            .ToListAsync(cancellationToken);

        var dict = reorderItems.ToDictionary(static r => r.MilestoneId, static r => r.SortOrder);
        foreach (var entity in entities)
        {
            if (dict.TryGetValue(entity.Id, out var newSortOrder))
            {
                entity.SortOrder = newSortOrder;
                entity.UpdatedAt = DateTime.UtcNow;
            }
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> IsProjectLeaderOrSupervisorAsync(long projectId, long userId, CancellationToken cancellationToken)
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

    public async Task<IReadOnlyList<MilestoneProgressDto>> GetMilestoneProgressAsync(long projectId, CancellationToken cancellationToken)
    {
        var milestones = await context.Milestones
            .AsNoTracking()
            .Where(m => m.ProjectId == projectId)
            .OrderBy(static m => m.SortOrder)
            .ThenBy(static m => m.Id)
            .Select(static m => new
            {
                m.Id,
                m.Title,
                Tasks = m.Tasks.Select(static t => new { t.Id, t.Status })
            })
            .ToListAsync(cancellationToken);

        var result = new List<MilestoneProgressDto>();
        foreach (var milestone in milestones)
        {
            var totalTasks = milestone.Tasks.Count();
            var doneTasks = milestone.Tasks.Count(static t => t.Status == "DONE");
            var progressPercentage = totalTasks == 0
                ? 0.0
                : Math.Round((doneTasks * 100.0) / totalTasks, 2);

            result.Add(new MilestoneProgressDto(
                milestone.Id,
                milestone.Title,
                totalTasks,
                doneTasks,
                progressPercentage));
        }

        return result;
    }
}
