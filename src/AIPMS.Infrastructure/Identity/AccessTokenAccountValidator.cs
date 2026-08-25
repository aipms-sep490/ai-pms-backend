using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Infrastructure.Persistence.Generated;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Identity;

internal sealed class AccessTokenAccountValidator(AipmsDbContext context)
    : IAccessTokenAccountValidator
{
    public Task<bool> IsValidAsync(
        long userId,
        DateTime? passwordChangedAtUtc,
        CancellationToken cancellationToken = default) =>
        context.Users.AsNoTracking().AnyAsync(
            user => user.Id == userId
                && user.Status == "ACTIVE"
                && user.PasswordChangedAt == passwordChangedAtUtc,
            cancellationToken);
}
