using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Infrastructure.Persistence.Generated;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Identity;

internal sealed class AccessTokenAccountValidator(AipmsDbContext context)
    : IAccessTokenAccountValidator
{
    public async Task<bool> IsValidAsync(
        long userId,
        DateTime? passwordChangedAtUtc,
        IReadOnlyCollection<string> claimedRoles,
        CancellationToken cancellationToken = default)
    {
        var account = await context.Users.AsNoTracking()
            .Where(user => user.Id == userId
                && user.Status == "ACTIVE"
                && user.PasswordChangedAt == passwordChangedAtUtc)
            .Select(user => new
            {
                Roles = user.UserRoleUsers.Select(assignment => assignment.Role.Code).ToArray()
            })
            .SingleOrDefaultAsync(cancellationToken);

        return account is not null && claimedRoles.All(role =>
            account.Roles.Contains(role, StringComparer.Ordinal));
    }
}
