using AIPMS.Application.Features.Auth.Models;

namespace AIPMS.Application.Features.Auth.Abstractions;

public interface IAuthRepository
{
    Task<AuthAccount?> FindByEmailAsync(
        string email,
        CancellationToken cancellationToken = default);

    Task UpdateLastLoginAsync(
        long userId,
        DateTime lastLoginAtUtc,
        CancellationToken cancellationToken = default);
}
