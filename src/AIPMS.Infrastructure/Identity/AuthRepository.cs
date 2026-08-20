using System.Threading.Tasks;
using AIPMS.Application.Features.Auth.Abstractions;
using AIPMS.Application.Features.Auth.Models;
using AIPMS.Infrastructure.Persistence.Generated;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Identity;

internal sealed class AuthRepository(AipmsDbContext context) : IAuthRepository
{
    public async Task<AuthAccount?> FindByEmailAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        var user = await context.Users
            .AsNoTracking()
            .Include(static user => user.UserRoles)
            .ThenInclude(static userRole => userRole.Role)
            .SingleOrDefaultAsync(user => user.Email == email, cancellationToken);

        return user is null
            ? null
            : new AuthAccount(
                user.Id,
                user.Email,
                user.PasswordHash,
                user.FullName,
                user.Status,
                user.UserRoles
                    .Select(static userRole => userRole.Role.Code)
                    .OrderBy(static role => role, StringComparer.Ordinal)
                    .ToArray());
    }

    public Task UpdateLastLoginAsync(
        long userId,
        DateTime lastLoginAtUtc,
        CancellationToken cancellationToken = default) =>
        context.Users
            .Where(user => user.Id == userId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(user => user.LastLoginAt, lastLoginAtUtc)
                    .SetProperty(user => user.UpdatedAt, lastLoginAtUtc),
                cancellationToken);
}
