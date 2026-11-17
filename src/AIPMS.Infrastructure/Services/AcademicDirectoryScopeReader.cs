using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Security;
using AIPMS.Infrastructure.Persistence.Generated;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Services;

internal sealed class AcademicDirectoryScopeReader(ICurrentUser currentUser, AipmsDbContext db)
    : IAcademicDirectoryScopeReader
{
    public async Task<AcademicDirectoryScope> GetAsync(CancellationToken cancellationToken = default)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is not long actorId)
            throw new UnauthorizedException();

        var actor = await db.Users.AsNoTracking()
            .Where(user => user.Id == actorId)
            .Select(user => new
            {
                user.Status,
                user.DepartmentId,
                OrganizationId = user.Department == null ? (long?)null : user.Department.OrganizationId,
                ActiveDepartment = user.Department != null && user.Department.IsActive
                    && user.Department.Organization.IsActive,
                IsAdmin = user.UserRoleUsers.Any(assignment => assignment.Role.Code == AppRoles.Admin),
                IsStaff = user.UserRoleUsers.Any(assignment => assignment.Role.Code == AppRoles.DepartmentStaff)
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (actor is null || actor.Status != "ACTIVE")
            throw new ForbiddenException("The account is not active.");
        if (actor.IsAdmin) return new AcademicDirectoryScope(null, null);
        if (!actor.ActiveDepartment || actor.OrganizationId is null)
            throw new ForbiddenException("An active academic organization is required.");

        return new AcademicDirectoryScope(actor.OrganizationId,
            actor.IsStaff ? actor.DepartmentId : null);
    }
}
