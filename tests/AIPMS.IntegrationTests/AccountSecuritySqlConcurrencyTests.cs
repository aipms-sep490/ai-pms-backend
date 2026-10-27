using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Task = System.Threading.Tasks.Task;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.AccountSecurity.DTOs;
using AIPMS.Application.Features.AccountSecurity.Models;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Generated.Models;
using AIPMS.Infrastructure.Persistence.Repositories;
using AIPMS.IntegrationTests.Supervisors;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace AIPMS.IntegrationTests;

public sealed class AccountSecuritySqlConcurrencyTests(SupervisorDatabaseFixture database) : IClassFixture<SupervisorDatabaseFixture>
{
    private sealed class Factory(SupervisorDatabaseFixture database) : AipmsWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = database.ConnectionString
            }));
        }
    }

    [Fact]
    public async Task UpdateAccount_SimultaneousRequests_OneSucceeds_OtherReturns409_AndOnlyOneAuditLogged()
    {
        var s = await database.SeedAsync();

        // Target user to be updated concurrently
        long targetUserId;
        string originalToken;

        await using (var db = database.CreateContext())
        {
            var user = await db.Users.FindAsync(s.Student);
            Assert.NotNull(user);
            targetUserId = user.Id;
            originalToken = Convert.ToBase64String(user.RowVersion);
        }

        using var app = new Factory(database);
        using var client1 = app.CreateAuthenticatedClient(s.Admin, roles: AppRoles.Admin);
        using var client2 = app.CreateAuthenticatedClient(s.Admin, roles: AppRoles.Admin);

        var req1 = new UpdateUserAccountRequest(
            FullName: "Updated By First Worker",
            Phone: "0901111111",
            Title: "Title A",
            StudentCode: null,
            EmployeeCode: null,
            ConcurrencyToken: originalToken);

        var req2 = new UpdateUserAccountRequest(
            FullName: "Updated By Second Worker",
            Phone: "0902222222",
            Title: "Title B",
            StudentCode: null,
            EmployeeCode: null,
            ConcurrencyToken: originalToken);

        // Send two requests concurrently with the same concurrency token
        var task1 = client1.PutAsJsonAsync($"/api/v1/users/{targetUserId}", req1);
        var task2 = client2.PutAsJsonAsync($"/api/v1/users/{targetUserId}", req2);

        var responses = await Task.WhenAll(task1, task2);
        var statusCodes = responses.Select(r => r.StatusCode).ToList();

        // Exactly one must succeed (200 OK) and the other must return 409 Conflict
        Assert.Contains(HttpStatusCode.OK, statusCodes);
        Assert.Contains(HttpStatusCode.Conflict, statusCodes);

        // Verify only ONE audit record exists for the successful update, and zero for the failed one
        await using (var db = database.CreateContext())
        {
            var targetIdStr = targetUserId.ToString();
            var auditEntries = await db.AuditLogs
                .Where(a => a.EntityId == targetIdStr && a.Action == "ACCOUNT_UPDATED")
                .ToListAsync();

            Assert.Single(auditEntries);
        }
    }

    [Fact]
    public async Task UpdateAccountAsync_WhenAuditCallbackFails_RollsBackTransaction_AndDoesNotPersistChanges()
    {
        var s = await database.SeedAsync();
        long targetUserId;
        string originalName;
        string originalToken;

        await using (var db = database.CreateContext())
        {
            var user = await db.Users.FindAsync(s.Student);
            Assert.NotNull(user);
            targetUserId = user.Id;
            originalName = user.FullName;
            originalToken = Convert.ToBase64String(user.RowVersion);
        }

        await using (var db = database.CreateContext())
        {
            var repo = new AccountSecurityRepository(db);

            // Deliberately throw inside the onUpdatedAsync callback
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await repo.UpdateAccountAsync(
                    targetUserId,
                    new UpdateAccountData("Transient Failed Name", "0909999999", "Failed Title", null, null),
                    originalToken,
                    DateTime.UtcNow,
                    onUpdatedAsync: _ => throw new InvalidOperationException("Simulated Audit Logging Outage!"),
                    CancellationToken.None);
            });
        }

        // Verify changes were completely rolled back in SQL Server
        await using (var db = database.CreateContext())
        {
            var userAfter = await db.Users.FindAsync(targetUserId);
            Assert.NotNull(userAfter);
            Assert.Equal(originalName, userAfter.FullName);
        }
    }
}
