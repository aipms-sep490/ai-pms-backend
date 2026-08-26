using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Projects.Abstractions;
using AIPMS.Application.Features.Projects.DTOs;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Generated.Models;
using AIPMS.Infrastructure.Persistence.Mappers;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Persistence.Repositories;

public sealed class ProjectRepository(AipmsDbContext context) : IProjectRepository
{
    private static readonly string[] ActiveStatuses = 
    [
        "DRAFT", "SUBMITTED", "UNDER_REVIEW", "REVISION_REQUIRED", 
        "APPROVED", "SUPERVISOR_PENDING", "ACTIVE", "FINAL_SUBMISSION"
    ];

    public async Task<ProjectDto?> GetByIdAsync(long id, CancellationToken cancellationToken)
    {
        var entity = await context.Projects
            .AsNoTracking()
            .Include(static p => p.Team)
            .Include(static p => p.CreatedByNavigation)
            .Include(static p => p.ProjectMajors)
                .ThenInclude(static pm => pm.Major)
            .Include(static p => p.ProjectTags)
                .ThenInclude(static pt => pt.Tag)
            .SingleOrDefaultAsync(p => p.Id == id, cancellationToken);

        return entity?.ToDto();
    }

    public async Task<PagedResult<ProjectSummaryDto>> GetProjectsAsync(
        string? status,
        long? teamId,
        long? semesterId,
        long? majorId,
        string? tag,
        string? search,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var query = context.Projects
            .AsNoTracking()
            .Include(static p => p.Team)
            .Include(static p => p.ProjectMajors)
                .ThenInclude(static pm => pm.Major)
            .Include(static p => p.ProjectTags)
                .ThenInclude(static pt => pt.Tag)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(p => p.Status == status);
        }

        if (teamId.HasValue)
        {
            query = query.Where(p => p.TeamId == teamId.Value);
        }

        if (semesterId.HasValue)
        {
            query = query.Where(p => p.Team.AcademicSemesterId == semesterId.Value);
        }

        if (majorId.HasValue)
        {
            query = query.Where(p => p.ProjectMajors.Any(pm => pm.MajorId == majorId.Value));
        }

        if (!string.IsNullOrWhiteSpace(tag))
        {
            var normalizedTag = Normalize(tag);
            query = query.Where(p => p.ProjectTags.Any(pt => pt.Tag.NormalizedName == normalizedTag));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(p => p.Code.Contains(search) || p.Title.Contains(search));
        }

        var totalCount = await query.LongCountAsync(cancellationToken);
        var entities = await query
            .OrderByDescending(static p => p.CreatedAt)
            .ThenBy(static p => p.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var dtos = entities.Select(static p => p.ToSummaryDto()).ToArray();

        return new PagedResult<ProjectSummaryDto>(dtos, page, pageSize, totalCount);
    }

    public async Task<PagedResult<ProjectSummaryDto>> GetReviewQueueAsync(
        long? departmentId,
        string? search,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var query = context.Projects
            .AsNoTracking()
            .Include(static p => p.Team)
            .Include(static p => p.ProjectMajors)
                .ThenInclude(static pm => pm.Major)
            .Include(static p => p.ProjectTags)
                .ThenInclude(static pt => pt.Tag)
            .Where(static p => p.Status == "SUBMITTED" || p.Status == "UNDER_REVIEW");

        if (departmentId.HasValue)
        {
            query = query.Where(p => p.ProjectMajors.Any(pm => pm.Major.DepartmentId == departmentId.Value));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(p => p.Code.Contains(search) || p.Title.Contains(search));
        }

        var totalCount = await query.LongCountAsync(cancellationToken);
        var entities = await query
            .OrderByDescending(static p => p.CreatedAt)
            .ThenBy(static p => p.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var dtos = entities.Select(static p => p.ToSummaryDto()).ToArray();

        return new PagedResult<ProjectSummaryDto>(dtos, page, pageSize, totalCount);
    }

    public Task<bool> HasActiveProjectAsync(long teamId, CancellationToken cancellationToken) =>
        context.Projects.AnyAsync(p => p.TeamId == teamId && ActiveStatuses.Contains(p.Status), cancellationToken);

    public async Task<long?> GetActiveRegistrationSemesterIdAsync(long userId, DateTime currentUtc, CancellationToken cancellationToken)
    {
        var user = await context.Users
            .AsNoTracking()
            .Include(u => u.Department)
            .Include(u => u.Major)
                .ThenInclude(m => m!.Department!)
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user is null) return null;

        long? orgId = user.Department?.OrganizationId ?? user.Major?.Department?.OrganizationId;
        if (orgId is null) return null;

        var periods = await context.ProjectPeriods
            .AsNoTracking()
            .Where(pp => pp.AcademicSemester.OrganizationId == orgId.Value
                      && pp.AcademicSemester.Status == "ACTIVE"
                      && pp.PeriodType == "REGISTRATION"
                      && pp.Status == "ACTIVE"
                      && pp.StartAt <= currentUtc
                      && pp.EndAt >= currentUtc)
            .ToListAsync(cancellationToken);

        if (periods.Count == 0)
        {
            return null;
        }

        if (periods.Select(pp => pp.AcademicSemesterId).Distinct().Count() > 1)
        {
            throw new ConflictException("Ambiguous active registration periods detected across multiple semesters.");
        }

        return periods[0].AcademicSemesterId;
    }

