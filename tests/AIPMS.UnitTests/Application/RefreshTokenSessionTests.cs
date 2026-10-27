using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Auth.Abstractions;
using AIPMS.Application.Features.Auth.Commands;
using AIPMS.Application.Features.Auth.Models;
using Xunit;

namespace AIPMS.UnitTests.Application;

public sealed class RefreshTokenSessionTests
{
    private static readonly DateTime FixedNow = new(2026, 10, 25, 12, 0, 0, DateTimeKind.Utc);

    private static AuthAccount ActiveAccount(long id = 42) => new(
        id,
        "student@fpt.edu.vn",
        "hash-password-123",
        "Nguyen Van A",
        "ACTIVE",
        ["STUDENT"],
        0,
        null,
        null);

    private static RefreshSession CreateSession(
        long id = 1,
        DateTime? expiresAt = null,
        DateTime? revokedAt = null,
        DateTime? reuseDetectedAt = null,
        AuthAccount? account = null) =>
        new(
            id,
            Guid.NewGuid(),
            expiresAt ?? FixedNow.AddDays(7),
            revokedAt,
            reuseDetectedAt,
            account ?? ActiveAccount());

    [Fact]
    public async Task Refresh_with_valid_token_rotates_refresh_token_and_returns_new_access_token()
    {
        var session = CreateSession();
        var repository = new MockAuthSessionRepository { Session = session };
        var audit = new RecordingAuditTrail();
        var handler = CreateHandler(repository, audit);

        var response = await handler.Handle(new RefreshTokenCommand("valid-plain-token"), CancellationToken.None);

        Assert.Equal("new-access-token", response.AccessToken);
        Assert.Equal("replacement-refresh-token", response.RefreshToken);
        Assert.Equal(42, response.User.Id);

        // Verification of token rotation: replacement token was saved
        Assert.True(repository.Rotated);
        Assert.Equal(session.Id, repository.RotatedTokenId);
        Assert.NotNull(repository.ReplacementData);
        Assert.Equal(session.FamilyId, repository.ReplacementData.FamilyId);

        // Audit verification
        Assert.Contains(audit.Entries, e => e.Action == "AUTH_REFRESH_TOKEN_ROTATED" && e.Outcome == "SUCCESS" && e.ActorUserId == 42);
    }

    [Fact]
    public async Task Refresh_with_expired_token_is_rejected_and_records_failure_audit()
    {
        var session = CreateSession(expiresAt: FixedNow.AddMinutes(-5));
        var repository = new MockAuthSessionRepository { Session = session };
        var audit = new RecordingAuditTrail();
        var handler = CreateHandler(repository, audit);

        var ex = await Assert.ThrowsAsync<UnauthorizedException>(() =>
            handler.Handle(new RefreshTokenCommand("expired-token"), CancellationToken.None));

        Assert.Equal("The refresh token has expired.", ex.Message);
        Assert.False(repository.Rotated);
        Assert.Contains(audit.Entries, e => e.Action == "AUTH_REFRESH_TOKEN_ROTATED" && e.Outcome == "FAILURE" && e.ActorUserId == 42);
    }

    [Fact]
    public async Task Refresh_with_already_consumed_or_revoked_token_detects_reuse_and_revokes_entire_family()
    {
        var session = CreateSession(revokedAt: FixedNow.AddMinutes(-10));
        var repository = new MockAuthSessionRepository { Session = session };
        var audit = new RecordingAuditTrail();
        var handler = CreateHandler(repository, audit);

        var ex = await Assert.ThrowsAsync<UnauthorizedException>(() =>
            handler.Handle(new RefreshTokenCommand("consumed-token-reuse"), CancellationToken.None));

        Assert.Equal("The refresh token is invalid.", ex.Message);
        Assert.False(repository.Rotated);

        // Security requirement: revokes entire family upon reuse detection
        Assert.True(repository.FamilyRevokedForReuse);
        Assert.Equal(session.FamilyId, repository.RevokedFamilyId);

        Assert.Contains(audit.Entries, e => e.Action == "AUTH_REFRESH_TOKEN_ROTATED" && e.Outcome == "DENIED" && e.ActorUserId == 42);
    }

    [Fact]
    public async Task Refresh_for_locked_or_inactive_user_is_forbidden_and_records_denied_audit()
    {
        var lockedAccount = ActiveAccount() with { Status = "INACTIVE" };
        var session = CreateSession(account: lockedAccount);
        var repository = new MockAuthSessionRepository { Session = session };
        var audit = new RecordingAuditTrail();
        var handler = CreateHandler(repository, audit);

        var ex = await Assert.ThrowsAsync<ForbiddenException>(() =>
            handler.Handle(new RefreshTokenCommand("inactive-account-token"), CancellationToken.None));

        Assert.Equal("This account is not active.", ex.Message);
        Assert.False(repository.Rotated);
        Assert.Contains(audit.Entries, e => e.Action == "AUTH_REFRESH_TOKEN_ROTATED" && e.Outcome == "DENIED" && e.ActorUserId == 42);
    }

    [Fact]
    public async Task Refresh_with_unknown_or_tampered_token_throws_unauthorized()
    {
        var repository = new MockAuthSessionRepository { Session = null };
        var audit = new RecordingAuditTrail();
        var handler = CreateHandler(repository, audit);

        var ex = await Assert.ThrowsAsync<UnauthorizedException>(() =>
            handler.Handle(new RefreshTokenCommand("tampered-or-unknown-token"), CancellationToken.None));

        Assert.Equal("The refresh token is invalid.", ex.Message);
        Assert.False(repository.Rotated);
        Assert.Empty(audit.Entries);
    }

