using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Features.Auth.Abstractions;
using AIPMS.Application.Features.Auth.Commands.Login;
using AIPMS.Application.Features.Auth.DTOs;
using AIPMS.Application.Features.Auth.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AIPMS.IntegrationTests;

public sealed class AuthEndpointTests : IClassFixture<AuthEndpointTests.AuthWebApplicationFactory>
{
    private readonly AuthWebApplicationFactory _factory;

    public AuthEndpointTests(AuthWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Login_ValidCredentials_ReturnsJwtThatCanAccessCurrentUser()
    {
        using var client = _factory.CreateClient();

        var loginResponse = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new LoginCommand("student@aipms.test", "Aipms@123"));
        var login = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();

        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        Assert.NotNull(login);
        Assert.False(string.IsNullOrWhiteSpace(login.AccessToken));
        Assert.Equal("Bearer", login.TokenType);
        Assert.False(string.IsNullOrWhiteSpace(login.RefreshToken));

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            login.AccessToken);

        var meResponse = await client.GetAsync("/api/v1/auth/me");
        var currentUser = await meResponse.Content.ReadFromJsonAsync<AuthUserDto>();

        Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);
        Assert.NotNull(currentUser);
        Assert.Equal(2001, currentUser.Id);
        Assert.Equal("student@aipms.test", currentUser.Email);
        Assert.Contains("STUDENT", currentUser.Roles);
        Assert.NotNull(_factory.Repository.LastLoginAtUtc);
    }

    [Fact]
    public async Task Login_InvalidPassword_ReturnsUnauthorizedProblemDetails()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new LoginCommand("student@aipms.test", "wrong-password"));
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Equal("Authentication failed.", problem.Title);
        Assert.True(problem.Extensions.ContainsKey("traceId"));
    }

    [Fact]
    public async Task Refresh_ReusedToken_RevokesTokenFamily()
    {
        using var client = _factory.CreateClient();
        _factory.Repository.ResetRefreshState();

        var loginResponse = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new LoginCommand("student@aipms.test", "Aipms@123"));
        var login = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(login);

        var refreshResponse = await client.PostAsJsonAsync(
            "/api/v1/auth/refresh",
            new { refreshToken = login.RefreshToken });
        var refreshed = await refreshResponse.Content.ReadFromJsonAsync<LoginResponse>();

        Assert.Equal(HttpStatusCode.OK, refreshResponse.StatusCode);
        Assert.NotNull(refreshed);
        Assert.NotEqual(login.RefreshToken, refreshed.RefreshToken);
        Assert.Equal(1, _factory.Repository.RotationCount);

        var reuseResponse = await client.PostAsJsonAsync(
            "/api/v1/auth/refresh",
            new { refreshToken = login.RefreshToken });

        Assert.Equal(HttpStatusCode.Unauthorized, reuseResponse.StatusCode);
        Assert.True(_factory.Repository.FamilyReuseDetected);
    }

    [Fact]
    public async Task Logout_RefreshToken_RevokesSession()
    {
        using var client = _factory.CreateClient();
        _factory.Repository.ResetRefreshState();

        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/logout",
            new { refreshToken = "valid-refresh-token" });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.True(_factory.Repository.RefreshTokenExplicitlyRevoked);
    }

    [Fact]
    public async Task ResetPassword_ValidToken_ConsumesToken()
    {
        using var client = _factory.CreateClient();
        _factory.Repository.ResetPasswordState();

        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/reset-password",
            new { token = "valid-reset-token", newPassword = "NewPassword@123" });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.True(_factory.Repository.PasswordResetCompleted);
    }

    public sealed class AuthWebApplicationFactory : AipmsWebApplicationFactory
    {
        public TestAuthRepository Repository { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IAuthRepository>();
                services.AddSingleton<IAuthRepository>(Repository);
                services.RemoveAll<IAuditTrail>();
                services.AddSingleton<IAuditTrail, NoOpAuditTrail>();
            });
        }
    }

    public sealed class TestAuthRepository : IAuthRepository
    {
        private const string ExistingDatabaseHash =
            "AQAAAAIAAYagAAAAECDlj2anj0PYyt+p+4Y/ZoHOJK1yaPX5R0QW/kB8Q+7+HZfcCfIn2WbJH4rtYP+2sg==";

        public DateTime? LastLoginAtUtc { get; private set; }

        public bool RefreshSessionRevoked { get; private set; }

        public bool FamilyReuseDetected { get; private set; }

        public bool RefreshTokenExplicitlyRevoked { get; private set; }

        public bool PasswordResetCompleted { get; private set; }

        public int RotationCount { get; private set; }

        public void ResetRefreshState()
        {
            RefreshSessionRevoked = false;
            FamilyReuseDetected = false;
            RefreshTokenExplicitlyRevoked = false;
            RotationCount = 0;
        }

        public void ResetPasswordState() => PasswordResetCompleted = false;

        public Task<AuthAccount?> FindByEmailAsync(
            string email,
            CancellationToken cancellationToken = default)
        {
            AuthAccount? account = string.Equals(
                email,
                "student@aipms.test",
                StringComparison.OrdinalIgnoreCase)
                ? new AuthAccount(
                    2001,
                    "student@aipms.test",
                    ExistingDatabaseHash,
                    "Integration Test Student",
                    "ACTIVE",
                    ["STUDENT"],
                    0,
                    null,
                    null)
                : null;

            return Task.FromResult(account);
        }

        public Task<AuthAccount?> FindByIdAsync(
            long userId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(userId == 2001
                ? new AuthAccount(
                    2001,
                    "student@aipms.test",
                    ExistingDatabaseHash,
                    "Integration Test Student",
                    "ACTIVE",
                    ["STUDENT"],
                    0,
                    null,
                    null)
                : null);

        public Task RecordFailedLoginAsync(
            long userId,
            int failedCount,
            DateTime? lockoutEndAtUtc,
            DateTime utcNow,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task CompleteSuccessfulLoginAsync(
            long userId,
            DateTime lastLoginAtUtc,
            RefreshTokenData refreshToken,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal(2001, userId);
            LastLoginAtUtc = lastLoginAtUtc;
            return Task.CompletedTask;
        }

        public Task<RefreshSession?> FindRefreshSessionAsync(
            byte[] tokenHash,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<RefreshSession?>(new RefreshSession(
                9001,
                Guid.Parse("11111111-1111-1111-1111-111111111111"),
                DateTime.UtcNow.AddDays(1),
                RefreshSessionRevoked ? DateTime.UtcNow : null,
                null,
                new AuthAccount(
                    2001,
                    "student@aipms.test",
                    ExistingDatabaseHash,
                    "Integration Test Student",
                    "ACTIVE",
                    ["STUDENT"],
                    0,
                    null,
                    null)));

        public Task RotateRefreshTokenAsync(
            long currentTokenId,
            RefreshTokenData replacement,
            DateTime utcNow,
            CancellationToken cancellationToken = default)
        {
            RefreshSessionRevoked = true;
            RotationCount++;
            return Task.CompletedTask;
        }
        public Task RevokeRefreshTokenAsync(
            byte[] tokenHash,
            DateTime utcNow,
            string? revokedByIp,
            CancellationToken cancellationToken = default)
        {
            RefreshTokenExplicitlyRevoked = true;
            return Task.CompletedTask;
        }
        public Task RevokeRefreshTokenFamilyForReuseAsync(
            Guid familyId,
            DateTime utcNow,
            string? revokedByIp,
            CancellationToken cancellationToken = default)
        {
            FamilyReuseDetected = true;
            return Task.CompletedTask;
        }
        public Task UpdatePasswordAsync(long userId, string passwordHash, DateTime utcNow, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task CreatePasswordResetTokenAsync(long userId, byte[] tokenHash, DateTime expiresAtUtc, DateTime utcNow, string? requestedByIp, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PasswordResetSession?> FindPasswordResetSessionAsync(
            byte[] tokenHash,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<PasswordResetSession?>(new PasswordResetSession(
                7001,
                2001,
                DateTime.UtcNow.AddMinutes(30),
                null));

        public Task CompletePasswordResetAsync(
            long tokenId,
            long userId,
            string passwordHash,
            DateTime utcNow,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal(7001, tokenId);
            Assert.Equal(2001, userId);
            PasswordResetCompleted = true;
            return Task.CompletedTask;
        }
    }

    public sealed class NoOpAuditTrail : IAuditTrail
    {
        public Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
