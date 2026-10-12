using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Infrastructure.Persistence.Generated;
using Microsoft.EntityFrameworkCore;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.Infrastructure.Services.Projects;

internal sealed record AiScope(long Actor, long ProjectId, string Status, bool Whole, long[] Majors, string Hash);

internal sealed class AiActorScope(AipmsDbContext db, ICurrentUser current)
{
    public async Task<AiScope> Read(long projectId, CancellationToken ct)
    {
        var actor = current.UserId ?? throw new UnauthorizedException();
        var account = await db.Users.AsNoTracking().Where(u => u.Id == actor && u.Status == "ACTIVE"
            && u.Department != null && u.Department.IsActive && u.Department.Organization.IsActive)
            .Select(u => new { u.DepartmentId, Student = u.UserRoleUsers.Any(r => r.Role.Code == "STUDENT"),
                Lecturer = u.UserRoleUsers.Any(r => r.Role.Code == "LECTURER"), Staff = u.UserRoleUsers.Any(r => r.Role.Code == "DEPARTMENT_STAFF") }).SingleOrDefaultAsync(ct) ?? throw new ForbiddenException();
        var project = await db.Projects.AsNoTracking().Where(p => p.Id == projectId).Select(p => new { p.TeamId, p.Status, p.Team.AcademicSemester.OrganizationId }).SingleOrDefaultAsync(ct) ?? throw new NotFoundException("Project", projectId);
        var academic = await ProjectAcademicScopeReader.ReadAsync(db, projectId, ct, true);
        if (academic.Provenance == "FROZEN_REGISTRATION_SNAPSHOT"
            && (await db.Departments.CountAsync(d => academic.DepartmentIds.Contains(d.Id) && d.IsActive && d.Organization.IsActive && d.OrganizationId == project.OrganizationId, ct) != academic.DepartmentIds.Count
                || await db.Majors.CountAsync(m => academic.MajorIds.Contains(m.Id) && m.IsActive, ct) != academic.MajorIds.Count))
            throw new ForbiddenException("Frozen academic scope is inactive or invalid.");
        var membership = account.Student ? await db.TeamMembers.AsNoTracking().Where(m => m.TeamId == project.TeamId && m.UserId == actor && m.LeftAt == null)
            .Select(m => new { m.Id, m.JoinedAt }).SingleOrDefaultAsync(ct) : null;
        var assignments = await db.SupervisorAssignments.AsNoTracking().Where(a => a.ProjectId == projectId && a.EndedAt == null && a.SupervisorProfile.UserId == actor && account.Lecturer)
            .OrderBy(a => a.Id).Select(a => new { a.Id, a.AssignmentType, a.IsPrimary, a.MajorId }).ToArrayAsync(ct);
        var whole = membership is not null || assignments.Any(a => a.AssignmentType == "PRIMARY" && a.IsPrimary)
            || account.Staff && academic.Provenance == "FROZEN_REGISTRATION_SNAPSHOT" && account.DepartmentId == academic.LeadDepartmentId;
        var majors = assignments.Where(a => a.AssignmentType == "DISCIPLINE_MENTOR" && a.MajorId.HasValue).Select(a => a.MajorId!.Value).ToHashSet();
        if (account.Staff && academic.Provenance == "FROZEN_REGISTRATION_SNAPSHOT")
            majors.UnionWith(academic.MajorDepartmentIds!.Where(p => p.Value == account.DepartmentId).Select(p => p.Key));
        majors.IntersectWith(academic.MajorIds);
        if (!whole && (academic.Provenance != "FROZEN_REGISTRATION_SNAPSHOT" || majors.Count == 0)) throw new ForbiddenException();
        var signature = System.Text.Json.JsonSerializer.Serialize(new { actor, projectId, whole, academic.Provenance,
            academic.LeadDepartmentId, academic.MajorDepartmentIds, membership, assignments, Majors = majors.Order().ToArray() });
        return new(actor, projectId, project.Status, whole, majors.ToArray(), Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(signature))));
    }

    public IQueryable<M.Task> Tasks(AiScope scope) => db.Tasks.AsNoTracking().Where(t => t.Milestone.ProjectId == scope.ProjectId
        && (scope.Whole || db.TaskDisciplines.Any(d => d.TaskId == t.Id && scope.Majors.Contains(d.MajorId))));
}
