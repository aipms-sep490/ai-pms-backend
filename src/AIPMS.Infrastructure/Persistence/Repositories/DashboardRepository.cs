using System.Threading.Tasks;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Security;
using AIPMS.Domain.Exceptions;
using AIPMS.Application.Features.Dashboards.Abstractions;
using AIPMS.Application.Features.Dashboards.DTOs;
using AIPMS.Application.Features.Projects.Models;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Project = AIPMS.Infrastructure.Persistence.Generated.Models.Project;

namespace AIPMS.Infrastructure.Persistence.Repositories;

public sealed class DashboardRepository(AipmsDbContext db) : IDashboardRepository
{
    private const int MaxPortfolioProjects = 10_000;
    private static readonly string[] OpenTaskStates = ["TODO", "IN_PROGRESS", "BLOCKED", "IN_REVIEW"];

    public async Task<T> InReadTransactionAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct)
        => await action(ct);

    public async Task RequireRoleAsync(long userId, string role, CancellationToken ct)
    {
        var query = db.Users.Where(u => u.Id == userId && u.Status == "ACTIVE"
            && u.UserRoleUsers.Any(r => r.Role.Code == role));
        if (!string.Equals(role, AppRoles.Admin, StringComparison.Ordinal))
            query = query.Where(u => u.Department != null && u.Department.IsActive && u.Department.Organization.IsActive);
        if (!await query.AnyAsync(ct))
            throw new ForbiddenException("An active account, persisted role and academic scope are required.");
    }

    public async Task<StudentDashboardFacts> GetStudentAsync(long userId, long? projectId, DateTime now, CancellationToken ct)
    {
        var unread = await db.NotificationRecipients.CountAsync(n => n.UserId == userId && !n.IsRead, ct);
        if (projectId is null) return new(null, unread, 0, 0, [], []);
        var projects = db.Projects.AsNoTracking().Where(p => p.Id == projectId
            && p.Team.TeamMembers.Any(m => m.UserId == userId && m.LeftAt == null));
        var project = (await ReadProjectsAsync(projects, ct)).SingleOrDefault()
            ?? throw new ForbiddenException("Current project membership is required.");
        var tasks = db.Tasks.AsNoTracking().Where(t => t.Milestone.ProjectId == projectId
            && OpenTaskStates.Contains(t.Status) && t.TaskAssignees.Any(a => a.UserId == userId));
        var open = await tasks.CountAsync(ct);
        var overdue = await tasks.CountAsync(t => t.DueAt < now, ct);
        var dueTasks = await tasks.Where(t => t.DueAt != null).OrderBy(t => t.DueAt).ThenBy(t => t.Id).Take(10)
            .Select(t => new { t.Id, t.Title, t.Status, DueAt = t.DueAt!.Value }).ToArrayAsync(ct);
        var today = DateOnly.FromDateTime(now);
        var dueMilestones = project.Facts.Milestones.Where(m => m.DueDate.HasValue && m.Status is not ("COMPLETED" or "CANCELLED"))
            .OrderBy(m => m.DueDate).ThenBy(m => m.Id).Take(10)
            .Select(m => new DashboardMilestoneDto(m.Id, m.Title, m.Status, m.DueDate!.Value, m.DueDate < today)).ToArray();
        return new(project, unread, open, overdue,
            dueTasks.Select(t => new DashboardTaskDto(t.Id, t.Title, t.Status,
                DateTime.SpecifyKind(t.DueAt, DateTimeKind.Utc), t.DueAt < now)).ToArray(), dueMilestones);
    }

    public async Task<SupervisorDashboardFacts> GetSupervisorAsync(long userId, SupervisorDashboardFilter filter,
        DateTime now, CancellationToken ct)
    {
        var profile = await db.SupervisorProfiles.AsNoTracking().Where(p => p.UserId == userId)
            .Select(p => new { p.IsAvailable, p.MaxActiveProjects }).SingleOrDefaultAsync(ct);
        // An ended assignment does not grant access to live project data in the existing resource policy.
        var assignedIds = db.SupervisorAssignments.Where(a => a.SupervisorProfile.UserId == userId && a.EndedAt == null)
            .Select(a => a.ProjectId).Distinct();
        var workload = new SupervisorWorkloadDto(profile is not null, profile?.IsAvailable ?? false,
            await assignedIds.CountAsync(ct), profile?.MaxActiveProjects);
        var projects = db.Projects.AsNoTracking().Where(p => assignedIds.Contains(p.Id));
        if (filter.SemesterId.HasValue) projects = projects.Where(p => p.Team.AcademicSemesterId == filter.SemesterId);
        if (filter.Status is not null) projects = projects.Where(p => p.Status == filter.Status);
        if (!string.IsNullOrWhiteSpace(filter.Search)) projects = projects.Where(p => p.Code.Contains(filter.Search) || p.Title.Contains(filter.Search));
        var states = await projects.GroupBy(p => p.Status).OrderBy(g => g.Key)
            .Select(g => new DashboardStatusCountDto(g.Key, g.Count())).ToArrayAsync(ct);
        var ids = projects.Select(p => p.Id);
        var pending = await db.ProgressReports.CountAsync(r => ids.Contains(r.ProjectId) && r.Status == "SUBMITTED", ct);
        var overdue = await db.Tasks.CountAsync(t => ids.Contains(t.Milestone.ProjectId)
            && OpenTaskStates.Contains(t.Status) && t.DueAt < now, ct);
        var page = projects.OrderByDescending(p => p.CreatedAt).ThenByDescending(p => p.Id)
            .Skip((filter.Page - 1) * filter.PageSize).Take(filter.PageSize);
        return new(workload, states, pending, overdue, states.Sum(s => (long)s.Count), await ReadProjectsAsync(page, ct));
    }

    public async Task<PortfolioDashboardFacts> GetPortfolioAsync(long userId, bool isAdmin,
        DashboardPortfolioFilter filter, DateTime now, CancellationToken ct)
    {
        var actor = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId && u.Status == "ACTIVE")
            .Select(u => new
            {
                u.DepartmentId,
                IsAdmin = u.UserRoleUsers.Any(r => r.Role.Code == AppRoles.Admin),
                IsStaff = u.UserRoleUsers.Any(r => r.Role.Code == AppRoles.DepartmentStaff)
                    && u.Department != null && u.Department.IsActive && u.Department.Organization.IsActive
            }).SingleOrDefaultAsync(ct);
        if (actor is null || (isAdmin && !actor.IsAdmin) || (!isAdmin && (!actor.IsStaff || !actor.DepartmentId.HasValue)))
            throw new ForbiddenException("An active persisted dashboard role and academic scope are required.");
        if (!isAdmin && filter.DepartmentId.HasValue && filter.DepartmentId != actor.DepartmentId)
            throw new ForbiddenException("Department staff can only view their own department.");

        var departmentId = isAdmin ? filter.DepartmentId : actor.DepartmentId;
        var query = db.Projects.AsNoTracking().AsQueryable();
        if (departmentId.HasValue)
            query = query.Where(p => DepartmentProjectIds(departmentId.Value).Contains(p.Id));
        if (filter.SemesterId.HasValue)
            query = query.Where(p => p.Team.AcademicSemesterId == filter.SemesterId.Value);
        if (filter.MajorId.HasValue)
            query = query.Where(p => p.ProjectMajors.Any(pm => pm.MajorId == filter.MajorId.Value));
        if (!string.IsNullOrWhiteSpace(filter.Status))
            query = query.Where(p => p.Status == filter.Status);
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var search = filter.Search.Trim();
            query = query.Where(p => p.Code.Contains(search) || p.Title.Contains(search));
        }

        var total = await query.LongCountAsync(ct);
        if (total > MaxPortfolioProjects)
            throw new DomainException($"The filtered portfolio contains more than {MaxPortfolioProjects} projects. Narrow the filters and retry.");
        var projects = await ReadProjectsAsync(query.OrderByDescending(p => p.CreatedAt).ThenByDescending(p => p.Id), ct);
        return new(departmentId, projects);
    }

    private async Task<IReadOnlyList<DashboardProjectFacts>> ReadProjectsAsync(IQueryable<Project> projects, CancellationToken ct)
    {
        var rows = await projects.Select(p => new { p.Id, p.Code, p.Title, p.Status, p.TeamId,
            p.CreatedAt, SemesterId = p.Team.AcademicSemesterId, MemberCount = p.Team.TeamMembers.Count(m => m.LeftAt == null),
            Majors = p.ProjectMajors.Select(pm => new DashboardMajorFact(p.Id, pm.MajorId, pm.Major.Code, pm.Major.Name,
                pm.Major.DepartmentId, pm.Major.Department.Name)).ToArray() }).ToArrayAsync(ct);
        if (rows.Length == 0) return [];
        var ids = rows.Select(p => p.Id).ToArray();
        // Four batched fact queries for the whole page, independent of the number of projects.
        var milestones = (await db.Milestones.AsNoTracking().Where(m => ids.Contains(m.ProjectId))
            .OrderBy(m => m.SortOrder).ThenBy(m => m.Id)
            .Select(m => new { m.ProjectId, Fact = new MilestoneFact(m.Id, m.Title, m.Status, m.StartDate, m.DueDate, m.SortOrder) })
            .ToArrayAsync(ct)).ToLookup(m => m.ProjectId, m => m.Fact);
        var tasks = (await db.Tasks.AsNoTracking().Where(t => ids.Contains(t.Milestone.ProjectId))
            .OrderBy(t => t.Id).Select(t => new { t.Milestone.ProjectId,
                Fact = new TaskFact(t.Id, t.MilestoneId, t.Title, t.Status, t.Priority, t.StartAt, t.DueAt, t.CompletedAt, t.TaskAssignees.Count) })
            .ToArrayAsync(ct)).ToLookup(t => t.ProjectId, t => t.Fact);
        var reports = (await db.ProgressReports.AsNoTracking().Where(r => ids.Contains(r.ProjectId))
            .OrderByDescending(r => r.PeriodEnd).ThenBy(r => r.Id)
            .Select(r => new { r.ProjectId, Fact = new ProgressReportFact(r.Id, r.ReportType, r.PeriodStart, r.PeriodEnd, r.Status, r.SubmittedAt) })
            .ToArrayAsync(ct)).ToLookup(r => r.ProjectId, r => r.Fact);
        var meetings = (await db.Meetings.AsNoTracking().Where(m => ids.Contains(m.ProjectId)).OrderBy(m => m.Id)
            .Select(m => new { m.ProjectId, Fact = new MeetingFact(m.Id, m.Title, m.Status, m.StartAt, m.EndAt) })
            .ToArrayAsync(ct)).ToLookup(m => m.ProjectId, m => m.Fact);
        var supervisors = (await db.SupervisorAssignments.AsNoTracking()
            .Where(a => ids.Contains(a.ProjectId) && a.EndedAt == null && a.IsPrimary)
            .Select(a => new { a.ProjectId, Fact = new DashboardSupervisorFact(a.SupervisorProfile.UserId, a.SupervisorProfile.User.FullName) })
            .ToArrayAsync(ct)).ToLookup(a => a.ProjectId, a => a.Fact);
        return rows.Select(p => new DashboardProjectFacts(p.Id, p.Code, p.Title, p.Status, p.TeamId, p.SemesterId,
            reports[p.Id].Count(r => r.Status == "SUBMITTED"),
            new(p.Id, p.Status, p.TeamId, p.MemberCount, milestones[p.Id].ToArray(), tasks[p.Id].ToArray(),
                reports[p.Id].ToArray(), meetings[p.Id].ToArray()), p.Majors,
                supervisors[p.Id].FirstOrDefault(), p.CreatedAt)).ToArray();
    }

    private IQueryable<long> DepartmentProjectIds(long departmentId)
    {
        var snapshots = db.Set<ProjectRegistrationSnapshot>();
        var latest = snapshots.Where(s => !snapshots.Any(newer => newer.ProjectId == s.ProjectId && newer.Id > s.Id));
        return db.Projects.Where(p =>
            ((p.Status == "DRAFT" || p.Status == "REVISION_REQUIRED" || !latest.Any(s => s.ProjectId == p.Id))
                && p.ProjectMajors.Any(m => m.Major.DepartmentId == departmentId))
            || (p.Status != "DRAFT" && p.Status != "REVISION_REQUIRED" && latest.Any(s => s.ProjectId == p.Id
                && (s.LeadDepartmentId == departmentId || s.Decisions.Any(d => d.DepartmentId == departmentId)))))
            .Select(p => p.Id);
    }
}
