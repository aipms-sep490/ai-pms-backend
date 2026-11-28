using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Security;
using AIPMS.Infrastructure.Persistence.Generated;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Services.Projects;

internal sealed class ProjectScopeResolver(AipmsDbContext db) : IProjectScopeResolver
{
    public async Task<ProjectActorScope?> ResolveAsync(long userId, long projectId, CancellationToken ct = default)
    {
        var actor = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId && u.Status == "ACTIVE")
            .Select(u => new
            {
                u.DepartmentId,
                HasScope = u.Department != null && u.Department.IsActive && u.Department.Organization.IsActive,
                Roles = u.UserRoleUsers.Select(r => r.Role.Code).ToArray()
            })
            .SingleOrDefaultAsync(ct);

        if (actor is null) return null;
        if (!await db.Projects.AsNoTracking().AnyAsync(p => p.Id == projectId, ct)) return null;

        if (actor.Roles.Contains(AppRoles.Admin, StringComparer.Ordinal))
            return new ProjectActorScope(true, false, false, Array.Empty<long>(), true);

        if (!actor.HasScope) return null;

        var projectScope = await ProjectAcademicScopeReader.ReadAsync(db, projectId, ct, requireFrozenScope: false);

        if (actor.Roles.Contains(AppRoles.DepartmentStaff, StringComparer.Ordinal) && actor.DepartmentId is long deptId)
        {
            if (projectScope.LeadDepartmentId == deptId || projectScope.DepartmentIds.Contains(deptId))
                return new ProjectActorScope(true, false, false, projectScope.MajorIds, true);
        }

        if (actor.Roles.Contains(AppRoles.Student, StringComparer.Ordinal))
        {
            var isMember = await db.Projects.AsNoTracking()
                .AnyAsync(p => p.Id == projectId && p.Team.TeamMembers.Any(m => m.UserId == userId && m.LeftAt == null), ct);
            if (isMember)
                return new ProjectActorScope(true, false, false, projectScope.MajorIds, true);
        }

        if (actor.Roles.Contains(AppRoles.Lecturer, StringComparer.Ordinal))
        {
            var assignments = await db.SupervisorAssignments.AsNoTracking()
                .Where(a => a.ProjectId == projectId && a.EndedAt == null && a.SupervisorProfile.UserId == userId)
                .Select(a => new { a.IsPrimary, a.AssignmentType, a.MajorId })
                .ToListAsync(ct);

            if (assignments.Any(a => a.IsPrimary && a.AssignmentType == "PRIMARY"))
                return new ProjectActorScope(true, true, false, projectScope.MajorIds, true);

            var mentorMajors = assignments
                .Where(a => a.AssignmentType == "DISCIPLINE_MENTOR" && a.MajorId.HasValue)
                .Select(a => a.MajorId!.Value)
                .Distinct()
                .ToArray();

            if (mentorMajors.Length > 0)
            {
                // TK-W2-05: Discipline Mentor accesses ONLY their assigned major(s) + COMMON/INTEGRATION.
                return new ProjectActorScope(false, false, true, mentorMajors, true);
            }
        }

        return null;
    }
}
