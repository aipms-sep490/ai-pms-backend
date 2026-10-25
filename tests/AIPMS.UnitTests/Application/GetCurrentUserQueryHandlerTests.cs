using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Auth.Abstractions;
using AIPMS.Application.Features.Auth.Models;
using AIPMS.Application.Features.Auth.Queries;

namespace AIPMS.UnitTests.Application;

public sealed class GetCurrentUserQueryHandlerTests
{
    [Fact]
    public async Task Handle_UnauthenticatedUser_ThrowsUnauthorizedException()
    {
        var currentUser = new StubCurrentUser(isAuthenticated: false, userId: null);
        var repository = new StubAuthRepository();
        var handler = new GetCurrentUserQueryHandler(currentUser, repository);

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            handler.Handle(new GetCurrentUserQuery(), CancellationToken.None));
    }

    [Fact]
    public async Task Handle_UserWithDatabaseRecord_ReturnsTitleAndPhone()
    {
        var currentUser = new StubCurrentUser(
            isAuthenticated: true,
            userId: 42,
            email: "student@aipms.test",
            fullName: "Student Name",
            roles: ["STUDENT"]);

        var repository = new StubAuthRepository
        {
            Account = new AuthAccount(
                42,
                "student@aipms.test",
                "hash",
                "Student Name",
                "ACTIVE",
                ["STUDENT"],
                0,
                null,
                null,
                Title: "DST",
                Phone: "0901234567")
        };

        var handler = new GetCurrentUserQueryHandler(currentUser, repository);
        var result = await handler.Handle(new GetCurrentUserQuery(), CancellationToken.None);

        Assert.Equal(42, result.Id);
        Assert.Equal("student@aipms.test", result.Email);
        Assert.Equal("Student Name", result.FullName);
        Assert.Equal("DST", result.Title);
        Assert.Equal("0901234567", result.Phone);
        Assert.Contains("STUDENT", result.Roles);
    }

    [Fact]
    public async Task Handle_UserNotInDatabase_FallsBackToCurrentUserClaims()
    {
        var currentUser = new StubCurrentUser(
            isAuthenticated: true,
            userId: 99,
            email: "synthetic@aipms.test",
            fullName: "Synthetic User",
            roles: ["LECTURER"]);

        var repository = new StubAuthRepository { Account = null };
        var handler = new GetCurrentUserQueryHandler(currentUser, repository);
        var result = await handler.Handle(new GetCurrentUserQuery(), CancellationToken.None);

        Assert.Equal(99, result.Id);
        Assert.Equal("synthetic@aipms.test", result.Email);
        Assert.Equal("Synthetic User", result.FullName);
        Assert.Null(result.Title);
        Assert.Null(result.Phone);
        Assert.Contains("LECTURER", result.Roles);
    }

    private sealed class StubCurrentUser(
        bool isAuthenticated,
        long? userId,
        string? email = null,
        string? fullName = null,
        IReadOnlyCollection<string>? roles = null) : ICurrentUser
    {
        public bool IsAuthenticated => isAuthenticated;
        public long? UserId => userId;
        public string? Email => email;
        public string? FullName => fullName;
        public IReadOnlyCollection<string> Roles => roles ?? [];
    }

    private sealed class StubAuthRepository : IAuthRepository
    {
        public AuthAccount? Account { get; init; }

        public Task<AuthAccount?> FindByIdAsync(long userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Account?.Id == userId ? Account : null);

        public Task<AuthAccount?> FindByEmailAsync(string email, CancellationToken cancellationToken = default) =>
            Task.FromResult(Account);

        public Task RecordFailedLoginAsync(long userId, int failedCount, DateTime? lockoutEndAtUtc, DateTime utcNow, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CompleteSuccessfulLoginAsync(long userId, DateTime lastLoginAtUtc, RefreshTokenData refreshToken, CancellationToken cancellationToken = default, string? expectedPasswordHash = null) => Task.CompletedTask;
        public Task<RefreshSession?> FindRefreshSessionAsync(byte[] tokenHash, CancellationToken cancellationToken = default) => Task.FromResult<RefreshSession?>(null);
        public Task RotateRefreshTokenAsync(long currentTokenId, RefreshTokenData replacement, DateTime utcNow, CancellationToken cancellationToken = default, string? expectedPasswordHash = null) => Task.CompletedTask;
        public Task RevokeRefreshTokenAsync(byte[] tokenHash, DateTime utcNow, string? revokedByIp, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RevokeRefreshTokenFamilyForReuseAsync(Guid familyId, DateTime utcNow, string? revokedByIp, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task UpdatePasswordAsync(long userId, string passwordHash, DateTime utcNow, CancellationToken cancellationToken = default, string? expectedPasswordHash = null, Func<Task>? onCompleted = null) => Task.CompletedTask;
        public Task CreatePasswordResetTokenAsync(long userId, byte[] tokenHash, DateTime expiresAtUtc, DateTime utcNow, string? requestedByIp, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<PasswordResetSession?> FindPasswordResetSessionAsync(byte[] tokenHash, CancellationToken cancellationToken = default) => Task.FromResult<PasswordResetSession?>(null);
        public Task CompletePasswordResetAsync(long tokenId, long userId, string passwordHash, DateTime utcNow, CancellationToken cancellationToken = default, Func<Task>? onCompleted = null) => Task.CompletedTask;
    }
}
