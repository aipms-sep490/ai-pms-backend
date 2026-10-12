using System.Threading.Tasks;
using AIPMS.Infrastructure.Persistence.Generated;
using Microsoft.EntityFrameworkCore;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.Infrastructure.Services.Projects;

internal static class MeetingGovernanceAuthority
{
    public static async Task<bool> CanManage(AipmsDbContext db, M.Meeting meeting, long actor, CancellationToken ct)
    {
        if (!await db.Users.AnyAsync(u => u.Id == actor && u.Status == "ACTIVE", ct)) return false;
        var team = await db.Projects.Where(p => p.Id == meeting.ProjectId).Select(p => p.TeamId).SingleAsync(ct);
        return await db.TeamMembers.AnyAsync(m => m.TeamId == team && m.UserId == actor && m.LeftAt == null && (m.IsLeader || meeting.CreatedBy == actor), ct)
            || await db.SupervisorAssignments.AnyAsync(a => a.ProjectId == meeting.ProjectId && a.EndedAt == null && a.SupervisorProfile.UserId == actor, ct);
    }
}
