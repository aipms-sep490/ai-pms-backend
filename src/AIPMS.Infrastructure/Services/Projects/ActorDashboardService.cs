using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Dashboards.Abstractions;
using AIPMS.Application.Features.Dashboards.DTOs;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Services.Projects;

internal sealed class ActorDashboardService(AipmsDbContext db, ICurrentUser current, TimeProvider clock) : IActorDashboardService
{
    private sealed class Candidate
    {
        public long Id { get; set; }
        public long OrganizationId { get; set; }
        public string SnapshotJson { get; set; } = "";
        public bool Student { get; set; }
        public bool Primary { get; set; }
    }
    public async Task<ActorDashboardDto> ReadAsync(long? semesterId, CancellationToken ct)
    {
        if (semesterId <= 0) throw new ArgumentException("Invalid semester.");
        var actor = current.UserId ?? throw new UnauthorizedException();
        var now = clock.GetUtcNow().UtcDateTime;
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var user = await db.Users.Where(u => u.Id == actor && u.Status == "ACTIVE").Select(u => new { u.DepartmentId,
            Academic = u.Department != null && u.Department.IsActive && u.Department.Organization.IsActive,
            Student = u.UserRoleUsers.Any(r => r.Role.Code == "STUDENT"), Staff = u.UserRoleUsers.Any(r => r.Role.Code == "DEPARTMENT_STAFF"),
            Lecturer = u.UserRoleUsers.Any(r => r.Role.Code == "LECTURER"), Admin = u.UserRoleUsers.Any(r => r.Role.Code == "ADMIN") }).SingleOrDefaultAsync(ct) ?? throw new ForbiddenException();
        var unread = await db.NotificationRecipients.LongCountAsync(n => n.UserId == actor && !n.IsRead, ct);
        if (user.Admin && !user.Student && !user.Lecturer && !user.Staff)
        {
            var active = await db.Users.LongCountAsync(u => u.Status == "ACTIVE", ct); await tx.CommitAsync(ct);
            return new(now, 0, 0, 0, 0, unread, active, "PLATFORM", true);
        }
        if (!user.Academic) throw new ForbiddenException();
        var candidates = await db.Database.SqlQuery<Candidate>($"""
            SELECT TOP(1001) p.id Id,sem.organization_id OrganizationId,snap.snapshot_json SnapshotJson,
             CAST(CASE WHEN {user.Student}=1 AND EXISTS(SELECT 1 FROM dbo.team_members m WHERE m.team_id=p.team_id AND m.user_id={actor} AND m.left_at IS NULL) THEN 1 ELSE 0 END AS bit) Student,
             CAST(CASE WHEN {user.Lecturer}=1 AND EXISTS(SELECT 1 FROM dbo.supervisor_assignments a JOIN dbo.supervisor_profiles sp ON sp.id=a.supervisor_profile_id WHERE a.project_id=p.id AND a.assignment_type='PRIMARY' AND a.is_primary=1 AND a.ended_at IS NULL AND sp.user_id={actor}) THEN 1 ELSE 0 END AS bit) [Primary]
            FROM dbo.projects p JOIN dbo.teams t ON t.id=p.team_id JOIN dbo.academic_semesters sem ON sem.id=t.academic_semester_id
            CROSS APPLY(SELECT TOP(1) s.snapshot_json FROM dbo.project_registration_snapshots s WHERE s.project_id=p.id ORDER BY s.id DESC) snap
            WHERE ({semesterId} IS NULL OR t.academic_semester_id={semesterId}) AND (
             ({user.Student}=1 AND EXISTS(SELECT 1 FROM dbo.team_members m WHERE m.team_id=p.team_id AND m.user_id={actor} AND m.left_at IS NULL))
             OR ({user.Lecturer}=1 AND EXISTS(SELECT 1 FROM dbo.supervisor_assignments a JOIN dbo.supervisor_profiles sp ON sp.id=a.supervisor_profile_id WHERE a.project_id=p.id AND a.ended_at IS NULL AND sp.user_id={actor}))
             OR ({user.Staff}=1 AND EXISTS(SELECT 1 FROM OPENJSON(CASE WHEN ISJSON(snap.snapshot_json)=1 THEN snap.snapshot_json ELSE '[]' END,'$.DepartmentIds') d WHERE TRY_CONVERT(bigint,d.value)={user.DepartmentId})))
            ORDER BY p.id
            """).ToArrayAsync(ct);
        if (candidates.Length > 1000) throw new ArgumentException("Dashboard exceeds 1000 projects; filter by semester.");
        var ids = candidates.Select(c => c.Id).ToArray();
        var registrations = candidates.Select(c => (Row: c, Scope: ProjectAcademicScopeReader.ParseEvidence(c.SnapshotJson))).Where(x => x.Scope is not null).ToArray();
        var departmentIds = registrations.SelectMany(x => x.Scope!.DepartmentIds).Distinct().ToArray();
        var activeDepartments = await db.Departments.Where(d => departmentIds.Contains(d.Id) && d.IsActive && d.Organization.IsActive).ToDictionaryAsync(d => d.Id, d => d.OrganizationId, ct);
        var majorIds = registrations.SelectMany(x => x.Scope!.MajorDepartmentIds!.Keys).Distinct().ToArray();
        var activeMajors = (await db.Majors.Where(m => majorIds.Contains(m.Id) && m.IsActive).Select(m => m.Id).ToArrayAsync(ct)).ToHashSet();
        var mentors = await db.SupervisorAssignments.Where(a => ids.Contains(a.ProjectId) && user.Lecturer && a.SupervisorProfile.UserId == actor && a.EndedAt == null && a.AssignmentType == "DISCIPLINE_MENTOR" && a.MajorId.HasValue)
            .Select(a => new { a.ProjectId, Major = a.MajorId!.Value }).ToArrayAsync(ct);
        var whole = new HashSet<long>(); var majorKeys = new HashSet<string>(); var projects = new HashSet<long>();
        foreach (var (row, registration) in registrations)
        {
            var scope = registration!;
            if (scope.DepartmentIds.Any(d => !activeDepartments.TryGetValue(d, out var organization) || organization != row.OrganizationId)
                || scope.MajorDepartmentIds!.Keys.Any(m => !activeMajors.Contains(m))) continue;
            if (row.Student && scope.Members.Any(m => m.UserId == actor) || row.Primary || user.Staff && scope.Scope.LeadDepartmentId == user.DepartmentId)
            { whole.Add(row.Id); projects.Add(row.Id); }
            foreach (var pair in scope.MajorDepartmentIds!)
                if (user.Staff && pair.Value == user.DepartmentId || mentors.Any(m => m.ProjectId == row.Id && m.Major == pair.Key))
                { majorKeys.Add($"{row.Id}:{pair.Key}"); projects.Add(row.Id); }
        }
        var full = whole.ToArray(); var keys = majorKeys.ToArray();
        var tasks = db.Tasks.Where(t => (full.Contains(t.Milestone.ProjectId) || db.TaskDisciplines.Any(d => d.TaskId == t.Id && keys.Contains(t.Milestone.ProjectId.ToString() + ":" + d.MajorId.ToString())))
            && t.Status != "DONE" && t.Status != "CANCELLED");
        var open = await tasks.LongCountAsync(ct); var overdue = await tasks.LongCountAsync(t => t.DueAt < now, ct);
        var evaluations = await db.Set<EvaluationAssignment>().LongCountAsync(a => user.Lecturer && a.EvaluatorId == actor && a.Status == "ACTIVE"
            && a.DepartmentId == user.DepartmentId && a.ScopeSnapshotJson != null && a.ComponentId != null && a.PolicyVersionId != null
            && (a.Scope == "COMMON" && a.MajorId == null && a.StudentId == null
                || a.Scope == "MAJOR_SPECIFIC" && a.MajorId != null && a.StudentId == null
                || a.Scope == "INDIVIDUAL" && a.MajorId != null && a.StudentId != null)
            && db.Set<EvaluationSchemeComponent>().Any(c => c.Id == a.ComponentId && c.Scope == a.Scope && c.MajorId == a.MajorId
                && c.RubricId == a.RubricId && c.Stage == a.Stage
                && db.Set<EvaluationScheme>().Any(s => s.Id == c.SchemeId && s.ProjectId == a.ProjectId && s.Status == "PUBLISHED"
                    && s.ProjectPeriodId == a.ProjectPeriodId && s.PolicyVersionId == a.PolicyVersionId))
            && (!semesterId.HasValue || db.Projects.Any(p => p.Id == a.ProjectId && p.Team.AcademicSemesterId == semesterId))
            && !db.Set<EvaluationDraftState>().Any(d => d.AssignmentId == a.Id && db.Set<EvaluationFinalization>().Any(f => f.EvaluationId == d.EvaluationId)), ct);
        await tx.CommitAsync(ct);
        return new(now, projects.Count, open, overdue, evaluations, unread, null, "ACTOR_ASSIGNMENTS", true);
    }
}
