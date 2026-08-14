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
                ["STUDENT"])
        };
        var handler = CreateHandler(repository, passwordIsValid: true);

        var response = await handler.Handle(
            new LoginCommand(" student@aipms.test ", "Password@123"),
            CancellationToken.None);

        Assert.Equal("test-access-token", response.AccessToken);
        Assert.Equal("Bearer", response.TokenType);
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
                ["STUDENT"])
        };
        var handler = CreateHandler(repository, passwordIsValid: false);

        await Assert.ThrowsAsync<UnauthorizedException>(() => handler.Handle(
            new LoginCommand("student@aipms.test", "wrong-password"),
            CancellationToken.None));

        Assert.Null(repository.UpdatedUserId);
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
                ["STUDENT"])
        };
        var handler = CreateHandler(repository, passwordIsValid: true);

        await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(
            new LoginCommand("student@aipms.test", "Password@123"),
            CancellationToken.None));

        Assert.Null(repository.UpdatedUserId);
    }

    private static LoginCommandHandler CreateHandler(
        FakeAuthRepository repository,
        bool passwordIsValid) =>
        new(
            repository,
            new FakePasswordHashingService(passwordIsValid),
            new FakeAccessTokenService(),
            new FixedTimeProvider(FixedNow));

    private sealed class FakeAuthRepository : IAuthRepository
    {
        public AuthAccount? Account { get; init; }

        public long? UpdatedUserId { get; private set; }

        public DateTime? LastLoginAtUtc { get; private set; }

        public Task<AuthAccount?> FindByEmailAsync(
            string email,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal(Account?.Email, email);
            return Task.FromResult(Account);
        }

        public Task UpdateLastLoginAsync(
            long userId,
            DateTime lastLoginAtUtc,
            CancellationToken cancellationToken = default)
        {
            UpdatedUserId = userId;
            LastLoginAtUtc = lastLoginAtUtc;
            return Task.CompletedTask;
        }
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

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
