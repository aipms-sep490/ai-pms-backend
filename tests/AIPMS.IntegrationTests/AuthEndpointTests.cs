using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
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
            });
        }
    }

    public sealed class TestAuthRepository : IAuthRepository
    {
        private const string ExistingDatabaseHash =
            "AQAAAAIAAYagAAAAECDlj2anj0PYyt+p+4Y/ZoHOJK1yaPX5R0QW/kB8Q+7+HZfcCfIn2WbJH4rtYP+2sg==";

        public DateTime? LastLoginAtUtc { get; private set; }

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
                    ["STUDENT"])
                : null;

            return Task.FromResult(account);
        }

        public Task UpdateLastLoginAsync(
            long userId,
            DateTime lastLoginAtUtc,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal(2001, userId);
            LastLoginAtUtc = lastLoginAtUtc;
            return Task.CompletedTask;
        }
    }
}
