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

public sealed partial class ProjectRepository(AipmsDbContext context, TimeProvider? timeProvider = null) : IProjectRepository
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

        if (entity is null) return null;
        if (entity.Status is not ("DRAFT" or "REVISION_REQUIRED"))
        {
            var snapshot = await LatestRegistrationAsync(entity.Id, cancellationToken);
            if (snapshot is not null)
                return entity.ToDto() with { AcademicScope = System.Text.Json.JsonSerializer.Deserialize<RegistrationEvidence>(snapshot.SnapshotJson)!.Scope };
        }
        var scope = await GetTeamScopeAsync(entity.TeamId, cancellationToken);
        return entity.ToDto() with { AcademicScope = scope is null ? null : AIPMS.Application.Features.Teams.DTOs.TeamAcademicScopeDto.FromScope(scope) };
    }

    public Task<PagedResult<ProjectSummaryDto>> GetProjectsAsync(
        string? status,
        long? teamId,
        long? semesterId,
        long? majorId,
        string? tag,
        string? search,
        int page,
        int pageSize,
        CancellationToken cancellationToken) =>
        GetProjectsCoreAsync(null, status, teamId, semesterId, majorId, tag, search, page, pageSize, cancellationToken);

    public Task<PagedResult<ProjectSummaryDto>> GetVisibleProjectsAsync(long userId, string? status, long? teamId,
        long? semesterId, long? majorId, string? tag, string? search, int page, int pageSize, CancellationToken ct) =>
        GetProjectsCoreAsync(userId, status, teamId, semesterId, majorId, tag, search, page, pageSize, ct);

    private async Task<PagedResult<ProjectSummaryDto>> GetProjectsCoreAsync(long? userId, string? status, long? teamId,
        long? semesterId, long? majorId, string? tag, string? search, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = context.Projects
            .AsNoTracking()
            .Include(static p => p.Team)
            .Include(static p => p.ProjectMajors)
                .ThenInclude(static pm => pm.Major)
            .Include(static p => p.ProjectTags)
                .ThenInclude(static pt => pt.Tag)
            .AsQueryable();

        if (userId is long actorId)
        {
            var actor = await context.Users.AsNoTracking().Where(u => u.Id == actorId && u.Status == "ACTIVE")
                .Select(u => new { u.DepartmentId,
                    Admin = u.UserRoleUsers.Any(r => r.Role.Code == "ADMIN"),
                    Staff = u.UserRoleUsers.Any(r => r.Role.Code == "DEPARTMENT_STAFF") && u.Department != null
                        && u.Department.IsActive && u.Department.Organization.IsActive }).SingleOrDefaultAsync(cancellationToken);
            if (actor is null) throw new ForbiddenException("An active account is required.");
            var departmentProjects = DepartmentProjectIds(actor.DepartmentId);
            if (!actor.Admin)
                query = query.Where(p => p.Team.TeamMembers.Any(m => m.UserId == actorId && m.LeftAt == null)
                    || context.SupervisorAssignments.Any(a => a.ProjectId == p.Id && a.EndedAt == null && a.SupervisorProfile.UserId == actorId)
                    || (actor.Staff && departmentProjects.Contains(p.Id)));
        }

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
            var departmentProjects = DepartmentProjectIds(departmentId);
            query = query.Where(p => departmentProjects.Contains(p.Id));
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
                      && pp.EndAt > currentUtc)
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
                      && (tm.Team.Status == "FORMING" || tm.Team.Status == "ELIGIBLE"))
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
        await using var transaction = context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(cancellationToken)
            : null;
        try
        {
            await ValidateProjectMajorsAsync(teamId, majorIds, cancellationToken);
            var utcNow = Now;
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
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);

            return (await GetByIdAsync(project.Id, cancellationToken))!;
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is Microsoft.Data.SqlClient.SqlException sqlException 
                  && (sqlException.Number == 2601 || sqlException.Number == 2627)
                  && sqlException.Message.Contains("uq_projects_active_team"))
        {
            if (transaction is not null) await transaction.RollbackAsync(CancellationToken.None);
            throw new ConflictException("The team already has an active or unfinished project proposal.");
        }
        catch
        {
            if (transaction is not null) await transaction.RollbackAsync(CancellationToken.None);
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
        await using var transaction = context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(cancellationToken) : null;
        try
        {
            await LockProjectAsync(projectId, cancellationToken);
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

            if (project.Status is not ("DRAFT" or "REVISION_REQUIRED"))
                throw new ConflictException("Only an editable proposal can be updated.");
            await ValidateProjectMajorsAsync(project.TeamId, majorIds, cancellationToken);
            var utcNow = Now;
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
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);

            return (await GetByIdAsync(project.Id, cancellationToken))!;
        }
        catch (DbUpdateConcurrencyException)
        {
            if (transaction is not null) await transaction.RollbackAsync(CancellationToken.None);
            throw new ConflictException("The project has been modified by another user. Please refresh and try again.");
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is Microsoft.Data.SqlClient.SqlException sqlException 
                  && (sqlException.Number == 2601 || sqlException.Number == 2627)
                  && sqlException.Message.Contains("uq_projects_active_team"))
        {
            if (transaction is not null) await transaction.RollbackAsync(CancellationToken.None);
            throw new ConflictException("The team already has an active or unfinished project proposal.");
        }
        catch
        {
            if (transaction is not null) await transaction.RollbackAsync(CancellationToken.None);
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
        await using var transaction = context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(cancellationToken)
            : null;
        try
        {
            await LockProjectAsync(projectId, cancellationToken);
            var project = await context.Projects
                .SingleOrDefaultAsync(p => p.Id == projectId, cancellationToken)
                ?? throw new NotFoundException("Project", projectId);

            var existingToken = Convert.ToBase64String(project.RowVersion);
            if (existingToken != concurrencyToken)
            {
                throw new ConflictException("The project has been modified by another user. Please refresh and try again.");
            }

            if (project.Status != oldStatus) throw new ConflictException("Project status changed. Refresh and retry.");
            await ValidateAcademicReviewTransitionAsync(project, newStatus, actorUserId, cancellationToken);
            var utcNow = Now;
            if (newStatus == "SUBMITTED") await CaptureRegistrationAsync(project, actorUserId, utcNow, cancellationToken);
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
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);

            return (await GetByIdAsync(projectId, cancellationToken))!;
        }
        catch (DbUpdateConcurrencyException)
        {
            if (transaction is not null) await transaction.RollbackAsync(CancellationToken.None);
            throw new ConflictException("The project has been modified by another user. Please refresh and try again.");
        }
        catch
        {
            if (transaction is not null) await transaction.RollbackAsync(CancellationToken.None);
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
            && pp.EndAt > currentUtc,
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
        var status = await context.Projects.Where(p => p.Id == projectId).Select(p => p.Status).SingleOrDefaultAsync(cancellationToken);
        if (status is not ("DRAFT" or "REVISION_REQUIRED"))
        {
            var snapshot = await LatestRegistrationAsync(projectId, cancellationToken);
            if (snapshot is not null)
                return System.Text.Json.JsonSerializer.Deserialize<RegistrationEvidence>(snapshot.SnapshotJson)!.DepartmentIds;
        }
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

    public async Task<ProjectProgressSummaryDto> GetProjectProgressSummaryAsync(
        long projectId,
        CancellationToken cancellationToken)
    {
        var milestonesData = await context.Milestones
            .AsNoTracking()
            .Where(m => m.ProjectId == projectId)
            .Select(static m => new { m.Id, m.Status })
            .ToListAsync(cancellationToken);

        var totalMilestones = milestonesData.Count;
        var completedMilestones = milestonesData.Count(static m => m.Status == "COMPLETED");

        var tasksData = await context.Tasks
            .AsNoTracking()
            .Where(t => t.Milestone.ProjectId == projectId)
            .Select(static t => new { t.Id, t.Status, t.DueAt })
            .ToListAsync(cancellationToken);

        var totalTasks = tasksData.Count;
        var doneTasks = tasksData.Count(static t => t.Status == "DONE");
        var blockedTasks = tasksData.Count(static t => t.Status == "BLOCKED");

        var utcNow = DateTime.UtcNow;
        var overdueTasks = tasksData.Count(t => t.DueAt < utcNow && t.Status != "DONE" && t.Status != "CANCELLED");

        var progressPercentage = totalTasks == 0
            ? 0.0
            : Math.Round((doneTasks * 100.0) / totalTasks, 2);

        return new ProjectProgressSummaryDto(
            projectId,
            totalTasks,
            doneTasks,
            blockedTasks,
            overdueTasks,
            totalMilestones,
            completedMilestones,
            progressPercentage);
    }

    public async Task<ProjectTimelineDataDto> GetTimelineDataAsync(
        long projectId,
        CancellationToken cancellationToken)
    {
        var milestones = await context.Milestones
            .AsNoTracking()
            .Where(m => m.ProjectId == projectId)
            .OrderBy(static m => m.SortOrder)
            .ThenBy(static m => m.Id)
            .Include(static m => m.Tasks)
                .ThenInclude(static t => t.TaskAssignees)
                    .ThenInclude(static ta => ta.User)
            .Include(static m => m.Tasks)
                .ThenInclude(static t => t.TaskDependencyTasks)
            .ToListAsync(cancellationToken);

        var timelineMilestones = new List<TimelineMilestoneDto>();
        foreach (var milestone in milestones)
        {
            var totalTasks = milestone.Tasks.Count;
            var doneTasks = milestone.Tasks.Count(static t => t.Status == "DONE");
            var progressPercentage = totalTasks == 0
                ? 0.0
                : Math.Round((doneTasks * 100.0) / totalTasks, 2);

            var timelineTasks = milestone.Tasks
                .OrderBy(static t => t.DueAt == null ? 1 : 0)
                .ThenBy(static t => t.DueAt)
                .ThenBy(static t => t.Id)
                .Select(static t => new TimelineTaskDto(
                    t.Id,
                    t.ParentTaskId,
                    t.Title,
                    t.Description,
                    t.Status,
                    t.Priority,
                    t.StartAt,
                    t.DueAt,
                    t.CompletedAt,
                    t.TaskAssignees.Select(static ta => new TimelineTaskAssigneeDto(
                        ta.UserId,
                        ta.User.FullName)).ToArray(),
                    t.TaskDependencyTasks.Select(static td => new TimelineTaskDependencyDto(
                        td.DependsOnTaskId,
                        td.DependencyType)).ToArray()
                )).ToList();

            timelineMilestones.Add(new TimelineMilestoneDto(
                milestone.Id,
                milestone.Title,
                milestone.Description,
                milestone.StartDate,
                milestone.DueDate,
                milestone.Status,
                milestone.SortOrder,
                progressPercentage,
                timelineTasks));
        }

        return new ProjectTimelineDataDto(projectId, timelineMilestones);
    }
}