    [Fact]
    public async Task Concurrent_refresh_requests_with_same_token_prevent_race_condition()
    {
        var session = CreateSession();
        var repository = new MockAuthSessionRepository
        {
            Session = session,
            SimulateAlreadyConsumedOnRotate = true
        };
        var audit = new RecordingAuditTrail();
        var handler = CreateHandler(repository, audit);

        // When a competing thread consumes the token before this rotation commits
        await Assert.ThrowsAsync<ConflictException>(() =>
            handler.Handle(new RefreshTokenCommand("concurrent-token"), CancellationToken.None));
    }

    [Fact]
    public async Task Audit_and_logs_never_contain_plain_refresh_tokens()
    {
        var session = CreateSession();
        var repository = new MockAuthSessionRepository { Session = session };
        var audit = new RecordingAuditTrail();
        var handler = CreateHandler(repository, audit);

        const string sensitiveToken = "secret-plain-refresh-token-value-xyz";
        await handler.Handle(new RefreshTokenCommand(sensitiveToken), CancellationToken.None);

        Assert.All(audit.Entries, e =>
        {
            if (e.Context is not null)
            {
                var serialized = System.Text.Json.JsonSerializer.Serialize(e.Context);
                Assert.DoesNotContain(sensitiveToken, serialized);
            }
        });

        // Repository received hashed bytes, not plain string
        Assert.NotNull(repository.LastQueriedHash);
        Assert.Equal([4, 5, 6], repository.LastQueriedHash);
    }

    private static RefreshTokenCommandHandler CreateHandler(
        IAuthRepository repository,
        IAuditTrail audit)
    {
        return new RefreshTokenCommandHandler(
            repository,
            new FakeOpaqueTokenService(),
            new FakeAccessTokenService(),
            new FakeSecurityPolicy(),
            new FakeRequestContext(),
            audit,
            new FixedTimeProvider(FixedNow));
    }

    private sealed class MockAuthSessionRepository : IAuthRepository
    {
        public RefreshSession? Session { get; set; }
        public bool Rotated { get; private set; }
        public long RotatedTokenId { get; private set; }
        public RefreshTokenData? ReplacementData { get; private set; }
        public bool FamilyRevokedForReuse { get; private set; }
        public Guid RevokedFamilyId { get; private set; }
        public bool SimulateAlreadyConsumedOnRotate { get; set; }
        public byte[]? LastQueriedHash { get; private set; }

        public Task<RefreshSession?> FindRefreshSessionAsync(byte[] tokenHash, CancellationToken cancellationToken = default)
        {
            LastQueriedHash = tokenHash;
            return Task.FromResult(Session);
        }

        public Task RotateRefreshTokenAsync(
            long currentTokenId,
            RefreshTokenData replacement,
            DateTime utcNow,
            CancellationToken cancellationToken = default,
            string? expectedPasswordHash = null)
        {
            if (SimulateAlreadyConsumedOnRotate)
            {
                throw new ConflictException("The refresh token has already been consumed.");
            }

            Rotated = true;
            RotatedTokenId = currentTokenId;
            ReplacementData = replacement;
            return Task.CompletedTask;
        }

        public Task RevokeRefreshTokenFamilyForReuseAsync(
            Guid familyId,
            DateTime utcNow,
            string? revokedByIp,
            CancellationToken cancellationToken = default)
        {
            FamilyRevokedForReuse = true;
            RevokedFamilyId = familyId;
            return Task.CompletedTask;
        }

        public Task RevokeRefreshTokenAsync(byte[] tokenHash, DateTime utcNow, string? revokedByIp, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<AuthAccount?> FindByEmailAsync(string email, CancellationToken cancellationToken = default) => Task.FromResult<AuthAccount?>(null);
        public Task<AuthAccount?> FindByIdAsync(long userId, CancellationToken cancellationToken = default) => Task.FromResult<AuthAccount?>(null);
        public Task RecordFailedLoginAsync(long userId, int failedCount, DateTime? lockoutEndAtUtc, DateTime utcNow, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CompleteSuccessfulLoginAsync(long userId, DateTime lastLoginAtUtc, RefreshTokenData refreshToken, CancellationToken cancellationToken = default, string? expectedPasswordHash = null) => Task.CompletedTask;
        public Task UpdatePasswordAsync(long userId, string passwordHash, DateTime utcNow, CancellationToken cancellationToken = default, string? expectedPasswordHash = null, Func<Task>? onCompleted = null) => Task.CompletedTask;
        public Task CreatePasswordResetTokenAsync(long userId, byte[] tokenHash, DateTime expiresAtUtc, DateTime utcNow, string? requestedByIp, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<PasswordResetSession?> FindPasswordResetSessionAsync(byte[] tokenHash, CancellationToken cancellationToken = default) => Task.FromResult<PasswordResetSession?>(null);
        public Task CompletePasswordResetAsync(long tokenId, long userId, string passwordHash, DateTime utcNow, CancellationToken cancellationToken = default, Func<Task>? onCompleted = null) => Task.CompletedTask;
    }

    private sealed class FakeOpaqueTokenService : IOpaqueTokenService
    {
        public OpaqueToken Generate() => new("replacement-refresh-token", [7, 8, 9]);
        public byte[] Hash(string token) => [4, 5, 6];
    }

    private sealed class FakeAccessTokenService : IAccessTokenService
    {
        public AccessTokenResult Create(AccessTokenDescriptor descriptor) =>
            new("new-access-token", FixedNow.AddHours(1));
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
        public string? UserAgent => "unit-test-browser";
        public Guid CorrelationId => Guid.NewGuid();
    }

    private sealed class RecordingAuditTrail : IAuditTrail
    {
        public List<AuditEntry> Entries { get; } = [];
        public Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken = default)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow);
    }
}
