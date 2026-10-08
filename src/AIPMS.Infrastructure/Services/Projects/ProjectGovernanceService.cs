using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Projects.Abstractions;
using AIPMS.Application.Features.Projects.DTOs;
using AIPMS.Application.Features.Evaluations.Abstractions;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Services.Projects;

internal sealed class ProjectGovernanceService(AipmsDbContext db, ICurrentUser currentUser, IProjectAccessService access,
    IEvaluationSchemeService evaluationScheme) : IProjectGovernanceService
{
    public async Task<ProjectGovernanceDto> GetAsync(long projectId, CancellationToken ct = default)
    {
        var actor = currentUser.UserId ?? throw new UnauthorizedException();
        if (!await access.CanAccessAsync(actor, projectId, ct)) throw new ForbiddenException();
        var project = await db.Projects.AsNoTracking().Where(p => p.Id == projectId)
            .Select(p => new { p.Id, p.Status,
                Supervisors = p.SupervisorAssignments.Where(a => a.EndedAt == null).Select(a => new { a.Id, a.SupervisorProfile.UserId, FullName = a.SupervisorProfile.User.FullName, a.AssignmentType, a.MajorId, a.IsPrimary }).ToList() })
            .SingleOrDefaultAsync(ct) ?? throw new NotFoundException("Project", projectId);
        var assignments = await db.Set<EvaluationAssignment>().AsNoTracking().Where(a => a.ProjectId == projectId)
            .Select(a => new GovernanceEvaluatorDto(a.Id, a.EvaluatorId, a.EvaluationType, a.Scope, a.MajorId, a.StudentId, a.Status)).ToListAsync(ct);
        var final = await db.Set<FinalSubmission>().AsNoTracking().Where(x => x.ProjectId == projectId)
            .Select(x => new { x.Id }).SingleOrDefaultAsync(ct);
        var projectResultPublished = await db.Set<ProjectResult>().AsNoTracking().AnyAsync(x => x.ProjectId == projectId, ct);
        var publishedScheme = await db.Set<EvaluationScheme>().AsNoTracking().AnyAsync(x => x.ProjectId == projectId && x.Status == "PUBLISHED", ct);
        var frozenScope = await ProjectAcademicScopeReader.ReadAsync(db, projectId, ct, requireFrozenScope: true);
        var isAdmin = await db.Users.AnyAsync(u => u.Id == actor && u.Status == "ACTIVE" && u.UserRoleUsers.Any(r => r.Role.Code == "ADMIN"), ct);
        var actorDept = await db.Users.Where(u => u.Id == actor).Select(u => u.DepartmentId).SingleOrDefaultAsync(ct);
        var actorMajors = await db.Users.Where(u => u.Id == actor).Select(u => u.MajorId).ToListAsync(ct);
        var frozenMajorDepartments = frozenScope.MajorDepartmentIds ?? new Dictionary<long, long>();
        var frozenDepartments = await db.Departments.AsNoTracking()
            .Where(d => frozenScope.DepartmentIds.Contains(d.Id))
            .Select(d => new { d.Id, d.Code, d.Name }).ToListAsync(ct);
        var scopeValid = frozenScope.LeadDepartmentId is long lead
            && frozenScope.DepartmentIds.Count > 0
            && frozenScope.DepartmentIds.Contains(lead)
            && frozenScope.MajorIds.Count > 0
            && frozenDepartments.Count == frozenScope.DepartmentIds.Distinct().Count()
            && frozenScope.MajorIds.All(id => frozenMajorDepartments.TryGetValue(id, out var dept) && frozenScope.DepartmentIds.Contains(dept));
        var scopeDepartments = frozenDepartments
            .Select(x => new GovernanceDepartmentDto(x.Id, x.Code, x.Name,
                scopeValid && x.Id == frozenScope.LeadDepartmentId,
                frozenMajorDepartments.Where(m => m.Value == x.Id).Select(m => m.Key).OrderBy(id => id).ToArray()))
            .OrderByDescending(x => x.IsLead).ThenBy(x => x.DepartmentId).ToArray();
        var hasPrimary = project.Supervisors.Any(x => x.IsPrimary && x.AssignmentType == "PRIMARY");
        var blockers = new List<string>();
        if (!hasPrimary) blockers.Add("PRIMARY_SUPERVISOR_REQUIRED");
        if (!scopeValid) blockers.Add("ACADEMIC_SCOPE_UNKNOWN");
        if (project.Status is "ARCHIVED" or "COMPLETED") blockers.Add("PROJECT_READ_ONLY");
        var canEvaluate = assignments.Any(a => a.EvaluatorId == actor && a.Status == "ACTIVE"
            && (a.Scope is "COMMON" or "MAJOR_SPECIFIC" or "INDIVIDUAL"));
        var scopeKind = isAdmin ? "ADMIN_PLATFORM" : actorDept.HasValue ? "DEPARTMENT" : "RESOURCE";
        var actions = new List<string>();
        actions.Add("READ_GOVERNANCE");
        var isStaff = await db.Users.AnyAsync(u => u.Id == actor && u.Status == "ACTIVE"
            && u.Department != null && u.Department.IsActive && u.Department.Organization.IsActive
            && u.UserRoleUsers.Any(r => r.Role.Code == "DEPARTMENT_STAFF"), ct);
        if (scopeValid && project.Status is not ("ARCHIVED" or "COMPLETED")
            && isStaff && scopeDepartments.Any(x => x.DepartmentId == actorDept)) actions.Add("MANAGE_GOVERNANCE");
        if (canEvaluate) actions.Add("READ_ASSIGNED_EVALUATION");
        var finalStatus = final is null ? null : "LOCKED";
        var resultStatus = projectResultPublished ? "PUBLISHED" : final is null ? "NOT_PUBLISHED" : "PENDING";
        var canPublish = false;
        if ((isAdmin || isStaff) && scopeValid && project.Status == "FINAL_SUBMISSION" && final is not null
            && publishedScheme && !projectResultPublished)
        {
            try
            {
                var preview = await evaluationScheme.PreviewAsync(projectId, null, ct);
                canPublish = preview.CanPublish;
                blockers.AddRange(preview.Blockers);
            }
            catch (ConflictException) { blockers.Add("RESULT_NOT_READY"); }
            catch (ForbiddenException) { blockers.Add("RESULT_PUBLICATION_FORBIDDEN"); }
        }
        return new(project.Id, project.Status, scopeDepartments.FirstOrDefault(x => x.IsLead), scopeDepartments.Where(x => !x.IsLead).ToArray(),
            project.Supervisors.Select(x => new GovernanceSupervisorDto(x.Id, x.UserId, x.FullName, x.AssignmentType, x.MajorId, true)).ToArray(),
            assignments, finalStatus, resultStatus,
            new(project.Status == "ACTIVE" && hasPrimary && scopeValid,
                canEvaluate && publishedScheme && scopeValid && project.Status == "FINAL_SUBMISSION", canPublish, project.Status == "ACTIVE", hasPrimary),
            blockers, new(scopeKind, actorDept, actorMajors.Where(x => x.HasValue).Select(x => x!.Value).ToArray(), isAdmin), actions,
            scopeValid ? frozenScope.Provenance : "UNKNOWN");
    }
}
