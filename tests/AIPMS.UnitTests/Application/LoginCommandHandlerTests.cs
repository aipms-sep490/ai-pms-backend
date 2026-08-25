using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Auth.Abstractions;
using AIPMS.Application.Features.Auth.Commands.Login;
using AIPMS.Application.Features.Auth.Models;

namespace AIPMS.UnitTests.Application;

public sealed class LoginCommandHandlerTests
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 8, 14, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Handle_ValidCredentials_ReturnsTokenAndUpdatesLastLogin()
    {
        var repository = new FakeAuthRepository
        {
            Account = new AuthAccount(
                42,
                "student@aipms.test",
                "valid-hash",
                "Test Student",
                "ACTIVE",
                ["STUDENT"],
                0,
                null,
                null)
        };
        var handler = CreateHandler(repository, passwordIsValid: true);

        var response = await handler.Handle(
            new LoginCommand(" student@aipms.test ", "Password@123"),
            CancellationToken.None);

        Assert.Equal("test-access-token", response.AccessToken);
        Assert.Equal("Bearer", response.TokenType);
        Assert.Equal("test-refresh-token", response.RefreshToken);
        Assert.Equal(42, response.User.Id);
        Assert.Equal(42, repository.UpdatedUserId);
        Assert.Equal(FixedNow.UtcDateTime, repository.LastLoginAtUtc);
    }

    [Fact]
    public async Task Handle_InvalidPassword_ThrowsUnauthorizedException()
    {
        var repository = new FakeAuthRepository
        {
            Account = new AuthAccount(
                42,
                "student@aipms.test",
                "valid-hash",
                "Test Student",
                "ACTIVE",
                ["STUDENT"],
                0,
                null,
                null)
        };
        var handler = CreateHandler(repository, passwordIsValid: false);

        await Assert.ThrowsAsync<UnauthorizedException>(() => handler.Handle(
            new LoginCommand("student@aipms.test", "wrong-password"),
            CancellationToken.None));

        Assert.Equal(42, repository.UpdatedUserId);
        Assert.Equal(1, repository.FailedLoginCount);
    }

    [Fact]
    public async Task Handle_SuspendedAccount_ThrowsForbiddenException()
    {
        var repository = new FakeAuthRepository
        {
            Account = new AuthAccount(
                42,
                "student@aipms.test",
                "valid-hash",
                "Test Student",
                "SUSPENDED",
                ["STUDENT"],
                0,
                null,
                null)
        };
        var handler = CreateHandler(repository, passwordIsValid: true);

        await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(
            new LoginCommand("student@aipms.test", "Password@123"),
            CancellationToken.None));

        Assert.Null(repository.UpdatedUserId);
    }

    [Fact]
    public async Task Handle_FifthInvalidAttempt_LocksAccountForConfiguredPeriod()
    {
        var repository = new FakeAuthRepository
        {
            Account = new AuthAccount(
                42,
                "student@aipms.test",
                "valid-hash",
                "Test Student",
                "ACTIVE",
                ["STUDENT"],
                4,
                null,
                null)
        };
        var handler = CreateHandler(repository, passwordIsValid: false);

        await Assert.ThrowsAsync<UnauthorizedException>(() => handler.Handle(
            new LoginCommand("student@aipms.test", "wrong-password"),
            CancellationToken.None));

        Assert.Equal(5, repository.FailedLoginCount);
        Assert.Equal(FixedNow.AddMinutes(15).UtcDateTime, repository.LockoutEndAtUtc);
    }

    private static LoginCommandHandler CreateHandler(
        FakeAuthRepository repository,
        bool passwordIsValid) =>
        new(
            repository,
            new FakePasswordHashingService(passwordIsValid),
            new FakeAccessTokenService(),
            new FakeOpaqueTokenService(),
            new FakeSecurityPolicy(),
            new FakeRequestContext(),
            new NoOpAuditTrail(),
            new FixedTimeProvider(FixedNow));

    private sealed class FakeAuthRepository : IAuthRepository
    {
        public AuthAccount? Account { get; init; }

        public long? UpdatedUserId { get; private set; }

        public DateTime? LastLoginAtUtc { get; private set; }

        public int? FailedLoginCount { get; private set; }

        public DateTime? LockoutEndAtUtc { get; private set; }

        public Task<AuthAccount?> FindByEmailAsync(
            string email,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal(Account?.Email, email);
            return Task.FromResult(Account);
        }

        public Task<AuthAccount?> FindByIdAsync(
            long userId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Account?.Id == userId ? Account : null);

        public Task RecordFailedLoginAsync(
            long userId,
            int failedCount,
            DateTime? lockoutEndAtUtc,
            DateTime utcNow,
            CancellationToken cancellationToken = default)
        {
            UpdatedUserId = userId;
            FailedLoginCount = failedCount;
            LockoutEndAtUtc = lockoutEndAtUtc;
            return Task.CompletedTask;
        }

        public Task CompleteSuccessfulLoginAsync(
            long userId,
            DateTime lastLoginAtUtc,
            RefreshTokenData refreshToken,
            CancellationToken cancellationToken = default)
        {
            UpdatedUserId = userId;
            LastLoginAtUtc = lastLoginAtUtc;
            return Task.CompletedTask;
        }

        public Task<RefreshSession?> FindRefreshSessionAsync(byte[] tokenHash, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task RotateRefreshTokenAsync(long currentTokenId, RefreshTokenData replacement, DateTime utcNow, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task RevokeRefreshTokenAsync(byte[] tokenHash, DateTime utcNow, string? revokedByIp, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task RevokeRefreshTokenFamilyForReuseAsync(Guid familyId, DateTime utcNow, string? revokedByIp, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UpdatePasswordAsync(long userId, string passwordHash, DateTime utcNow, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task CreatePasswordResetTokenAsync(long userId, byte[] tokenHash, DateTime expiresAtUtc, DateTime utcNow, string? requestedByIp, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PasswordResetSession?> FindPasswordResetSessionAsync(byte[] tokenHash, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task CompletePasswordResetAsync(long tokenId, long userId, string passwordHash, DateTime utcNow, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakePasswordHashingService(bool isValid) : IPasswordHashingService
    {
        public string Hash(string password) => "unused";

        public bool Verify(string passwordHash, string providedPassword) => isValid;
    }

    private sealed class FakeAccessTokenService : IAccessTokenService
    {
        public AccessTokenResult Create(AccessTokenDescriptor descriptor) =>
            new("test-access-token", FixedNow.AddHours(1).UtcDateTime);
    }

    private sealed class FakeOpaqueTokenService : IOpaqueTokenService
    {
        public OpaqueToken Generate() => new("test-refresh-token", [1, 2, 3]);

        public byte[] Hash(string token) => [1, 2, 3];
    }

    private sealed class FakeSecurityPolicy : IAccountSecurityPolicy
    {
        public int FailedLoginThreshold => 5;
        public int LockoutMinutes => 15;
        public int RefreshTokenDays => 14;
        public int PasswordResetMinutes => 30;
    }

    private sealed class FakeRequestContext : IRequestContext
    {
        public string? IpAddress => "127.0.0.1";
        public string? UserAgent => "unit-test";
        public Guid CorrelationId => Guid.Empty;
    }

    private sealed class NoOpAuditTrail : IAuditTrail
    {
        public Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
