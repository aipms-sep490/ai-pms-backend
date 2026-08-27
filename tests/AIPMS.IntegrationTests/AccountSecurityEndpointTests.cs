using System.Net;
using System.Net.Http.Json;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.AccountSecurity.Abstractions;
using AIPMS.Application.Features.AccountSecurity.DTOs;
using AIPMS.Application.Features.AccountSecurity.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AIPMS.IntegrationTests;

public sealed class AccountSecurityEndpointTests
    : IClassFixture<AccountSecurityEndpointTests.AccountSecurityWebApplicationFactory>
{
    private readonly AccountSecurityWebApplicationFactory _factory;

    public AccountSecurityEndpointTests(AccountSecurityWebApplicationFactory factory) =>
        _factory = factory;

    [Fact]
    public async Task GetUsers_Administrator_ReturnsPagedAccounts()
    {
        using var client = _factory.CreateAuthenticatedClient(roles: [AppRoles.Admin]);

        var response = await client.GetAsync("/api/v1/users?page=1&pageSize=20");
        var result = await response.Content.ReadFromJsonAsync<PagedResult<UserAccountDto>>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        Assert.Single(result.Items);
        Assert.Equal("admin@aipms.test", result.Items[0].Email);
    }

    [Fact]
    public async Task GetUsers_Student_ReturnsForbidden()
    {
        using var client = _factory.CreateAuthenticatedClient(roles: [AppRoles.Student]);

        var response = await client.GetAsync("/api/v1/users");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task BlockUser_Administrator_MapsToSuspendedStatus()
    {
        using var client = _factory.CreateAuthenticatedClient(roles: [AppRoles.Admin]);

        var response = await client.PostAsync("/api/v1/users/1/block", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<UserAccountDto>();
        Assert.NotNull(result);
        Assert.Equal("SUSPENDED", result.Status);
    }

    public sealed class AccountSecurityWebApplicationFactory : AipmsWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IUserAccountRepository>();
                services.AddSingleton<IUserAccountRepository, StubUserAccountRepository>();
                services.RemoveAll<IAuditTrail>();
                services.AddSingleton<IAuditTrail, NoOpAuditTrail>();
            });
        }
    }

    private sealed class StubUserAccountRepository : IUserAccountRepository
    {
        private static readonly AccountUser Admin = new(
            1,
            null,
            null,
            "admin@aipms.test",
            "System Administrator",
            null,
            null,
            "ADMIN-001",
            "Administrator",
            "ACTIVE",
            0,
            null,
            null,
            null,
            DateTime.UtcNow,
            DateTime.UtcNow,
            [AppRoles.Admin]);

        public Task<PagedResult<AccountUser>> GetUsersAsync(string? search, string? status, int page, int pageSize, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PagedResult<AccountUser>([Admin], page, pageSize, 1));

        public Task<AccountUser?> GetUserAsync(long userId, CancellationToken cancellationToken = default) => Task.FromResult<AccountUser?>(userId == 1 ? Admin : null);
        public Task<bool> IdentityExistsAsync(string email, string? studentCode, string? employeeCode, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AccountUser> CreateUserAsync(CreateAccountData data, long actorUserId, DateTime utcNow, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<AccountUser>> CreateUsersAsync(IReadOnlyCollection<CreateAccountData> accounts, long actorUserId, DateTime utcNow, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AccountUser> UpdateProfileAsync(long userId, UpdateProfileData data, DateTime utcNow, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AccountUser> SetStatusAsync(long userId, string status, DateTime utcNow, CancellationToken cancellationToken = default) =>
            Task.FromResult(Admin with { Status = status, UpdatedAt = utcNow });
        public Task<bool> UserHasRoleAsync(long userId, string roleCode, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<int> CountActiveUsersInRoleAsync(string roleCode, CancellationToken cancellationToken = default) => Task.FromResult(2);
        public Task AssignRoleAsync(long userId, long roleId, long actorUserId, DateTime utcNow, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task RemoveRoleAsync(long userId, long roleId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class NoOpAuditTrail : IAuditTrail
    {
        public Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
