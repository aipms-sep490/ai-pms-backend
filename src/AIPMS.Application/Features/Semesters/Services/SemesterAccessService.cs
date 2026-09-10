using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Security;

namespace AIPMS.Application.Features.Semesters.Services;

/// <summary>
/// Enforces who may create / mutate academic semesters and project periods.
/// Rules:
///   - Admin: full access.
///   - DepartmentStaff / Student: read-only (all authenticated users may read).
///   - Unauthenticated: no access.
/// </summary>
public sealed class SemesterAccessService(ICurrentUser currentUser)
{
    public long ActorUserId => currentUser.UserId
        ?? throw new UnauthorizedException();

    public void EnsureCanManageSemesters()
    {
        EnsureAuthenticated();

        if (!HasRole(AppRoles.Admin))
        {
            throw new ForbiddenException(
                "Only a system administrator can manage academic semesters.");
        }
    }

    public void EnsureCanManageProjectPeriods()
    {
        EnsureAuthenticated();

        if (!HasRole(AppRoles.Admin))
        {
            throw new ForbiddenException(
                "Only a system administrator can manage project periods.");
        }
    }

    private void EnsureAuthenticated()
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is null)
        {
            throw new UnauthorizedException();
        }
    }

    private bool HasRole(string role) =>
        currentUser.Roles.Contains(role, StringComparer.Ordinal);
}
