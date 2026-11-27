using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Common.Exceptions;
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
            .Include(m => m.MilestoneDependencies)
                .ThenInclude(d => d.DependsOnMilestone)
            .FirstOrDefaultAsync(m => m.Id == id, cancellationToken);

        return entity?.ToDto();
    }

    public async Task<IReadOnlyList<MilestoneDto>> GetProjectMilestonesAsync(long projectId, CancellationToken cancellationToken)
    {
        var entities = await context.Milestones
            .AsNoTracking()
            .Include(static m => m.CreatedByNavigation)
            .Include(m => m.MilestoneDependencies)
                .ThenInclude(d => d.DependsOnMilestone)
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
        if (!string.IsNullOrWhiteSpace(entity.TemplateOriginId))
            entity.IsUserCustomized = true;
        entity.UpdatedAt = DateTime.UtcNow;
        entity.ConcurrencyToken = Guid.NewGuid();

        await context.SaveChangesAsync(cancellationToken);

        return (await GetByIdAsync(entity.Id, cancellationToken))!;
    }

    public async Task DeleteAsync(long id, CancellationToken cancellationToken)
    {
        var entity = await context.Milestones
            .FirstOrDefaultAsync(m => m.Id == id, cancellationToken);
        if (entity is not null)
        {
            if (await context.MilestoneDependencies.AnyAsync(d => d.DependsOnMilestoneId == id, cancellationToken))
                throw new ConflictException("Mốc đang được mốc khác sử dụng làm tiền nhiệm. Hãy gỡ phụ thuộc trước khi xóa.");
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
                entity.ConcurrencyToken = Guid.NewGuid();
            }
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> IsProjectLeaderOrSupervisorAsync(long projectId, long userId, CancellationToken cancellationToken)
    {
        return await AIPMS.Infrastructure.Services.Projects.ProjectExecutionAuthority.IsManagerAsync(context, projectId, userId, cancellationToken);
    }

    public Task<bool> MilestoneBelongsToProjectAsync(long milestoneId, long projectId, CancellationToken cancellationToken) =>
        context.Milestones.AsNoTracking().AnyAsync(m => m.Id == milestoneId && m.ProjectId == projectId, cancellationToken);

    public async Task AcquireProjectDependencyLockAsync(long projectId, CancellationToken cancellationToken)
    {
        if (!context.Database.IsRelational()) return;

        var lockKey = $"milestone-dependency:{projectId}";
        var rcParam = new Microsoft.Data.SqlClient.SqlParameter("@rc", System.Data.SqlDbType.Int) { Direction = System.Data.ParameterDirection.Output };
        var resourceParam = new Microsoft.Data.SqlClient.SqlParameter("@p0", System.Data.SqlDbType.NVarChar, 255) { Value = lockKey };
        if (context.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Milestone dependency changes require an active transaction.");
        await context.Database.ExecuteSqlRawAsync(
                "EXEC @rc = sp_getapplock @Resource = @p0, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 10000",
                new object[] { rcParam, resourceParam },
                cancellationToken);

        if (rcParam.Value is not int rcValue || rcValue < 0)
            throw new ConflictException("Không thể khóa dữ liệu phụ thuộc của mốc. Vui lòng thử lại.");
    }

    public async Task AddDependencyAsync(long milestoneId, long dependsOnMilestoneId, string dependencyType, CancellationToken cancellationToken)
    {
        var dependency = new MilestoneDependency
        {
            MilestoneId = milestoneId,
            DependsOnMilestoneId = dependsOnMilestoneId,
            DependencyType = dependencyType,
            CreatedAt = DateTime.UtcNow
        };

        context.MilestoneDependencies.Add(dependency);
        var milestone = await context.Milestones.SingleAsync(m => m.Id == milestoneId, cancellationToken);
        milestone.ConcurrencyToken = Guid.NewGuid();
        milestone.UpdatedAt = DateTime.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveDependencyAsync(long milestoneId, long dependsOnMilestoneId, CancellationToken cancellationToken)
    {
        var dependency = await context.MilestoneDependencies
            .FirstOrDefaultAsync(d => d.MilestoneId == milestoneId && d.DependsOnMilestoneId == dependsOnMilestoneId, cancellationToken);

        if (dependency is not null)
        {
            context.MilestoneDependencies.Remove(dependency);
            var milestone = await context.Milestones.SingleAsync(m => m.Id == milestoneId, cancellationToken);
            milestone.ConcurrencyToken = Guid.NewGuid();
            milestone.UpdatedAt = DateTime.UtcNow;
            await context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<IReadOnlyList<long>> GetDependsOnMilestoneIdsAsync(long milestoneId, CancellationToken cancellationToken) =>
        await context.MilestoneDependencies
            .AsNoTracking()
            .Where(d => d.MilestoneId == milestoneId)
            .Select(d => d.DependsOnMilestoneId)
            .ToListAsync(cancellationToken);

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