    public async Task<long?> GetUserActiveTeamIdAsync(long userId, long semesterId, CancellationToken cancellationToken)
    {
        var teamIds = await context.TeamMembers
            .AsNoTracking()
            .Where(tm => tm.UserId == userId 
                      && tm.LeftAt == null 
                      && tm.IsLeader == true
                      && tm.Team.AcademicSemesterId == semesterId 
                      && tm.Team.Status == "ELIGIBLE")
            .Select(static tm => tm.TeamId)
            .Take(2)
            .ToListAsync(cancellationToken);

        if (teamIds.Count == 0)
        {
            return null;
        }

        if (teamIds.Count > 1)
        {
            throw new ConflictException("Ambiguous active teams detected for the user in this semester.");
        }

        return teamIds[0];
    }

    public Task<bool> IsTeamLeaderAsync(long teamId, long userId, CancellationToken cancellationToken) =>
        context.TeamMembers
            .AsNoTracking()
            .AnyAsync(tm => tm.TeamId == teamId && tm.UserId == userId && tm.IsLeader && tm.LeftAt == null, cancellationToken);

    public async Task<ProjectDto> CreateDraftAsync(
        long teamId,
        long userId,
        string title,
        string? description,
        string? objectives,
        string? problemStatement,
        string? expectedOutput,
        IReadOnlyList<long> majorIds,
        string domain,
        IReadOnlyList<string> technologies,
        IReadOnlyList<string> keywords,
        CancellationToken cancellationToken)
    {
        using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var utcNow = DateTime.UtcNow;
            var project = new Project
            {
                TeamId = teamId,
                Code = "PRJ-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(),
                Title = title.Trim(),
                Description = description?.Trim(),
                Objectives = objectives?.Trim(),
                Status = "DRAFT",
                RegisteredAt = utcNow,
                CreatedBy = userId,
                CreatedAt = utcNow,
                UpdatedAt = utcNow,
                ProblemStatement = problemStatement?.Trim(),
                ExpectedOutput = expectedOutput?.Trim()
            };

            context.Projects.Add(project);

            // Associate majors
            foreach (var majorId in majorIds)
            {
                context.ProjectMajors.Add(new ProjectMajor
                {
                    Project = project,
                    MajorId = majorId,
                    CreatedAt = utcNow
                });
            }

            // Associate tags
            var tags = await GetOrCreateTagsInternalAsync(domain, technologies, keywords, utcNow, cancellationToken);
            foreach (var tag in tags)
            {
                context.ProjectTags.Add(new ProjectTag
                {
                    Project = project,
                    Tag = tag,
                    CreatedAt = utcNow
                });
            }

            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return (await GetByIdAsync(project.Id, cancellationToken))!;
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is Microsoft.Data.SqlClient.SqlException sqlException 
                  && (sqlException.Number == 2601 || sqlException.Number == 2627)
                  && sqlException.Message.Contains("uq_projects_active_team"))
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new ConflictException("The team already has an active or unfinished project proposal.");
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<ProjectDto> UpdateDraftAsync(
        long projectId,
        string concurrencyToken,
        string title,
        string? description,
        string? objectives,
        string? problemStatement,
        string? expectedOutput,
        IReadOnlyList<long> majorIds,
        string domain,
        IReadOnlyList<string> technologies,
        IReadOnlyList<string> keywords,
        CancellationToken cancellationToken)
    {
        using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var project = await context.Projects
                .Include(static p => p.ProjectMajors)
                .Include(static p => p.ProjectTags)
                .SingleOrDefaultAsync(p => p.Id == projectId, cancellationToken)
                ?? throw new NotFoundException("Project", projectId);

            var existingToken = Convert.ToBase64String(project.RowVersion);
            if (existingToken != concurrencyToken)
            {
                throw new ConflictException("The project has been modified by another user. Please refresh and try again.");
            }

            var utcNow = DateTime.UtcNow;
            project.Title = title.Trim();
            project.Description = description?.Trim();
            project.Objectives = objectives?.Trim();
            project.ProblemStatement = problemStatement?.Trim();
            project.ExpectedOutput = expectedOutput?.Trim();
            project.UpdatedAt = utcNow;

            // Update majors
            context.ProjectMajors.RemoveRange(project.ProjectMajors);
            foreach (var majorId in majorIds)
            {
                context.ProjectMajors.Add(new ProjectMajor
                {
                    Project = project,
                    MajorId = majorId,
                    CreatedAt = utcNow
                });
            }

            // Update tags
            context.ProjectTags.RemoveRange(project.ProjectTags);
            var tags = await GetOrCreateTagsInternalAsync(domain, technologies, keywords, utcNow, cancellationToken);
            foreach (var tag in tags)
            {
                context.ProjectTags.Add(new ProjectTag
                {
                    Project = project,
                    Tag = tag,
                    CreatedAt = utcNow
                });
            }

            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return (await GetByIdAsync(project.Id, cancellationToken))!;
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new ConflictException("The project has been modified by another user. Please refresh and try again.");
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is Microsoft.Data.SqlClient.SqlException sqlException 
                  && (sqlException.Number == 2601 || sqlException.Number == 2627)
                  && sqlException.Message.Contains("uq_projects_active_team"))
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new ConflictException("The team already has an active or unfinished project proposal.");
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<ProjectDto> UpdateStatusAsync(
        long projectId,
        string concurrencyToken,
        string oldStatus,
        string newStatus,
        long actorUserId,
        string? reason,
        CancellationToken cancellationToken)
    {
        using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var project = await context.Projects
                .SingleOrDefaultAsync(p => p.Id == projectId, cancellationToken)
                ?? throw new NotFoundException("Project", projectId);

            var existingToken = Convert.ToBase64String(project.RowVersion);
            if (existingToken != concurrencyToken)
            {
                throw new ConflictException("The project has been modified by another user. Please refresh and try again.");
            }

            var utcNow = DateTime.UtcNow;
            project.Status = newStatus;
            project.UpdatedAt = utcNow;

            if (newStatus == "SUBMITTED")
            {
                project.SubmittedAt = utcNow;
            }
            else if (newStatus == "APPROVED")
            {
                project.ApprovedAt = utcNow;
            }
            else if (newStatus == "COMPLETED")
            {
                project.CompletedAt = utcNow;
            }

            // Write status history
            var history = new ProjectStatusHistory
            {
                ProjectId = projectId,
                OldStatus = oldStatus,
                NewStatus = newStatus,
                ChangedBy = actorUserId,
                Reason = reason,
                ChangedAt = utcNow
            };
            context.ProjectStatusHistories.Add(history);

            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return (await GetByIdAsync(projectId, cancellationToken))!;
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new ConflictException("The project has been modified by another user. Please refresh and try again.");
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<IReadOnlyList<ProjectStatusHistoryDto>> GetStatusHistoryAsync(
        long projectId,
        CancellationToken cancellationToken)
    {
        var history = await context.ProjectStatusHistories
            .AsNoTracking()
            .Include(static h => h.ChangedByNavigation)
            .Where(h => h.ProjectId == projectId)
            .OrderBy(static h => h.ChangedAt)
            .ThenBy(static h => h.Id)
            .ToListAsync(cancellationToken);

        return history.Select(static h => new ProjectStatusHistoryDto(
            h.Id,
            h.ProjectId,
            h.OldStatus,
            h.NewStatus,
            h.ChangedBy,
            h.ChangedByNavigation.FullName,
            h.Reason,
            h.ChangedAt)).ToArray();
    }

    public Task<bool> IsSemesterRegistrationOpenAsync(
        long semesterId,
        DateTime currentUtc,
        CancellationToken cancellationToken) =>
        context.ProjectPeriods.AnyAsync(pp => 
            pp.AcademicSemesterId == semesterId 
            && pp.PeriodType == "REGISTRATION" 
            && pp.Status == "ACTIVE" 
            && pp.StartAt <= currentUtc 
            && pp.EndAt >= currentUtc, 
            cancellationToken);

    public Task<long?> GetSemesterIdByTeamIdAsync(
        long teamId,
        CancellationToken cancellationToken) =>
        context.Teams
            .AsNoTracking()
            .Where(t => t.Id == teamId)
            .Select(static t => (long?)t.AcademicSemesterId)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<bool> ValidateMajorsExistAsync(
        IEnumerable<long> majorIds,
        CancellationToken cancellationToken)
    {
        var count = await context.Majors
            .AsNoTracking()
            .Where(m => majorIds.Contains(m.Id) && m.IsActive)
            .CountAsync(cancellationToken);

        return count == majorIds.Distinct().Count();
    }

    public Task<bool> IsTeamEligibleAsync(
        long teamId,
        CancellationToken cancellationToken) =>
        context.Teams
            .AsNoTracking()
            .AnyAsync(t => t.Id == teamId && t.Status == "ELIGIBLE", cancellationToken);

    public Task<bool> ProjectBelongsToTeamAsync(
        long projectId,
        long teamId,
        CancellationToken cancellationToken) =>
        context.Projects
            .AsNoTracking()
            .AnyAsync(p => p.Id == projectId && p.TeamId == teamId, cancellationToken);

    public async Task<IReadOnlyList<long>> GetProjectMajorDepartmentIdsAsync(
        long projectId,
        CancellationToken cancellationToken)
    {
        var departmentIds = await context.ProjectMajors
            .AsNoTracking()
            .Where(pm => pm.ProjectId == projectId)
            .Select(static pm => pm.Major.DepartmentId)
            .ToListAsync(cancellationToken);

        return departmentIds;
    }

    public async Task<bool> CanUserViewProjectAsync(
        long projectId,
        long userId,
        bool isAdmin,
        long? staffScopeDepartmentId,
        CancellationToken cancellationToken)
    {
        if (isAdmin) return true;
        if (staffScopeDepartmentId.HasValue)
        {
            var projectDeptIds = await GetProjectMajorDepartmentIdsAsync(projectId, cancellationToken);
            return projectDeptIds.Contains(staffScopeDepartmentId.Value);
        }

        var belongsToTeam = await context.Projects.AnyAsync(p => 
            p.Id == projectId 
            && p.Team.TeamMembers.Any(tm => tm.UserId == userId && tm.LeftAt == null),
            cancellationToken);

        if (belongsToTeam) return true;

        var isSupervisor = await context.SupervisorAssignments.AnyAsync(sa => 
            sa.ProjectId == projectId 
            && sa.SupervisorProfile.UserId == userId
            && sa.EndedAt == null, 
            cancellationToken);

        return isSupervisor;
    }

    private static string Normalize(string name) => name.Trim().ToUpperInvariant().Replace(" ", "_");

    private async Task<List<Tag>> GetOrCreateTagsInternalAsync(
        string domain,
        IEnumerable<string> technologies,
        IEnumerable<string> keywords,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        var tags = new List<Tag>();

        if (!string.IsNullOrWhiteSpace(domain))
        {
            tags.Add(await GetOrCreateTagAsync(domain, "DOMAIN", utcNow, cancellationToken));
        }

        foreach (var tech in technologies.Where(static t => !string.IsNullOrWhiteSpace(t)))
        {
            tags.Add(await GetOrCreateTagAsync(tech, "TECHNOLOGY", utcNow, cancellationToken));
        }

        foreach (var kw in keywords.Where(static k => !string.IsNullOrWhiteSpace(k)))
        {
            tags.Add(await GetOrCreateTagAsync(kw, "KEYWORD", utcNow, cancellationToken));
        }

        return tags.GroupBy(t => new { t.NormalizedName, t.TagType })
                   .Select(g => g.First())
                   .ToList();
    }

    private async Task<Tag> GetOrCreateTagAsync(
        string name,
        string type,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        var normalized = Normalize(name);
        var existing = await context.Tags
            .FirstOrDefaultAsync(t => t.NormalizedName == normalized && t.TagType == type, cancellationToken);

        if (existing is not null)
        {
            return existing;
        }

        var tracked = context.Tags.Local
            .FirstOrDefault(t => t.NormalizedName == normalized && t.TagType == type);

        if (tracked is not null)
        {
            return tracked;
        }

        var newTag = new Tag
        {
            Name = name.Trim(),
            NormalizedName = normalized,
            TagType = type,
            CreatedAt = utcNow
        };

        context.Tags.Add(newTag);
        return newTag;
    }
}
