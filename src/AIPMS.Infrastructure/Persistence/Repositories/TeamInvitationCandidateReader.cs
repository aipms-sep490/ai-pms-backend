using System.Threading.Tasks;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.Teams.Abstractions;
using AIPMS.Application.Features.Teams.Models;
using AIPMS.Infrastructure.Persistence.Generated;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Persistence.Repositories;

internal sealed class TeamInvitationCandidateReader(AipmsDbContext context) : ITeamInvitationCandidateReader
{
    public async Task<PagedResult<TeamInvitationCandidate>> SearchAsync(TeamInvitationCandidateScope scope,
        string? search, int page, int pageSize, CancellationToken cancellationToken)
    {
        var majorIds = scope.AllowedMajorIds?.ToArray() ?? [scope.MajorId];
        var query = context.Users.AsNoTracking().Where(u =>
            u.Status == "ACTIVE" && u.UserRoleUsers.Any(r => r.Role.Code == AppRoles.Student)
            && u.MajorId.HasValue && majorIds.Contains(u.MajorId.Value) && u.Major != null && u.Major.IsActive
            && u.DepartmentId == u.Major.DepartmentId && u.Major.Department.IsActive
            && u.Major.Department.OrganizationId == scope.OrganizationId && u.Major.Department.Organization.IsActive
            && !u.TeamMembers.Any(m => m.AcademicSemesterId == scope.SemesterId && m.LeftAt == null));
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(u => u.FullName.Contains(search) || u.Email.Contains(search)
                || (u.StudentCode != null && u.StudentCode.Contains(search)));

        var count = await query.LongCountAsync(cancellationToken);
        // Only expose the requesting team's live invitation; other teams' invitations are private.
        var items = await query.OrderBy(u => u.FullName).ThenBy(u => u.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(u => new TeamInvitationCandidate(u.Id, u.FullName, u.Email, u.StudentCode,
                u.MajorId!.Value, u.Major!.Code, u.Major.Name,
                u.TeamInvitationInvitedUsers.Where(i => i.TeamId == scope.TeamId && i.Status == "PENDING"
                    && i.ExpiresAt > scope.Now).OrderByDescending(i => i.Id).Select(i => (long?)i.Id).FirstOrDefault(),
                u.TeamInvitationInvitedUsers.Where(i => i.TeamId == scope.TeamId && i.Status == "PENDING"
                    && i.ExpiresAt > scope.Now).OrderByDescending(i => i.Id).Select(i => i.ExpiresAt).FirstOrDefault()))
            .ToListAsync(cancellationToken);
        return new(items, page, pageSize, count);
    }
}
