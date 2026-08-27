using AIPMS.Application.Features.Auth.Models;

namespace AIPMS.Application.Features.Auth.Abstractions;

public interface IAuthRepository
{
    Task<AuthAccount?> FindByEmailAsync(
        string email,
        CancellationToken cancellationToken = default);

    Task<AuthAccount?> FindByIdAsync(
        long userId,
        CancellationToken cancellationToken = default);

    Task RecordFailedLoginAsync(
        long userId,
        int failedCount,
        DateTime? lockoutEndAtUtc,
        DateTime utcNow,
        CancellationToken cancellationToken = default);

    Task CompleteSuccessfulLoginAsync(
        long userId,
        DateTime lastLoginAtUtc,
        RefreshTokenData refreshToken,
        CancellationToken cancellationToken = default);

    Task<RefreshSession?> FindRefreshSessionAsync(
        byte[] tokenHash,
        CancellationToken cancellationToken = default);

    Task RotateRefreshTokenAsync(
        long currentTokenId,
        RefreshTokenData replacement,
        DateTime utcNow,
        CancellationToken cancellationToken = default);

    Task RevokeRefreshTokenAsync(
        byte[] tokenHash,
        DateTime utcNow,
        string? revokedByIp,
        CancellationToken cancellationToken = default);

    Task RevokeRefreshTokenFamilyForReuseAsync(
        Guid familyId,
        DateTime utcNow,
        string? revokedByIp,
        CancellationToken cancellationToken = default);

    Task UpdatePasswordAsync(
        long userId,
        string passwordHash,
        DateTime utcNow,
        CancellationToken cancellationToken = default);

    Task CreatePasswordResetTokenAsync(
        long userId,
        byte[] tokenHash,
        DateTime expiresAtUtc,
        DateTime utcNow,
        string? requestedByIp,
        CancellationToken cancellationToken = default);

    Task<PasswordResetSession?> FindPasswordResetSessionAsync(
        byte[] tokenHash,
        CancellationToken cancellationToken = default);

    Task CompletePasswordResetAsync(
        long tokenId,
        long userId,
        string passwordHash,
        DateTime utcNow,
        CancellationToken cancellationToken = default);
}
