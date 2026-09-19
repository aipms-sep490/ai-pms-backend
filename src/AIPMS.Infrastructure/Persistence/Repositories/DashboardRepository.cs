using System.Threading.Tasks;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Dashboards.Abstractions;
using AIPMS.Application.Features.Dashboards.DTOs;
using AIPMS.Application.Features.Projects.Models;
using AIPMS.Infrastructure.Persistence.Generated;
using Microsoft.EntityFrameworkCore;
using Project = AIPMS.Infrastructure.Persistence.Generated.Models.Project;

namespace AIPMS.Infrastructure.Persistence.Repositories;

public sealed class DashboardRepository(AipmsDbContext db) : IDashboardRepository
{
    private static readonly string[] OpenTaskStates = ["TODO", "IN_PROGRESS", "BLOCKED", "IN_REVIEW"];

    public async Task<T> InReadTransactionAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct)
        => await action(ct);

    public async Task RequireRoleAsync(long userId, string role, CancellationToken ct)
    {
        if (!await db.Users.AnyAsync(u => u.Id == userId && u.Status == "ACTIVE"
            && u.UserRoleUsers.Any(r => r.Role.Code == role)
            && u.Department != null && u.Department.IsActive && u.Department.Organization.IsActive, ct))
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

    private async Task<IReadOnlyList<DashboardProjectFacts>> ReadProjectsAsync(IQueryable<Project> projects, CancellationToken ct)
    {
        var rows = await projects.Select(p => new { p.Id, p.Code, p.Title, p.Status, p.TeamId,
            SemesterId = p.Team.AcademicSemesterId, MemberCount = p.Team.TeamMembers.Count(m => m.LeftAt == null) }).ToArrayAsync(ct);
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
        return rows.Select(p => new DashboardProjectFacts(p.Id, p.Code, p.Title, p.Status, p.TeamId, p.SemesterId,
            reports[p.Id].Count(r => r.Status == "SUBMITTED"),
            new(p.Id, p.Status, p.TeamId, p.MemberCount, milestones[p.Id].ToArray(), tasks[p.Id].ToArray(),
                reports[p.Id].ToArray(), meetings[p.Id].ToArray()))).ToArray();
    }
}
