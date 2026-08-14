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
        var hasPlatformAccess = await context.UserRoles
            .AsNoTracking()
            .AnyAsync(
                userRole => userRole.UserId == userId
                    && (userRole.Role.Code == AppRoles.Admin
                        || userRole.Role.Code == AppRoles.DepartmentStaff),
                cancellationToken);

        if (hasPlatformAccess)
        {
            return true;
        }

        return await context.Projects
            .AsNoTracking()
            .AnyAsync(
                project => project.Id == projectId
                    && (project.Team.TeamMembers.Any(
                            member => member.UserId == userId && member.LeftAt == null)
                        || (project.SupervisorAssignment != null
                            && project.SupervisorAssignment.EndedAt == null
                            && project.SupervisorAssignment.SupervisorProfile.UserId == userId)),
                cancellationToken);
    }
}
