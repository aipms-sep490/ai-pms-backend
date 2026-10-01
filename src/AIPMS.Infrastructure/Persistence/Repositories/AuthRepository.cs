using System.Data;
using System.Threading.Tasks;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Auth.Abstractions;
using AIPMS.Application.Features.Auth.Models;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Email;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using PasswordResetToken = AIPMS.Infrastructure.Persistence.Generated.Models.PasswordResetToken;
using RefreshTokenEntity = AIPMS.Infrastructure.Persistence.Generated.Models.RefreshToken;
using User = AIPMS.Infrastructure.Persistence.Generated.Models.User;

namespace AIPMS.Infrastructure.Persistence.Repositories;

internal sealed class AuthRepository(AipmsDbContext context, IOptions<PasswordRecoverySettings> recoveryOptions, TimeProvider clock) : IAuthRepository
{
    public async Task<AuthAccount?> FindByEmailAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        var user = await AccountQuery()
            .SingleOrDefaultAsync(user => user.Email == email, cancellationToken);
        return user is null ? null : ToAccount(user);
    }

    public async Task<AuthAccount?> FindByIdAsync(
        long userId,
        CancellationToken cancellationToken = default)
    {
        var user = await AccountQuery()
            .SingleOrDefaultAsync(user => user.Id == userId, cancellationToken);
        return user is null ? null : ToAccount(user);
    }

    public Task RecordFailedLoginAsync(
        long userId,
        int failedCount,
        DateTime? lockoutEndAtUtc,
        DateTime utcNow,
        CancellationToken cancellationToken = default) =>
        context.Users
            .Where(user => user.Id == userId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(user => user.AccessFailedCount, failedCount)
                    .SetProperty(user => user.LockoutEndAt, lockoutEndAtUtc)
                    .SetProperty(user => user.UpdatedAt, utcNow),
                cancellationToken);

    public async Task CompleteSuccessfulLoginAsync(
        long userId,
        DateTime lastLoginAtUtc,
        RefreshTokenData refreshToken,
        CancellationToken cancellationToken = default, string? expectedPasswordHash = null)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var user = await LockUserAsync(userId, cancellationToken);
        EnsureCurrentAccount(user, expectedPasswordHash);
        lastLoginAtUtc = clock.GetUtcNow().UtcDateTime;
        if (user.LockoutEndAt > lastLoginAtUtc) throw new UnauthorizedException("Invalid email or password.");
        user.LastLoginAt = lastLoginAtUtc;
        user.AccessFailedCount = 0;
        user.LockoutEndAt = null;
        user.UpdatedAt = lastLoginAtUtc;
        context.RefreshTokens.Add(ToEntity(userId, refreshToken, lastLoginAtUtc));
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<RefreshSession?> FindRefreshSessionAsync(
        byte[] tokenHash,
        CancellationToken cancellationToken = default)
    {
        var token = await context.RefreshTokens
            .AsNoTracking()
            .Include(static item => item.User)
            .ThenInclude(static user => user.UserRoleUsers)
            .ThenInclude(static userRole => userRole.Role)
            .SingleOrDefaultAsync(item => item.TokenHash == tokenHash, cancellationToken);
        return token is null
            ? null
            : new RefreshSession(
                token.Id,
                token.FamilyId,
                token.ExpiresAt,
                token.RevokedAt,
                token.ReuseDetectedAt,
                ToAccount(token.User));
    }

    public async Task RotateRefreshTokenAsync(
        long currentTokenId,
        RefreshTokenData replacement,
        DateTime utcNow,
        CancellationToken cancellationToken = default, string? expectedPasswordHash = null)
    {
        var ownerId = await context.RefreshTokens.AsNoTracking().Where(x => x.Id == currentTokenId)
            .Select(x => x.UserId).SingleAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var user = await LockUserAsync(ownerId, cancellationToken);
        EnsureCurrentAccount(user, expectedPasswordHash);
        utcNow = clock.GetUtcNow().UtcDateTime;
        var current = await context.RefreshTokens.SingleAsync(
            token => token.Id == currentTokenId,
            cancellationToken);
        if (current.RevokedAt is not null || current.ExpiresAt <= utcNow)
        {
            throw new ConflictException("The refresh token has already been consumed.");
        }

        var replacementEntity = ToEntity(current.UserId, replacement, utcNow);
        context.RefreshTokens.Add(replacementEntity);
        await context.SaveChangesAsync(cancellationToken);
        current.RevokedAt = utcNow;
        current.RevokedByIp = replacement.IpAddress;
        current.ReplacedByTokenId = replacementEntity.Id;
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task RevokeRefreshTokenAsync(
        byte[] tokenHash,
        DateTime utcNow,
        string? revokedByIp,
        CancellationToken cancellationToken = default)
    {
        var token = await context.RefreshTokens.SingleOrDefaultAsync(
            item => item.TokenHash == tokenHash,
            cancellationToken);
        if (token is null || token.RevokedAt is not null)
        {
            return;
        }

        token.RevokedAt = utcNow;
        token.RevokedByIp = revokedByIp;
        await context.SaveChangesAsync(cancellationToken);
    }

    public Task RevokeRefreshTokenFamilyForReuseAsync(
        Guid familyId,
        DateTime utcNow,
        string? revokedByIp,
        CancellationToken cancellationToken = default) =>
        context.RefreshTokens
            .Where(token => token.FamilyId == familyId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(token => token.RevokedAt, token => token.RevokedAt ?? utcNow)
                    .SetProperty(token => token.RevokedByIp, revokedByIp)
                    .SetProperty(token => token.ReuseDetectedAt, utcNow),
                cancellationToken);

    public async Task UpdatePasswordAsync(
        long userId,
        string passwordHash,
        DateTime utcNow,
        CancellationToken cancellationToken = default, string? expectedPasswordHash = null, Func<Task>? onCompleted = null)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var user = await LockUserAsync(userId, cancellationToken);
        EnsureCurrentAccount(user, expectedPasswordHash);
        utcNow = clock.GetUtcNow().UtcDateTime;
        user.PasswordHash = passwordHash;
        user.PasswordChangedAt = NextPasswordVersion(user.PasswordChangedAt, utcNow);
        user.AccessFailedCount = 0;
        user.LockoutEndAt = null;
        user.UpdatedAt = utcNow;
        await RevokeActiveSessionsAsync(userId, utcNow, cancellationToken);
        await InvalidateRecoveryAsync(user, utcNow, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        if (onCompleted is not null) await onCompleted();
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task CreatePasswordResetTokenAsync(
        long userId,
        byte[] tokenHash,
        DateTime expiresAtUtc,
        DateTime utcNow,
        string? requestedByIp,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        EnsureCurrentAccount(await LockUserAsync(userId, cancellationToken), null);
        await context.PasswordResetTokens
            .Where(token => token.UserId == userId && token.UsedAt == null && token.ExpiresAt > utcNow)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(token => token.UsedAt, utcNow),
                cancellationToken);
        context.PasswordResetTokens.Add(new PasswordResetToken
        {
            UserId = userId,
            TokenHash = tokenHash,
            ExpiresAt = expiresAtUtc,
            CreatedAt = utcNow,
            RequestedByIp = requestedByIp
        });
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<PasswordResetSession?> FindPasswordResetSessionAsync(
        byte[] tokenHash,
        CancellationToken cancellationToken = default)
    {
        var token = await context.PasswordResetTokens
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.TokenHash == tokenHash, cancellationToken);
        return token is null
            ? null
            : new PasswordResetSession(token.Id, token.UserId, token.ExpiresAt, token.UsedAt);
    }

    public async Task CompletePasswordResetAsync(
        long tokenId,
        long userId,
        string passwordHash,
        DateTime utcNow,
        CancellationToken cancellationToken = default, Func<Task>? onCompleted = null)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var user = await LockUserAsync(userId, cancellationToken);
        EnsureCurrentAccount(user, null);
        utcNow = clock.GetUtcNow().UtcDateTime;
        if (!string.IsNullOrWhiteSpace(recoveryOptions.Value.LookupKey))
            await PasswordRecoveryIdentity.LockAsync(context, PasswordRecoveryIdentity.Fingerprint(user.Email, recoveryOptions.Value.LookupKey), cancellationToken);
        var token = await context.PasswordResetTokens.SingleAsync(
            item => item.Id == tokenId && item.UserId == userId,
            cancellationToken);
        if (token.UsedAt is not null || token.ExpiresAt <= utcNow)
        {
            throw new ConflictException("The password reset token has already been consumed.");
        }

        var recovery = await context.PasswordRecoveryRequests.AsNoTracking()
            .SingleOrDefaultAsync(x => x.ResetTokenId == tokenId, cancellationToken);
        if (recovery is not null && (recovery.Status is not ("SENT" or "SENDING" or "RETRY")
            || await context.PasswordRecoveryRequests.AnyAsync(x => x.EmailHash == recovery.EmailHash && x.Id > recovery.Id, cancellationToken)))
            throw new UnauthorizedException("The password reset token is invalid or expired.");
        user.PasswordHash = passwordHash;
        user.PasswordChangedAt = NextPasswordVersion(user.PasswordChangedAt, utcNow);
        user.AccessFailedCount = 0;
        user.LockoutEndAt = null;
        user.UpdatedAt = utcNow;
        token.UsedAt = utcNow;
        await context.PasswordResetTokens
            .Where(item => item.UserId == userId && item.Id != tokenId && item.UsedAt == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(item => item.UsedAt, utcNow),
                cancellationToken);
        await RevokeActiveSessionsAsync(userId, utcNow, cancellationToken);
        await InvalidateRecoveryAsync(user, utcNow, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        if (onCompleted is not null) await onCompleted();
        await transaction.CommitAsync(cancellationToken);
    }

    private Task<User> LockUserAsync(long userId, CancellationToken ct) => context.Users
        .FromSqlInterpolated($"SELECT * FROM dbo.users WITH (UPDLOCK, HOLDLOCK) WHERE id = {userId}")
        .SingleAsync(ct);

    private static void EnsureCurrentAccount(User user, string? expectedPasswordHash)
    {
        if (user.Status != "ACTIVE") throw new UnauthorizedException("The account is unavailable.");
        if (expectedPasswordHash is not null && user.PasswordHash != expectedPasswordHash)
            throw new UnauthorizedException("Credentials changed. Please sign in again.");
    }

    private async Task InvalidateRecoveryAsync(User user, DateTime now, CancellationToken ct)
    {
        // Keep a high-precision cutoff: password_changed_at is a monotonic, second-resolution JWT version.
        user.PasswordRecoveryInvalidBefore = clock.GetUtcNow().UtcDateTime;
        var userId = user.Id;
        var emailHash = string.IsNullOrWhiteSpace(recoveryOptions.Value.LookupKey) ? ""
            : PasswordRecoveryIdentity.Fingerprint(user.Email, recoveryOptions.Value.LookupKey);
        if (emailHash.Length > 0) await PasswordRecoveryIdentity.LockAsync(context, emailHash, ct);
        await context.PasswordResetTokens.Where(x => x.UserId == userId && x.UsedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.UsedAt, now), ct);
        await context.PasswordRecoveryRequests.Where(x => (x.EmailHash == emailHash || (x.ResetTokenId != null
            && context.PasswordResetTokens.Any(t => t.Id == x.ResetTokenId && t.UserId == userId)))
            && (x.Status == "PENDING" || x.Status == "RETRY" || x.Status == "SENDING" || x.Status == "SENT"))
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, "SUPERSEDED")
                .SetProperty(x => x.ProtectedPayload, (string?)null).SetProperty(x => x.CompletedAt, now)
                .SetProperty(x => x.LeaseToken, (Guid?)null).SetProperty(x => x.LeaseUntil, (DateTime?)null), ct);
    }

    private IQueryable<User> AccountQuery() =>
        context.Users
            .AsNoTracking()
            .Include(static user => user.UserRoleUsers)
            .ThenInclude(static userRole => userRole.Role);

    private static DateTime NextPasswordVersion(DateTime? current, DateTime utcNow)
    {
        var normalized = new DateTime(
            utcNow.Ticks - (utcNow.Ticks % TimeSpan.TicksPerSecond),
            DateTimeKind.Utc);
        return current is not null && current.Value >= normalized
            ? current.Value.AddSeconds(1)
            : normalized;
    }

    private static AuthAccount ToAccount(User user) =>
        new(
            user.Id,
            user.Email,
            user.PasswordHash,
            user.FullName,
            user.Status,
            user.UserRoleUsers
                .Select(static userRole => userRole.Role.Code)
                .OrderBy(static role => role, StringComparer.Ordinal)
                .ToArray(),
            user.AccessFailedCount,
            user.LockoutEndAt,
            user.PasswordChangedAt);

    private static RefreshTokenEntity ToEntity(
        long userId,
        RefreshTokenData token,
        DateTime utcNow) =>
        new()
        {
            UserId = userId,
            TokenHash = token.TokenHash,
            FamilyId = token.FamilyId,
            ExpiresAt = token.ExpiresAtUtc,
            CreatedAt = utcNow,
            CreatedByIp = token.IpAddress,
            UserAgent = token.UserAgent
        };

    private Task RevokeActiveSessionsAsync(
        long userId,
        DateTime utcNow,
        CancellationToken cancellationToken) =>
        context.RefreshTokens
            .Where(token => token.UserId == userId && token.RevokedAt == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(token => token.RevokedAt, utcNow),
                cancellationToken);
}
