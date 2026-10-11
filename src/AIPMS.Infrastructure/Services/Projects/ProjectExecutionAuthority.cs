using System.Threading.Tasks;
using AIPMS.Infrastructure.Persistence.Generated;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Services.Projects;

internal static class ProjectExecutionAuthority
{
    internal static Task<bool> IsManagerAsync(AipmsDbContext db, long projectId, long userId, CancellationToken ct) =>
        db.Users.AnyAsync(u => u.Id == userId && u.Status == "ACTIVE"
            && ((u.UserRoleUsers.Any(r => r.Role.Code == "STUDENT") && db.TeamMembers.Any(m => m.UserId == userId
                && m.IsLeader && m.LeftAt == null && m.Team.Project != null && m.Team.Project.Id == projectId))
            || (u.UserRoleUsers.Any(r => r.Role.Code == "LECTURER") && u.Department != null && u.Department.IsActive
                && u.Department.Organization.IsActive && db.SupervisorAssignments.Any(a => a.ProjectId == projectId
                    && a.IsPrimary && a.AssignmentType == "PRIMARY" && a.EndedAt == null && a.SupervisorProfile.UserId == userId))), ct);
}
