using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.Academic.Abstractions;

namespace AIPMS.Application.Features.Academic.Services;

public sealed class AcademicAccessService(
    ICurrentUser currentUser,
    IAcademicStructureRepository repository)
{
    public long ActorUserId => currentUser.UserId
        ?? throw new UnauthorizedException();

    public void EnsureCanManageOrganizations()
    {
        EnsureAuthenticated();

        if (!HasRole(AppRoles.Admin))
        {
            throw new ForbiddenException(
                "Only a system administrator can manage organizations.");
        }
    }

    public void EnsureCanCreateDepartment()
    {
        EnsureAuthenticated();

        if (!HasRole(AppRoles.Admin))
        {
            throw new ForbiddenException(
                "Only a system administrator can create departments.");
        }
    }

    public async Task EnsureCanManageDepartmentAsync(
        long departmentId,
        CancellationToken cancellationToken)
    {
        EnsureAuthenticated();

        if (HasRole(AppRoles.Admin))
        {
            return;
        }

        var scope = await GetDepartmentStaffScopeAsync(cancellationToken);
        if (scope.DepartmentId != departmentId)
        {
            throw new ForbiddenException(
                "Department staff can only manage their assigned department.");
        }
    }

    public async Task EnsureCanManageMajorInDepartmentAsync(
        long departmentId,
        CancellationToken cancellationToken)
    {
        await EnsureCanManageDepartmentAsync(departmentId, cancellationToken);
    }

    private async Task<Models.AcademicUserScope> GetDepartmentStaffScopeAsync(
        CancellationToken cancellationToken)
    {
        if (!HasRole(AppRoles.DepartmentStaff))
        {
            throw new ForbiddenException();
        }

        var scope = await repository.GetUserScopeAsync(ActorUserId, cancellationToken);
        return scope ?? throw new ForbiddenException(
            "The current department staff account has no academic scope.");
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
