using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.ActionItems.Abstractions;
using AIPMS.Application.Features.ActionItems.DTOs;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Persistence.Repositories;

public sealed class ProjectActionItemRepository(AipmsDbContext context) : IProjectActionItemRepository
{
    public async Task<ProjectActionItemDto?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        var entity = await context.ProjectActionItems
            .AsNoTracking()
            .Include(x => x.Owner)
            .Include(x => x.Task)
            .Include(x => x.Milestone)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        return entity is null ? null : ToDto(entity);
    }

    public async Task<PagedResult<ProjectActionItemDto>> ListAsync(
        long projectId,
        string? sourceType,
        long? meetingId,
        long? progressReportId,
        string? status,
        long? ownerId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = context.ProjectActionItems
            .AsNoTracking()
            .Include(x => x.Owner)
            .Include(x => x.Task)
            .Include(x => x.Milestone)
            .Where(x => x.ProjectId == projectId);

        if (!string.IsNullOrWhiteSpace(sourceType))
        {
            query = query.Where(x => x.SourceType == sourceType.Trim().ToUpperInvariant());
        }

        if (meetingId.HasValue)
        {
            query = query.Where(x => x.MeetingId == meetingId.Value);
        }

        if (progressReportId.HasValue)
        {
            query = query.Where(x => x.ProgressReportId == progressReportId.Value);
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(x => x.Status == status.Trim().ToUpperInvariant());
        }

        if (ownerId.HasValue)
        {
            query = query.Where(x => x.OwnerId == ownerId.Value);
        }

        var total = await query.LongCountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<ProjectActionItemDto>(items.Select(ToDto).ToList(), page, pageSize, total);
    }

    public async Task<ProjectActionItemDto> CreateAsync(
        long projectId,
        string sourceType,
        long? meetingId,
        long? progressReportId,
        string title,
        string? description,
        long? ownerId,
        long? taskId,
        long? milestoneId,
        DateTime? dueAt,
        long createdBy,
        DateTime now,
        Func<ProjectActionItemDto, Task>? onCreated = null,
        CancellationToken cancellationToken = default)
    {
        await using var tx = context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(cancellationToken)
            : null;
        try
        {
            if (string.IsNullOrWhiteSpace(sourceType))
            {
                throw new ValidationException(new System.Collections.Generic.Dictionary<string, string[]>
                {
                    ["sourceType"] = ["SourceType is required."]
                });
            }

            var normalizedSourceType = sourceType.Trim().ToUpperInvariant();

            if (normalizedSourceType == "MEETING" || meetingId.HasValue)
            {
                if (!meetingId.HasValue)
                    throw new ValidationException(new System.Collections.Generic.Dictionary<string, string[]> { ["meetingId"] = ["MeetingId is required for MEETING action items."] });

                var meeting = await context.Meetings
                    .FromSqlInterpolated($"SELECT * FROM dbo.meetings WITH (UPDLOCK, ROWLOCK) WHERE id = {meetingId.Value}")
                    .FirstOrDefaultAsync(cancellationToken)
                    ?? throw new NotFoundException("Meeting", meetingId.Value);

                if (meeting.ProjectId != projectId)
                    throw new NotFoundException("Meeting", meetingId.Value);

                if (meeting.Status == "CANCELLED")
                    throw new ConflictException("Cannot create action items for a cancelled meeting.");
            }

            var entity = new ProjectActionItem
            {
                ProjectId = projectId,
                SourceType = normalizedSourceType,
                MeetingId = meetingId,
                ProgressReportId = progressReportId,
                Title = title,
                Description = description,
                OwnerId = ownerId,
                TaskId = taskId,
                MilestoneId = milestoneId,
                DueAt = dueAt,
                Status = "TODO",
                ConcurrencyToken = Guid.NewGuid(),
                CreatedBy = createdBy,
                CreatedAt = now,
                UpdatedAt = now
            };

            context.ProjectActionItems.Add(entity);
            await context.SaveChangesAsync(cancellationToken);

            var dto = (await GetByIdAsync(entity.Id, cancellationToken))!;
            if (onCreated != null)
            {
                await onCreated(dto);
            }
            if (tx != null) await tx.CommitAsync(cancellationToken);

            return dto;
        }
        catch
        {
            if (tx != null) await tx.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<ProjectActionItemDto> UpdateDetailsAsync(
        long id,
        string title,
        string? description,
        long? ownerId,
        long? taskId,
        long? milestoneId,
        DateTime? dueAt,
        Guid? expectedToken,
        DateTime now,
        Func<ProjectActionItemDto, Task>? onUpdated = null,
        CancellationToken cancellationToken = default)
    {
        await using var tx = context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(cancellationToken)
            : null;
        try
        {
            var entity = await context.ProjectActionItems
                .FromSqlInterpolated($"SELECT * FROM dbo.project_action_items WITH (UPDLOCK, HOLDLOCK) WHERE id = {id}")
                .FirstOrDefaultAsync(cancellationToken);

            if (entity is null)
                throw new NotFoundException("ProjectActionItem", id);

            if (entity.MeetingId.HasValue && await IsMeetingCancelledAsync(entity.MeetingId.Value, cancellationToken))
                throw new ConflictException("Cannot modify action items associated with a cancelled meeting.");

            if (expectedToken.HasValue && entity.ConcurrencyToken != expectedToken.Value)
                throw new ConflictException("The action item has been modified by another user. Please refresh and try again.", WorkflowErrorCodes.StaleConcurrencyToken);

            entity.Title = title;
            entity.Description = description;
            entity.OwnerId = ownerId;
            entity.TaskId = taskId;
            entity.MilestoneId = milestoneId;
            entity.DueAt = dueAt;
            entity.UpdatedAt = now;
            entity.ConcurrencyToken = Guid.NewGuid();

            await context.SaveChangesAsync(cancellationToken);

            var dto = (await GetByIdAsync(entity.Id, cancellationToken))!;
            if (onUpdated != null)
            {
                await onUpdated(dto);
            }
            if (tx != null) await tx.CommitAsync(cancellationToken);

            return dto;
        }
        catch
        {
            if (tx != null) await tx.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<ProjectActionItemDto> UpdateStatusAsync(
        long id,
        string newStatus,
        Guid? expectedToken,
        DateTime now,
        Func<ProjectActionItemDto, Task>? onUpdated = null,
        CancellationToken cancellationToken = default)
    {
        await using var tx = context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(cancellationToken)
            : null;
        try
        {
            var entity = await context.ProjectActionItems
                .FromSqlInterpolated($"SELECT * FROM dbo.project_action_items WITH (UPDLOCK, HOLDLOCK) WHERE id = {id}")
                .FirstOrDefaultAsync(cancellationToken);

            if (entity is null)
                throw new NotFoundException("ProjectActionItem", id);

            if (entity.MeetingId.HasValue && await IsMeetingCancelledAsync(entity.MeetingId.Value, cancellationToken))
                throw new ConflictException("Cannot modify action items associated with a cancelled meeting.");

            if (expectedToken.HasValue && entity.ConcurrencyToken != expectedToken.Value)
                throw new ConflictException("The action item has been modified by another user. Please refresh and try again.", WorkflowErrorCodes.StaleConcurrencyToken);

            entity.Status = newStatus.Trim().ToUpperInvariant();
            entity.UpdatedAt = now;
            entity.ConcurrencyToken = Guid.NewGuid();

            await context.SaveChangesAsync(cancellationToken);

            var dto = (await GetByIdAsync(entity.Id, cancellationToken))!;
            if (onUpdated != null)
            {
                await onUpdated(dto);
            }
            if (tx != null) await tx.CommitAsync(cancellationToken);

            return dto;
        }
        catch
        {
            if (tx != null) await tx.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<bool> IsMeetingInProjectAsync(long meetingId, long projectId, CancellationToken cancellationToken = default)
    {
        return await context.Meetings
            .AsNoTracking()
            .AnyAsync(m => m.Id == meetingId && m.ProjectId == projectId, cancellationToken);
    }

    public async Task<bool> IsMeetingCancelledAsync(long meetingId, CancellationToken cancellationToken = default)
    {
        return await context.Meetings
            .AsNoTracking()
            .AnyAsync(m => m.Id == meetingId && m.Status == "CANCELLED", cancellationToken);
    }

    public async Task<bool> IsProgressReportInProjectAsync(long reportId, long projectId, CancellationToken cancellationToken = default)
    {
        return await context.ProgressReports
            .AsNoTracking()
            .AnyAsync(r => r.Id == reportId && r.ProjectId == projectId, cancellationToken);
    }

    public async Task<bool> IsTaskInProjectAsync(long taskId, long projectId, CancellationToken cancellationToken = default)
    {
        return await context.Tasks
            .AsNoTracking()
            .AnyAsync(t => t.Id == taskId && t.Milestone.ProjectId == projectId, cancellationToken);
    }

    public async Task<bool> IsMilestoneInProjectAsync(long milestoneId, long projectId, CancellationToken cancellationToken = default)
    {
        return await context.Milestones
            .AsNoTracking()
            .AnyAsync(m => m.Id == milestoneId && m.ProjectId == projectId, cancellationToken);
    }

    public async Task<bool> IsEligibleOwnerAsync(long projectId, long userId, CancellationToken cancellationToken = default)
    {
        return await context.Projects
            .AsNoTracking()
            .Where(p => p.Id == projectId)
            .AnyAsync(p => p.Team.TeamMembers.Any(m => m.UserId == userId && m.LeftAt == null)
                || p.SupervisorAssignments.Any(a => a.EndedAt == null && a.SupervisorProfile.UserId == userId),
                cancellationToken);
    }

    public async Task<bool> IsMeetingParticipantAsync(long meetingId, long userId, CancellationToken cancellationToken = default)
    {
        return await context.MeetingParticipants
            .AsNoTracking()
            .AnyAsync(mp => mp.MeetingId == meetingId && mp.UserId == userId, cancellationToken);
    }

    public async Task<bool> IsMeetingCreatorAsync(long meetingId, long userId, CancellationToken cancellationToken = default)
    {
        return await context.Meetings
            .AsNoTracking()
            .AnyAsync(m => m.Id == meetingId && m.CreatedBy == userId, cancellationToken);
    }

    public async Task<bool> IsProjectLeaderAsync(long projectId, long userId, CancellationToken cancellationToken = default)
    {
        return await context.Projects
            .AsNoTracking()
            .Where(p => p.Id == projectId)
            .SelectMany(p => p.Team.TeamMembers)
            .AnyAsync(m => m.UserId == userId && m.IsLeader && m.LeftAt == null, cancellationToken);
    }

    public async Task<bool> IsAssignedSupervisorAsync(long projectId, long userId, CancellationToken cancellationToken = default)
    {
        return await context.SupervisorAssignments
            .AsNoTracking()
            .AnyAsync(a => a.ProjectId == projectId && a.EndedAt == null && a.SupervisorProfile.UserId == userId, cancellationToken);
    }

    public async Task<bool> IsTaskBelongsToMilestoneAsync(long taskId, long milestoneId, CancellationToken cancellationToken = default)
    {
        return await context.Tasks
            .AsNoTracking()
            .AnyAsync(t => t.Id == taskId && t.MilestoneId == milestoneId, cancellationToken);
    }

    public async Task<bool> IsActiveTeamMemberAsync(long projectId, long userId, CancellationToken cancellationToken = default)
    {
        return await context.Projects
            .AsNoTracking()
            .Where(p => p.Id == projectId)
            .SelectMany(p => p.Team.TeamMembers)
            .AnyAsync(m => m.UserId == userId && m.LeftAt == null, cancellationToken);
    }

    public Task<bool> HasAdminRoleInDbAsync(long userId, CancellationToken cancellationToken = default)
        => context.UserRoles
            .AsNoTracking()
            .AnyAsync(ur => ur.UserId == userId && ur.User.Status == "ACTIVE" && ur.Role.Code == AppRoles.Admin, cancellationToken);

    private static ProjectActionItemDto ToDto(ProjectActionItem x) =>
        new(
            x.Id,
            x.ProjectId,
            x.SourceType,
            x.MeetingId,
            x.ProgressReportId,
            x.Title,
            x.Description,
            x.OwnerId,
            x.Owner?.FullName,
            x.TaskId,
            x.Task?.Title,
            x.MilestoneId,
            x.Milestone?.Title,
            x.DueAt,
            x.Status,
            x.CreatedBy,
            x.CreatedAt,
            x.UpdatedAt,
            x.ConcurrencyToken.ToString("N"));
}
