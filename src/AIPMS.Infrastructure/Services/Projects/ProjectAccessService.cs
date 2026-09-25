using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Security;
using AIPMS.Infrastructure.Persistence.Generated;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Services.Projects;

internal sealed class ProjectAccessService(AipmsDbContext context) : IProjectAccessService
{
    public async Task<bool> CanAccessAsync(long userId, long projectId, CancellationToken cancellationToken = default)
    {
        var actor = await context.Users.AsNoTracking().Where(u => u.Id == userId && u.Status == "ACTIVE")
            .Select(u => new { u.DepartmentId,
                HasScope = u.Department != null && u.Department.IsActive && u.Department.Organization.IsActive,
                Roles = u.UserRoleUsers.Select(r => r.Role.Code).ToArray() }).SingleOrDefaultAsync(cancellationToken);
        if (actor is null || !await context.Projects.AnyAsync(p => p.Id == projectId, cancellationToken)) return false;
        if (actor.Roles.Contains(AppRoles.Admin)) return true;
        if (!actor.HasScope) return false;
        if (actor.Roles.Contains(AppRoles.DepartmentStaff) && actor.DepartmentId is long departmentId)
        {
            var scope = await ProjectAcademicScopeReader.ReadAsync(context, projectId, cancellationToken);
            if (scope.DepartmentIds.Contains(departmentId)) return true;
        }
        return await context.Projects.AsNoTracking().AnyAsync(p => p.Id == projectId
            && ((actor.Roles.Contains(AppRoles.Student)
                    && p.Team.TeamMembers.Any(m => m.UserId == userId && m.LeftAt == null))
                || (actor.Roles.Contains(AppRoles.Lecturer)
                    && p.SupervisorAssignments.Any(a => a.EndedAt == null && a.SupervisorProfile.UserId == userId))),
            cancellationToken);
    }
}
