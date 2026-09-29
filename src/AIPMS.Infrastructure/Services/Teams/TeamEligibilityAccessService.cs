using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.Projects.Abstractions;
using AIPMS.Application.Features.Teams.Abstractions;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Services.Teams;

internal sealed class TeamEligibilityAccessService(
    AipmsDbContext context,
    ICurrentUser currentUser,
    IProjectRepository projectRepository) : ITeamEligibilityAccessService
{
    public async Task ValidateCanReadEligibilityAsync(long teamId, CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || !currentUser.UserId.HasValue)
            throw new UnauthorizedException();

        var actorId = currentUser.UserId.Value;

        var user = await context.Users
            .AsNoTracking()
            .Where(u => u.Id == actorId)
            .Select(u => new
            {
                u.Id,
                u.Status,
                u.DepartmentId,
                DepartmentIsActive = u.Department != null && u.Department.IsActive && u.Department.Organization.IsActive,
                Roles = u.UserRoleUsers.Select(r => r.Role.Code).ToList()
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (user is null || user.Status != "ACTIVE")
            throw new ForbiddenException("The account is not active.");

        var teamExists = await context.Teams
            .AsNoTracking()
            .AnyAsync(t => t.Id == teamId, cancellationToken);

        if (!teamExists)
            throw new NotFoundException("Team", teamId);

        // 1. Active Admin with legitimate persisted admin role
        var isAdmin = currentUser.Roles.Contains(AppRoles.Admin) && user.Roles.Contains(AppRoles.Admin);
        if (isAdmin)
            return;

        // 2. Active Team Member
        var isMember = await context.TeamMembers
            .AsNoTracking()
            .AnyAsync(m => m.TeamId == teamId && m.UserId == actorId && m.LeftAt == null, cancellationToken);
        if (isMember)
            return;

        // 3. Authorized Supervisor for team project
        var isSupervisor = await context.SupervisorAssignments
            .AsNoTracking()
            .AnyAsync(sa => sa.Project.TeamId == teamId
                           && sa.EndedAt == null
                           && sa.SupervisorProfile.UserId == actorId,
                      cancellationToken);
        if (isSupervisor)
            return;

        // 4. Department Staff
        var isStaff = currentUser.Roles.Contains(AppRoles.DepartmentStaff) && user.Roles.Contains(AppRoles.DepartmentStaff);
        if (!isStaff)
            throw new ForbiddenException("You do not have access to view this team's eligibility.");

        if (!user.DepartmentId.HasValue || !user.DepartmentIsActive)
            throw new ForbiddenException("Active department staff assignment with active department scope is required.");

        var staffDeptId = user.DepartmentId.Value;

        // 1. Authoritative project-level scope (if team has a relevant active or submitted project)
        var projectDeptIds = await projectRepository.GetAuthoritativeDepartmentIdsForTeamAsync(teamId, cancellationToken);
        if (projectDeptIds is not null)
        {
            if (projectDeptIds.Contains(staffDeptId))
                return;

            throw new ForbiddenException("You do not have access to view this team's eligibility.");
        }

        // 2. Team-formation scope (team has no relevant project yet)
        var config = await context.Set<TeamAcademicConfiguration>()
            .AsNoTracking()
            .Include(c => c.Requirements)
            .SingleOrDefaultAsync(c => c.TeamId == teamId, cancellationToken);

        if (config is not null)
        {
            if (config.LeadDepartmentId == staffDeptId)
                return;

            var reqMajorIds = config.Requirements.Select(r => r.MajorId).ToList();
            if (reqMajorIds.Count > 0)
            {
                var matchesReq = await context.Majors
                    .AsNoTracking()
                    .AnyAsync(m => reqMajorIds.Contains(m.Id) && m.DepartmentId == staffDeptId, cancellationToken);
                if (matchesReq)
                    return;
            }

            throw new ForbiddenException("You do not have access to view this team's eligibility.");
        }

        // 3. Unconfigured team formation: active members' majors determine department staff access
        var matchesMemberMajor = await context.TeamMembers
            .AsNoTracking()
            .Where(m => m.TeamId == teamId && m.LeftAt == null)
            .AnyAsync(m => m.User.Major != null && m.User.Major.DepartmentId == staffDeptId, cancellationToken);

        if (matchesMemberMajor)
            return;

        throw new ForbiddenException("You do not have access to view this team's eligibility.");
    }
}
