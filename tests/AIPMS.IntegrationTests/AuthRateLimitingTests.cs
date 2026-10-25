using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Features.Auth.Abstractions;
using AIPMS.Application.Features.Auth.Commands;
using AIPMS.Application.Features.Auth.DTOs;
using AIPMS.Application.Features.Auth.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AIPMS.IntegrationTests;

public sealed class AuthRateLimitingTests : IClassFixture<AuthRateLimitingTests.RateLimitingTestFactory>
{
    private readonly RateLimitingTestFactory _factory;

    public AuthRateLimitingTests(RateLimitingTestFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Logins_WithDifferentEmails_ArePartitionedSeparately()
    {
        using var client = _factory.CreateClient();

        for (var i = 1; i <= 7; i++)
        {
            var response = await client.PostAsJsonAsync(
                "/api/v1/auth/login",
                new LoginCommand($"user{i}@aipms.test", "InvalidPassword@123"));

            Assert.NotEqual(HttpStatusCode.TooManyRequests, response.StatusCode);
        }
    }

    [Fact]
    public async Task Logins_ExceedingLimitForSameEmail_Returns429WithRetryAfterAndCode()
    {
        using var client = _factory.CreateClient();
        var targetEmail = "repeated_throttle@aipms.test";

        HttpResponseMessage? lastResponse = null;
        for (var i = 0; i < 4; i++)
        {
            lastResponse = await client.PostAsJsonAsync(
                "/api/v1/auth/login",
                new LoginCommand(targetEmail, "WrongPassword@123"));
        }

        Assert.NotNull(lastResponse);
        Assert.Equal(HttpStatusCode.TooManyRequests, lastResponse.StatusCode);
        Assert.True(lastResponse.Headers.Contains("Retry-After"));

        var content = await lastResponse.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);
        Assert.True(doc.RootElement.TryGetProperty("code", out var codeProp));
        Assert.Equal("RATE_LIMITED", codeProp.GetString());
    }

    [Fact]
    public async Task GetCurrentUser_IsNotThrottledByAuthPolicy()
    {
        using var client = _factory.CreateAuthenticatedClient(
            userId: 2001,
            email: "student@aipms.test",
            fullName: "Integration Test Student",
            "STUDENT");

        for (var i = 0; i < 15; i++)
        {
            var response = await client.GetAsync("/api/v1/auth/me");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Fact]
    public async Task Logout_IsNotThrottledByAuthPolicy()
    {
        using var client = _factory.CreateClient();

        for (var i = 0; i < 10; i++)
        {
            var response = await client.PostAsJsonAsync(
                "/api/v1/auth/logout",
                new LogoutCommand("some-refresh-token"));

            Assert.NotEqual(HttpStatusCode.TooManyRequests, response.StatusCode);
        }
    }

    public sealed class RateLimitingTestFactory : AipmsWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["RateLimiting:Authentication:PermitLimit"] = "3",
                    ["RateLimiting:Authentication:LoopbackPermitLimit"] = "3",
                    ["RateLimiting:Authentication:WindowMinutes"] = "1"
                });
            });
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IAuthRepository>();
                services.AddSingleton<IAuthRepository, NoOpAuthRepository>();
                services.RemoveAll<IAuditTrail>();
                services.AddSingleton<IAuditTrail, NoOpAuditTrail>();
            });
        }
    }

    private sealed class NoOpAuthRepository : IAuthRepository
    {
        public Task<AuthAccount?> FindByEmailAsync(string email, CancellationToken cancellationToken = default) =>
            Task.FromResult<AuthAccount?>(null);

        public Task<AuthAccount?> FindByIdAsync(long userId, CancellationToken cancellationToken = default) =>
            Task.FromResult<AuthAccount?>(new AuthAccount(
                userId,
                "student@aipms.test",
                "hash",
                "Student",
                "ACTIVE",
                ["STUDENT"],
                0,
                null,
                null));

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

    private sealed class NoOpAuditTrail : IAuditTrail
    {
        public Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
