using System.Net;
using System.Net.Http.Json;
using AIPMS.Application.Features.Milestones.DTOs;
using AIPMS.Infrastructure.Persistence.Repositories;
using AIPMS.IntegrationTests.Supervisors;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.IntegrationTests;

public sealed class MilestoneDependencySqlTests(SupervisorDatabaseFixture database) : IClassFixture<SupervisorDatabaseFixture>
{
    [Fact]
    public async Task Dependency_updates_rotate_token_reject_stale_cycle_and_protected_delete()
    {
        var s = await Seed();
        using var app = new Factory(database);
        using var leader = app.CreateAuthenticatedClient(s.Leader);
        var before = (await leader.GetFromJsonAsync<MilestoneDto>($"/api/v1/milestones/{s.Second}"))!;
        var added = await leader.PostAsJsonAsync("/api/v1/milestones/dependency",
            new { milestoneId = s.Second, dependsOnMilestoneId = s.First, concurrencyToken = before.ConcurrencyToken });
        Assert.True(added.IsSuccessStatusCode, await added.Content.ReadAsStringAsync());
        var after = (await added.Content.ReadFromJsonAsync<MilestoneDto>())!;
        Assert.NotEqual(before.ConcurrencyToken, after.ConcurrencyToken);
        Assert.Equal(s.First, Assert.Single(after.Dependencies!).DependsOnMilestoneId);
        Assert.Equal(HttpStatusCode.Conflict, (await leader.PostAsJsonAsync("/api/v1/milestones/dependency",
            new { milestoneId = s.Second, dependsOnMilestoneId = s.First, concurrencyToken = before.ConcurrencyToken })).StatusCode);
        var first = (await leader.GetFromJsonAsync<MilestoneDto>($"/api/v1/milestones/{s.First}"))!;
        Assert.Equal(HttpStatusCode.Conflict, (await leader.PostAsJsonAsync("/api/v1/milestones/dependency",
            new { milestoneId = s.First, dependsOnMilestoneId = s.Second, concurrencyToken = first.ConcurrencyToken })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await leader.DeleteAsync($"/api/v1/milestones/{s.First}?concurrencyToken={first.ConcurrencyToken}")).StatusCode);
        var removed = await leader.DeleteAsync($"/api/v1/milestones/{s.Second}/dependency/{s.First}?concurrencyToken={after.ConcurrencyToken}");
        Assert.True(removed.IsSuccessStatusCode, await removed.Content.ReadAsStringAsync());
        var final = (await removed.Content.ReadFromJsonAsync<MilestoneDto>())!;
        Assert.Empty(final.Dependencies!);
        Assert.NotEqual(after.ConcurrencyToken, final.ConcurrencyToken);
        Assert.Equal(HttpStatusCode.NoContent,
            (await leader.DeleteAsync($"/api/v1/milestones/{s.First}?concurrencyToken={first.ConcurrencyToken}")).StatusCode);
    }

    [Fact]
    public async Task Concurrent_opposite_edges_commit_at_most_one_and_unauthorized_user_cannot_write()
    {
        var s = await Seed();
        using var app = new Factory(database);
        using var leader = app.CreateAuthenticatedClient(s.Leader);
        using var outsider = app.CreateAuthenticatedClient(s.Outsider);
        var first = (await leader.GetFromJsonAsync<MilestoneDto>($"/api/v1/milestones/{s.First}"))!;
        var second = (await leader.GetFromJsonAsync<MilestoneDto>($"/api/v1/milestones/{s.Second}"))!;
        Assert.Equal(HttpStatusCode.Forbidden, (await outsider.PostAsJsonAsync("/api/v1/milestones/dependency",
            new { milestoneId = s.Second, dependsOnMilestoneId = s.First, concurrencyToken = second.ConcurrencyToken })).StatusCode);
        var responses = await Task.WhenAll(
            leader.PostAsJsonAsync("/api/v1/milestones/dependency", new { milestoneId = s.First, dependsOnMilestoneId = s.Second, concurrencyToken = first.ConcurrencyToken }),
            leader.PostAsJsonAsync("/api/v1/milestones/dependency", new { milestoneId = s.Second, dependsOnMilestoneId = s.First, concurrencyToken = second.ConcurrencyToken }));
        Assert.Single(responses, r => r.IsSuccessStatusCode);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
        await using var readback = database.CreateContext();
        Assert.Single(await readback.MilestoneDependencies.Where(d => d.MilestoneId == s.First || d.MilestoneId == s.Second).ToArrayAsync());
    }

