using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Security;

namespace AIPMS.Application.Features.AccountSecurity.Services;

public sealed class AccountSecurityAccessService(ICurrentUser currentUser)
{
    public long ActorUserId => currentUser.UserId ?? throw new UnauthorizedException();

    public void EnsureAdministrator()
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is null)
        {
            throw new UnauthorizedException();
        }

        if (!currentUser.Roles.Contains(AppRoles.Admin, StringComparer.Ordinal))
        {
            throw new ForbiddenException();
        }
    }
}
