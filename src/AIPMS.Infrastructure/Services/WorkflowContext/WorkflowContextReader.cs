using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.Projects.Abstractions;
using AIPMS.Application.Features.Teams.Abstractions;
using AIPMS.Application.Features.Supervisors.Abstractions;
using AIPMS.Application.Features.WorkflowContext.Abstractions;
using AIPMS.Application.Features.WorkflowContext.DTOs;
using AIPMS.Infrastructure.Persistence.Generated;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;

namespace AIPMS.Infrastructure.Services.WorkflowContext;

internal sealed partial class WorkflowContextReader(AipmsDbContext db, ITeamRepository teams,
    ITeamFormationPolicyProvider policies, IProjectRepository projects, ISupervisorCandidateRepository candidateReader,
    IProjectAccessService projectAccess, TimeProvider clock) : IWorkflowContextReader
{
    private sealed record Actor(WorkflowUserDto User, UserAcademicContextDto Academic)
    {
        public bool HasRole(string role) => User.EffectiveRoles.Contains(role);
        public bool Admin => HasRole(AppRoles.Admin);
        public bool Student => HasRole(AppRoles.Student);
        public bool Staff => HasRole(AppRoles.DepartmentStaff);
        public bool Lecturer => HasRole(AppRoles.Lecturer);
        public long Id => User.Id;
    }

    private async Task<Actor> ReadActorAsync(long userId, IReadOnlyCollection<string> tokenRoles, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => new
        {
            u.Id, u.Email, u.FullName, u.Status, u.StudentCode, u.EmployeeCode,
            Department = u.Department == null ? null : new AcademicReferenceDto(u.Department.Id, u.Department.Code, u.Department.Name, u.Department.IsActive),
            Organization = u.Department == null ? null : new AcademicReferenceDto(u.Department.Organization.Id,
                u.Department.Organization.Code, u.Department.Organization.Name, u.Department.Organization.IsActive),
            Major = u.Major == null ? null : new AcademicReferenceDto(u.Major.Id, u.Major.Code, u.Major.Name, u.Major.IsActive),
            MajorDepartmentId = u.Major == null ? (long?)null : u.Major.DepartmentId
        }).SingleOrDefaultAsync(ct) ?? throw new UnauthorizedException();
        if (user.Status != "ACTIVE") throw new UnauthorizedException();
        var roles = await db.UserRoles.AsNoTracking().Where(r => r.UserId == userId).Select(r => r.Role.Code).Distinct().OrderBy(c => c).ToArrayAsync(ct);
        var grants = await db.UserRoles.AsNoTracking().Where(r => r.UserId == userId)
            .SelectMany(r => r.Role.RolePermissions.Select(p => p.Permission.Code)).Distinct().OrderBy(c => c).ToArrayAsync(ct);
        // Write endpoints use both claims and persisted roles; advertise only privileges present in both.
        var effective = roles.Intersect(tokenRoles, StringComparer.Ordinal).OrderBy(c => c, StringComparer.Ordinal).ToArray();
        var issues = new List<string>();
        var activeDepartment = user.Department is { IsActive: true } && user.Organization is { IsActive: true };
        var eligibleStudent = activeDepartment && user.Major is { IsActive: true } && user.MajorDepartmentId == user.Department!.Id;
        if (user.Department is null) issues.Add("DEPARTMENT_SCOPE_MISSING");
        else if (!activeDepartment) issues.Add("ACADEMIC_SCOPE_INACTIVE");
        if (roles.Contains(AppRoles.Student) && !eligibleStudent) issues.Add("STUDENT_PROFILE_INELIGIBLE");
        return new(new(user.Id, user.Email, user.FullName, user.Status, user.StudentCode, user.EmployeeCode,
                roles, grants, effective, !roles.ToHashSet(StringComparer.Ordinal).SetEquals(tokenRoles)),
            new(user.Organization, user.Department, user.Major, activeDepartment, eligibleStudent, issues));
    }

    private static WorkflowActionDto Action(string code, params (bool Pass, string Reason)[] gates)
    {
        var reasons = gates.Where(g => !g.Pass).Select(g => g.Reason).Distinct().ToArray();
        return new(code, reasons.Length == 0, reasons);
    }

    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    public async Task<UserWorkflowContextDto> GetCurrentAsync(long userId, IReadOnlyCollection<string> tokenRoles,
        long? semesterId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var actor = await ReadActorAsync(userId, tokenRoles, ct);
        var organizationId = actor.Academic.Organization?.Id;
        var semesterQuery = db.AcademicSemesters.AsNoTracking()
            .Where(s => s.OrganizationId == organizationId || actor.Admin);
        var current = await semesterQuery.Where(s => s.Status == "ACTIVE" && s.Organization.IsActive
                && s.StartDate <= today && today <= s.EndDate)
            .OrderBy(s => s.StartDate).ThenBy(s => s.Id)
            .Select(s => new WorkflowSemesterDto(s.Id, s.OrganizationId, s.Code, s.Name, s.Status, s.StartDate, s.EndDate, true)).ToArrayAsync(ct);
        WorkflowSemesterDto? selected = null;
        if (semesterId.HasValue)
        {
            selected = await semesterQuery.Where(s => s.Id == semesterId).Select(s => new WorkflowSemesterDto(s.Id,
                s.OrganizationId, s.Code, s.Name, s.Status, s.StartDate, s.EndDate,
                s.Status == "ACTIVE" && s.Organization.IsActive && s.StartDate <= today && today <= s.EndDate)).SingleOrDefaultAsync(ct)
                ?? throw new NotFoundException("Academic semester", semesterId.Value);
        }
        else if (current.Length == 1) selected = current[0];
        var selectionIssues = selected is null ? new[] { current.Length > 1 ? "SEMESTER_SELECTION_REQUIRED" : "NO_CURRENT_SEMESTER" }
            : selected.IsCurrent ? Array.Empty<string>() : ["SEMESTER_NOT_CURRENT"];
        var periodRows = selected is null ? [] : await db.ProjectPeriods.AsNoTracking().Where(p => p.AcademicSemesterId == selected.Id)
            .OrderBy(p => p.StartAt).ThenBy(p => p.Id)
            .Select(p => new { p.Id, p.Code, p.Name, p.PeriodType, p.Status, p.StartAt, p.EndAt }).ToArrayAsync(ct);
        var periods = periodRows.Select(p => new WorkflowPeriodDto(p.Id, p.Code, p.Name, p.PeriodType, p.Status,
            Utc(p.StartAt), Utc(p.EndAt), selected!.IsCurrent && p.Status == "ACTIVE" && p.StartAt <= now.UtcDateTime && now.UtcDateTime < p.EndAt)).ToArray();
        WorkflowTeamSummaryDto? currentTeam = null;
        if (selected is not null && actor.Student)
        {
            var teamId = await teams.GetCurrentTeamIdAsync(selected.Id, userId, ct);
            if (teamId.HasValue)
            {
                var team = await teams.GetAsync(teamId.Value, ct);
                if (team is not null)
                {
                    var project = await db.Projects.AsNoTracking().Where(p => p.TeamId == team.Id).OrderByDescending(p => p.Id)
                        .Select(p => new { p.Id, p.Status }).FirstOrDefaultAsync(ct);
                    currentTeam = new(team.Id, team.Code, team.Name, team.Status, team.Members.Any(m => m.UserId == userId && m.IsLeader), project?.Id, project?.Status);
                }
            }
        }
        var window = selected is null ? null : await teams.GetOpenWindowAsync(selected.Id, now.UtcDateTime, ct);
        var policy = window is null ? null : await policies.GetAsync(window.PeriodId, ct);
        var actions = new[]
        {
            Action("view_dashboard"), Action("view_projects"), Action("view_notifications"), Action("edit_profile"), Action("change_password"),
            Action("manage_accounts", (actor.Admin, "ADMIN_REQUIRED")),
            Action("manage_academic_structure", (actor.Admin || actor.Staff && actor.Academic.HasActiveDepartmentScope, "ACADEMIC_MANAGER_REQUIRED")),
            Action("manage_semesters", (actor.Admin, "ADMIN_REQUIRED")),
            Action("view_review_queue", (actor.Admin || actor.Staff && actor.Academic.HasActiveDepartmentScope, "REVIEWER_SCOPE_REQUIRED")),
            Action("view_invitations", (actor.Student, "STUDENT_ROLE_REQUIRED")),
            Action("view_team", (actor.Student, "STUDENT_ROLE_REQUIRED"), (currentTeam is not null, "NO_CURRENT_TEAM")),
            Action("view_supervisor_inbox", (actor.Lecturer, "LECTURER_ROLE_REQUIRED"), (actor.Academic.HasActiveDepartmentScope, "ACADEMIC_SCOPE_INACTIVE")),
            Action("manage_own_supervisor_profile", (actor.Lecturer, "LECTURER_ROLE_REQUIRED"), (actor.Academic.HasActiveDepartmentScope, "ACADEMIC_SCOPE_INACTIVE")),
            Action("create_team", (actor.Student, "STUDENT_ROLE_REQUIRED"), (actor.Academic.HasEligibleStudentProfile, "STUDENT_PROFILE_INELIGIBLE"),
                (selected is not null, "SEMESTER_SELECTION_REQUIRED"), (selected?.OrganizationId == organizationId, "ORGANIZATION_MISMATCH"),
                (currentTeam is null, "TEAM_ALREADY_EXISTS"), (window is not null, "REGISTRATION_WINDOW_UNAVAILABLE"),
                (policy is { IsValid: true }, "TEAM_POLICY_UNCONFIGURED"))
        };
        return new(now, actor.User, actor.Academic, current, selected, selectionIssues, periods, currentTeam, actions);
    }
}