    [Fact]
    public async Task Sql_lock_requires_transaction_and_succeeds_inside_owned_transaction()
    {
        await using var db = database.CreateContext();
        var repository = new MilestoneRepository(db);
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.AcquireProjectDependencyLockAsync(1, default));
        await using var tx = await db.Database.BeginTransactionAsync();
        await repository.AcquireProjectDependencyLockAsync(1, default);
        await tx.RollbackAsync();
    }

    [Fact]
    public async Task Legacy_database_upgrade_and_replay_preserve_rows_and_enforce_constraints()
    {
        await using var isolated = new IsolatedSqlDatabase();
        await isolated.StartAsync(Environment.GetEnvironmentVariable("AIPMS_TEST_SQL_CONNECTION"), bootstrap: false);
        await SqlSchemaBootstrap.ExecuteAsync(isolated.ConnectionString,
            "CREATE TABLE dbo.milestones(id bigint PRIMARY KEY); INSERT dbo.milestones VALUES(1),(2);");
        var sql = await File.ReadAllTextAsync(Path.Combine(SqlSchemaBootstrap.Root, "db", "changes", "20261118_milestone_dependencies.sql"));
        await SqlSchemaBootstrap.ExecuteAsync(isolated.ConnectionString, sql);
        await SqlSchemaBootstrap.ExecuteAsync(isolated.ConnectionString,
            "INSERT dbo.milestone_dependencies(milestone_id,depends_on_milestone_id) VALUES(2,1);");
        await SqlSchemaBootstrap.ExecuteAsync(isolated.ConnectionString, sql);
        await using var connection = new SqlConnection(isolated.ConnectionString);
        await connection.OpenAsync();
        await using var count = new SqlCommand("SELECT COUNT(*) FROM dbo.milestone_dependencies WHERE milestone_id=2 AND depends_on_milestone_id=1", connection);
        Assert.Equal(1, (int)(await count.ExecuteScalarAsync())!);
        foreach (var invalid in new[] { "VALUES(1,1,N'FINISH_TO_START')", "VALUES(2,1,N'FINISH_TO_START')", "VALUES(1,2,N'START_TO_START')", "VALUES(99,1,N'FINISH_TO_START')" })
        {
            await using var command = new SqlCommand("INSERT dbo.milestone_dependencies(milestone_id,depends_on_milestone_id,dependency_type) " + invalid, connection);
            await Assert.ThrowsAsync<SqlException>(() => command.ExecuteNonQueryAsync());
        }
    }

    private async Task<(long Leader, long Outsider, long First, long Second)> Seed()
    {
        var accounts = await database.SeedAsync();
        await using var db = database.CreateContext();
        var organizationId = (await db.Departments.FindAsync(accounts.DepartmentId))!.OrganizationId;
        var semester = new M.AcademicSemester { OrganizationId = organizationId, Code = Guid.NewGuid().ToString("N"), Name = "Dependencies",
            Status = "ACTIVE", StartDate = new(2026, 1, 1), EndDate = new(2026, 12, 31) };
        var project = new M.Project { Code = Guid.NewGuid().ToString("N"), Title = "Dependency test", Status = "ACTIVE", CreatedBy = accounts.Student,
            Team = new M.Team { AcademicSemester = semester, Code = Guid.NewGuid().ToString("N"), Name = "Team", Status = "ELIGIBLE", CreatedBy = accounts.Student,
                TeamMembers = [new M.TeamMember { UserId = accounts.Student, IsLeader = true, JoinedAt = DateTime.UtcNow }] } };
        var first = new M.Milestone { Project = project, Title = "First", Status = "PLANNED", CreatedBy = accounts.Student };
        var second = new M.Milestone { Project = project, Title = "Second", Status = "PLANNED", CreatedBy = accounts.Student };
        db.Milestones.AddRange(first, second);
        await db.SaveChangesAsync();
        return (accounts.Student, accounts.OtherLecturer, first.Id, second.Id);
    }

    private sealed class Factory(SupervisorDatabaseFixture database) : AipmsWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = database.ConnectionString,
                ["ExecutionConcurrency:RequireTokens"] = "true"
            }));
        }
    }
}
