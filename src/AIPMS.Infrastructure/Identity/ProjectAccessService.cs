using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Security;
using AIPMS.Infrastructure.Persistence.Generated;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Identity;

internal sealed class ProjectAccessService(AipmsDbContext context) : IProjectAccessService
{
    public async Task<bool> CanAccessAsync(
        long userId,
        long projectId,
        CancellationToken cancellationToken = default)
    {
        // Load user roles
        var roles = await context.UserRoles
            .AsNoTracking()
            .Where(ur => ur.UserId == userId)
            .Select(ur => ur.Role.Code)
            .ToListAsync(cancellationToken);

        if (roles.Count == 0) return false;

        // Admin → platform-wide access
        if (roles.Contains(AppRoles.Admin)) return true;

        // DepartmentStaff → only projects whose majors belong to the staff's department
        if (roles.Contains(AppRoles.DepartmentStaff))
        {
            var departmentId = await context.Users
                .AsNoTracking()
                .Where(u => u.Id == userId)
                .Select(u => u.DepartmentId)
                .FirstOrDefaultAsync(cancellationToken);

            if (departmentId is null) return false;

            return await context.ProjectMajors
                .AsNoTracking()
                .AnyAsync(
                    pm => pm.ProjectId == projectId
                          && pm.Major.DepartmentId == departmentId.Value,
                    cancellationToken);
        }

        // Students / Supervisors → must be active team member or active supervisor
        return await context.Projects
            .AsNoTracking()
            .AnyAsync(
                p => p.Id == projectId
                     && (p.Team.TeamMembers.Any(
                             m => m.UserId == userId && m.LeftAt == null)
                         || (p.SupervisorAssignment != null
                             && p.SupervisorAssignment.EndedAt == null
                             && p.SupervisorAssignment.SupervisorProfile.UserId == userId)),
                cancellationToken);
    }
}
