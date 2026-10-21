using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.ActionAggregators;
using AIPMS.Application.Features.ActionAggregators.Abstractions;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Services.Projects;

internal sealed class ActionAggregatorService(AipmsDbContext db, ICurrentUser current, TimeProvider clock)
    : IActionAggregatorService
{
    private long Actor => current.UserId ?? throw new UnauthorizedException();
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task<StudentNextActionsDto> StudentNextActions(CancellationToken ct)
    {
        Require(AppRoles.Student);
        var project = await db.Projects.AsNoTracking()
            .Where(p => p.Team.TeamMembers.Any(m => m.UserId == Actor && m.LeftAt == null))
            .OrderByDescending(p => p.Id)
            .Select(p => new { p.Id, p.Code, p.Title, p.Status })
            .FirstOrDefaultAsync(ct);
        if (project is null) return new(Now, null, []);
        var items = new List<ActionAggregatorItem>();
        var now = Now;
        var tasks = await db.Tasks.AsNoTracking()
            .Where(t => t.Milestone.ProjectId == project.Id && t.TaskAssignees.Any(a => a.UserId == Actor)
                && t.Status != "DONE" && t.Status != "CANCELLED")
            .OrderBy(t => t.DueAt).Take(20)
            .Select(t => new { t.Id, t.Title, t.Status, t.DueAt }).ToArrayAsync(ct);
        items.AddRange(tasks.Select(t => new ActionAggregatorItem("TASK", t.Id, project.Id, t.Title, t.Status,
            t.DueAt < now ? "HIGH" : "NORMAL", t.DueAt, $"/project/tasks/{t.Id}")));
        var reports = await db.ProgressReports.AsNoTracking().Where(r => r.ProjectId == project.Id && r.SubmittedBy == Actor && r.Status == "DRAFT")
            .OrderByDescending(r => r.UpdatedAt).Take(10).Select(r => new { r.Id, r.ReportType }).ToArrayAsync(ct);
        items.AddRange(reports.Select(r => new ActionAggregatorItem("PROGRESS_REPORT", r.Id, project.Id,
            $"Hoàn tất báo cáo {r.ReportType}", r.ReportType, "NORMAL", null, $"/project/progress-reports/{r.Id}")));
        var gates = await db.ProjectGateInstances.AsNoTracking().Where(g => g.ProjectId == project.Id && g.Status != "PASSED")
            .OrderBy(g => g.Sequence).Take(10).Select(g => new { g.Id, g.Code, g.Title, g.Status }).ToArrayAsync(ct);
        items.AddRange(gates.Select(g => new ActionAggregatorItem("REVIEW_GATE", g.Id, project.Id, g.Title, g.Status,
            g.Status == "BLOCKED" ? "HIGH" : "NORMAL", null, $"/project/review-gates/{g.Code}")));
        return new(Now, project.Id, items.OrderByDescending(x => x.Severity == "HIGH").ThenBy(x => x.DueAt).ToArray());
    }

    public async Task<SupervisorTriageQueueDto> SupervisorTriage(CancellationToken ct)
    {
        Require(AppRoles.Lecturer);
        var projects = db.SupervisorAssignments.AsNoTracking().Where(a => a.SupervisorProfile.UserId == Actor && a.EndedAt == null)
            .Select(a => a.ProjectId).Distinct();
        var items = new List<ActionAggregatorItem>();
        var reports = await db.ProgressReports.AsNoTracking().Where(r => projects.Contains(r.ProjectId) && r.Status == "SUBMITTED")
            .OrderBy(r => r.SubmittedAt).Take(50).Select(r => new { r.Id, r.ProjectId, r.ReportType, r.SubmittedAt }).ToArrayAsync(ct);
        items.AddRange(reports.Select(r => new ActionAggregatorItem("PROGRESS_REVIEW", r.Id, r.ProjectId,
            $"Duyệt báo cáo {r.ReportType}", "SUBMITTED", "HIGH", r.SubmittedAt, $"/supervisor/progress-reports/{r.Id}")));
        var requests = await db.SupervisorRequests.AsNoTracking().Where(r => projects.Contains(r.ProjectId) && r.Status == "PENDING")
            .OrderBy(r => r.RequestedAt).Take(50).Select(r => new { r.Id, r.ProjectId, r.AssignmentType, r.RequestedAt }).ToArrayAsync(ct);
        items.AddRange(requests.Select(r => new ActionAggregatorItem("SUPERVISOR_REQUEST", r.Id, r.ProjectId,
            $"Xử lý đề nghị {r.AssignmentType}", "PENDING", "NORMAL", r.RequestedAt, $"/supervisor/requests/{r.Id}")));
        var meetings = await db.Meetings.AsNoTracking().Where(m => projects.Contains(m.ProjectId) && m.Status == "COMPLETED" && m.MeetingNotes == null)
            .OrderBy(m => m.StartAt).Take(50).Select(m => new { m.Id, m.ProjectId, m.Title, m.StartAt }).ToArrayAsync(ct);
        items.AddRange(meetings.Select(m => new ActionAggregatorItem("MEETING_SIGN_OFF", m.Id, m.ProjectId,
            $"Hoàn tất biên bản {m.Title}", "COMPLETED", "NORMAL", m.StartAt, $"/supervisor/meetings/{m.Id}")));
        return new(Now, items.OrderByDescending(x => x.Severity == "HIGH").ThenBy(x => x.DueAt).ToArray());
    }

    public async Task<DepartmentUrgentActionsDto> DepartmentUrgent(long departmentId, CancellationToken ct)
    {
        Require(AppRoles.DepartmentStaff);
        var scope = await db.Users.AsNoTracking().Where(u => u.Id == Actor && u.DepartmentId == departmentId && u.Status == "ACTIVE").AnyAsync(ct);
        if (!scope && !current.Roles.Contains(AppRoles.Admin)) throw new ForbiddenException("Department scope is required.");
        var projectIds = db.ProjectMajors.AsNoTracking().Where(x => x.Major.DepartmentId == departmentId).Select(x => x.ProjectId).Distinct();
        var items = new List<ActionAggregatorItem>();
        var unassigned = await db.ProjectGateInstances.AsNoTracking().Where(g => projectIds.Contains(g.ProjectId) && g.Status != "PASSED"
            && !g.Assignments.Any(a => a.RevokedAt == null && a.Status != "REVOKED"))
            .OrderBy(g => g.Sequence).Take(50).Select(g => new { g.Id, g.ProjectId, g.Title, g.Status }).ToArrayAsync(ct);
        items.AddRange(unassigned.Select(g => new ActionAggregatorItem("REVIEWER_ASSIGNMENT", g.Id, g.ProjectId,
            $"Chưa phân reviewer: {g.Title}", g.Status, "HIGH", null, $"/department/projects/{g.ProjectId}/review-gates")));
        var appeals = await db.Set<ResultAppealRequest>().AsNoTracking().Where(a => projectIds.Contains(a.ProjectId) && (a.Status == "PENDING" || a.Status == "UNDER_REVIEW"))
            .OrderBy(a => a.RequestedAt).Take(50).Select(a => new { a.Id, a.ProjectId, a.Status, a.RequestedAt }).ToArrayAsync(ct);
        items.AddRange(appeals.Select(a => new ActionAggregatorItem("APPEAL", a.Id, a.ProjectId,
            "Phúc khảo/đính chính cần xử lý", a.Status, "HIGH", a.RequestedAt, $"/department/projects/{a.ProjectId}/appeals/{a.Id}")));
        return new(Now, departmentId, items.OrderByDescending(x => x.Severity == "HIGH").ThenBy(x => x.DueAt).ToArray());
    }

    private void Require(string role)
    {
        if (!current.IsAuthenticated) throw new UnauthorizedException();
        if (!current.Roles.Contains(role) && !current.Roles.Contains(AppRoles.Admin)) throw new ForbiddenException();
    }
}
