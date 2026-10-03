using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Projects.Abstractions;
using AIPMS.Application.Features.Projects.DTOs;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Services.Projects;

internal sealed class ProjectGovernanceService(AipmsDbContext db, ICurrentUser currentUser, IProjectAccessService access) : IProjectGovernanceService
{
    public async Task<ProjectGovernanceDto> GetAsync(long projectId, CancellationToken ct = default)
    {
        var actor = currentUser.UserId ?? throw new UnauthorizedException();
        if (!await access.CanAccessAsync(actor, projectId, ct)) throw new ForbiddenException();
        var project = await db.Projects.AsNoTracking().Where(p => p.Id == projectId)
            .Select(p => new { p.Id, p.Status, p.TeamId, p.Team.AcademicSemester.OrganizationId,
                Majors = p.ProjectMajors.Select(m => new { m.MajorId, m.Major.DepartmentId, DepartmentCode = m.Major.Department.Code, DepartmentName = m.Major.Department.Name }).ToList(),
                Supervisors = p.SupervisorAssignments.Where(a => a.EndedAt == null).Select(a => new { a.Id, a.SupervisorProfile.UserId, FullName = a.SupervisorProfile.User.FullName, a.AssignmentType, a.MajorId, a.IsPrimary }).ToList() })
            .SingleOrDefaultAsync(ct) ?? throw new NotFoundException("Project", projectId);
        var assignments = await db.Set<EvaluationAssignment>().AsNoTracking().Where(a => a.ProjectId == projectId)
            .Select(a => new GovernanceEvaluatorDto(a.Id, a.EvaluatorId, a.EvaluationType, a.Scope, a.MajorId, a.StudentId, a.Status)).ToListAsync(ct);
        var final = await db.Set<FinalSubmission>().AsNoTracking().Where(x => x.ProjectId == projectId)
            .Select(x => new { x.Id }).SingleOrDefaultAsync(ct);
        var isAdmin = await db.Users.AnyAsync(u => u.Id == actor && u.Status == "ACTIVE" && u.UserRoleUsers.Any(r => r.Role.Code == "ADMIN"), ct);
        var actorDept = await db.Users.Where(u => u.Id == actor).Select(u => u.DepartmentId).SingleOrDefaultAsync(ct);
        var actorMajors = await db.Users.Where(u => u.Id == actor).Select(u => u.MajorId).ToListAsync(ct);
        var deptRows = project.Majors.GroupBy(x => new { x.DepartmentId, x.DepartmentCode, x.DepartmentName }).OrderBy(x => x.Key.DepartmentId)
            .Select((x, i) => new GovernanceDepartmentDto(x.Key.DepartmentId, x.Key.DepartmentCode, x.Key.DepartmentName, i == 0,
                x.Select(m => m.MajorId).Distinct().OrderBy(x => x).ToArray())).ToArray();
        var hasPrimary = project.Supervisors.Any(x => x.IsPrimary && x.AssignmentType == "PRIMARY");
        var blockers = new List<string>();
        if (!hasPrimary) blockers.Add("PRIMARY_SUPERVISOR_REQUIRED");
        if (project.Status is "ARCHIVED" or "COMPLETED") blockers.Add("PROJECT_READ_ONLY");
        var canEvaluate = assignments.Any(a => a.EvaluatorId == actor && a.Status == "ACTIVE"
            && (a.Scope is "COMMON" or "MAJOR_SPECIFIC" or "INDIVIDUAL"));
        var scopeKind = isAdmin ? "ADMIN_PLATFORM" : actorDept.HasValue ? "DEPARTMENT" : "RESOURCE";
        var actions = new List<string>();
        if (!project.Status.Equals("ARCHIVED", StringComparison.Ordinal)) actions.Add("READ_GOVERNANCE");
        if (isAdmin || deptRows.Any(x => x.DepartmentId == actorDept)) actions.Add("MANAGE_GOVERNANCE");
        if (canEvaluate) actions.Add("READ_ASSIGNED_EVALUATION");
        return new(project.Id, project.Status, deptRows.FirstOrDefault(), deptRows.Skip(1).ToArray(),
            project.Supervisors.Select(x => new GovernanceSupervisorDto(x.Id, x.UserId, x.FullName, x.AssignmentType, x.MajorId, true)).ToArray(),
            assignments, final is null ? null : "LOCKED", final is null ? "NOT_PUBLISHED" : "PENDING", 
            new(project.Status == "ACTIVE" && hasPrimary, canEvaluate, false, project.Status == "ACTIVE", hasPrimary),
            blockers, new(scopeKind, actorDept, actorMajors.Where(x => x.HasValue).Select(x => x!.Value).ToArray(), isAdmin), actions);
    }
}
