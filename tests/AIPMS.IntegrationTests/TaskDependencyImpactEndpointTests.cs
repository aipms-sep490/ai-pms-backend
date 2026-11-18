using System.Net;
using System.Net.Http.Json;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.ExecutionCapabilities.DTOs;
using AIPMS.Application.Features.Tasks.DTOs;
using AIPMS.Infrastructure.Persistence.Repositories;
using AIPMS.IntegrationTests.Supervisors;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.IntegrationTests;

public sealed class TaskDependencyImpactEndpointTests(SupervisorDatabaseFixture database) : IClassFixture<SupervisorDatabaseFixture>
{
    [Fact]
    public async Task Reopen_requires_manager_reason_and_current_token_and_impact_remains_until_explicit_review()
    {
        var s = await Seed();
        using var app = new Factory(database);
        using var leader = app.CreateAuthenticatedClient(s.Leader);
        using var member = app.CreateAuthenticatedClient(s.Member);
        using var outsider = app.CreateAuthenticatedClient(s.Outsider, roles: AppRoles.Lecturer);
        var original = await Task(leader, s.First);
        var memberReopen = await member.PutAsJsonAsync($"/api/v1/tasks/{s.First}/status",
            new { newStatus = "IN_PROGRESS", reason = "Schema changed", concurrencyToken = original.ConcurrencyToken });
        Assert.Equal(HttpStatusCode.Forbidden, memberReopen.StatusCode);
        var missingReason = await leader.PutAsJsonAsync($"/api/v1/tasks/{s.First}/status",
            new { newStatus = "IN_PROGRESS", reason = " ", concurrencyToken = original.ConcurrencyToken });
        Assert.Equal(HttpStatusCode.Conflict, missingReason.StatusCode);
        var reopened = await Change(leader, s.First, "IN_PROGRESS", "Schema changed");
        var stale = await leader.PutAsJsonAsync($"/api/v1/tasks/{s.First}/status",
            new { newStatus = "DONE", concurrencyToken = original.ConcurrencyToken });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("IN_PROGRESS", reopened.Status);
        var unchanged = await Task(leader, s.Second);
        Assert.Equal("DONE", unchanged.Status);
        var pending = await Impacts(leader, s.Second);
        var impact = Assert.Single(pending.Items);
        Assert.True(pending.CanReview);
        Assert.False(impact.CanResolve);
        Assert.Equal("PENDING", impact.Status);
        var tooEarly = await Resolve(leader, s.Second, impact.Id, "CONFIRMED_VALID", unchanged.ConcurrencyToken);
        Assert.Equal(HttpStatusCode.Conflict, tooEarly.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await outsider.GetAsync($"/api/v1/tasks/{s.Second}/dependency-impacts")).StatusCode);
        Assert.False((await Impacts(member, s.Second)).CanReview);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await Resolve(member, s.Second, impact.Id, "CONFIRMED_VALID", unchanged.ConcurrencyToken)).StatusCode);

        await Change(leader, s.First, "DONE", null);
        Assert.Equal("PENDING", Assert.Single((await Impacts(leader, s.Second)).Items).Status);
        var valid = await Resolve(leader, s.Second, impact.Id, "CONFIRMED_VALID", (await Task(leader, s.Second)).ConcurrencyToken);
        Assert.True(valid.IsSuccessStatusCode, await valid.Content.ReadAsStringAsync());
        var result = (await valid.Content.ReadFromJsonAsync<TaskDto>())!;
        Assert.Equal("DONE", result.Status);
        Assert.Equal(unchanged.CompletedAt, result.CompletedAt);
        Assert.Equal("CONFIRMED_VALID", Assert.Single((await Impacts(leader, s.Second)).Items).Status);
        Assert.Equal(HttpStatusCode.Conflict, (await Resolve(leader, s.Second, impact.Id, "REWORK_REQUIRED", result.ConcurrencyToken)).StatusCode);
        await using var db = database.CreateContext();
        Assert.True(await db.Notifications.AnyAsync(n => n.RelatedEntityType == "TASK" && n.RelatedEntityId == s.Second && n.NotificationType == "TASK_DEPENDENCY_IMPACT"));
        Assert.True(await db.AuditLogs.AnyAsync(a => a.EntityId == s.Second.ToString() && a.Action == "TASK_DEPENDENCY_IMPACT_RESOLVED"));
    }

    [Fact]
    public async Task Explicit_rework_reopens_successor_and_keeps_next_successor_done_with_new_impact()
    {
        var s = await Seed();
        using var app = new Factory(database);
        using var leader = app.CreateAuthenticatedClient(s.Leader);
        var before = await Task(leader, s.Second);
        var locked = await leader.PostAsJsonAsync($"/api/v1/tasks/{s.Second}/assignees?concurrencyToken={before.ConcurrencyToken}", new[] { s.Leader, s.Member });
        Assert.Equal(HttpStatusCode.Conflict, locked.StatusCode);
        await Change(leader, s.First, "IN_PROGRESS", "Change schema");
        await Change(leader, s.First, "DONE", null);
        var pending = Assert.Single((await Impacts(leader, s.Second)).Items);
        var response = await Resolve(leader, s.Second, pending.Id, "REWORK_REQUIRED", (await Task(leader, s.Second)).ConcurrencyToken);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var second = (await response.Content.ReadFromJsonAsync<TaskDto>())!;
        Assert.Equal("IN_PROGRESS", second.Status);
        Assert.Null(second.CompletedAt);
        Assert.Equal("REWORK_REQUIRED", Assert.Single((await Impacts(leader, s.Second)).Items).Status);
        Assert.Equal("DONE", (await Task(leader, s.Third)).Status);
        Assert.Equal("PENDING", Assert.Single((await Impacts(leader, s.Third)).Items).Status);
        var assignment = await leader.PostAsJsonAsync($"/api/v1/tasks/{s.Second}/assignees?concurrencyToken={second.ConcurrencyToken}", new[] { s.Leader, s.Member });
        Assert.True(assignment.IsSuccessStatusCode, await assignment.Content.ReadAsStringAsync());
        await Change(leader, s.Second, "IN_REVIEW", null);
        var reviewing = await Task(leader, s.Second);
        var reviewLocked = await leader.PostAsJsonAsync($"/api/v1/tasks/{s.Second}/assignees?concurrencyToken={reviewing.ConcurrencyToken}", new[] { s.Leader });
        Assert.Equal(HttpStatusCode.Conflict, reviewLocked.StatusCode);
    }

    [Fact]
    public async Task Migration_replay_is_idempotent_and_failed_reopen_preserves_every_related_record()
    {
        var s = await Seed();
        var sql = await File.ReadAllTextAsync(Path.Combine(SqlSchemaBootstrap.Root, "db", "changes", "20261118_task_dependency_impacts.sql"));
        await SqlSchemaBootstrap.ExecuteAsync(database.ConnectionString, sql);
        await SqlSchemaBootstrap.ExecuteAsync(database.ConnectionString, sql);
        await using (var db = database.CreateContext())
        {
            var repository = new TaskRepository(db);
            await Assert.ThrowsAsync<DbUpdateException>(() => repository.UpdateStatusAsync(s.First, "IN_PROGRESS", "Invalid actor", long.MaxValue, CancellationToken.None));
        }
        await using var readback = database.CreateContext();
        Assert.Equal("DONE", (await readback.Tasks.FindAsync(s.First))!.Status);
        Assert.Empty(await readback.TaskDependencyImpacts.Where(x => x.TaskId == s.Second).ToArrayAsync());
        Assert.Empty(await readback.TaskStatusHistories.Where(x => x.TaskId == s.First).ToArrayAsync());
        Assert.Empty(await readback.Notifications.Where(x => x.RelatedEntityType == "TASK" && x.RelatedEntityId == s.Second).ToArrayAsync());
    }

    [Theory]
    [InlineData("CONFIRMED_VALID", "DONE")]
    [InlineData("REWORK_REQUIRED", "IN_PROGRESS")]
    public async Task Cancelled_predecessor_supports_explicit_review_without_dead_end(string decision, string expectedStatus)
    {
        var s = await Seed();
        using var app = new Factory(database);
        using var leader = app.CreateAuthenticatedClient(s.Leader);
        await Change(leader, s.First, "CANCELLED", "Superseded schema");
        var impact = Assert.Single((await Impacts(leader, s.Second)).Items);
        Assert.True(impact.CanResolve);
        var response = await Resolve(leader, s.Second, impact.Id, decision, (await Task(leader, s.Second)).ConcurrencyToken);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        Assert.Equal(expectedStatus, (await response.Content.ReadFromJsonAsync<TaskDto>())!.Status);
        Assert.Equal(decision, Assert.Single((await Impacts(leader, s.Second)).Items).Status);
    }

    [Fact]
    public async Task Restoring_cancelled_completed_predecessor_requires_manager_reason_and_renews_review()
    {
        var s = await Seed();
        using var app = new Factory(database);
        using var leader = app.CreateAuthenticatedClient(s.Leader);
        using var member = app.CreateAuthenticatedClient(s.Member);
        await Change(leader, s.First, "CANCELLED", "Superseded");
        var impact = Assert.Single((await Impacts(leader, s.Second)).Items);
        var resolved = await Resolve(leader, s.Second, impact.Id, "CONFIRMED_VALID", (await Task(leader, s.Second)).ConcurrencyToken);
        Assert.True(resolved.IsSuccessStatusCode, await resolved.Content.ReadAsStringAsync());
        var cancelled = await Task(leader, s.First);
        var unauthorized = await member.PutAsJsonAsync($"/api/v1/tasks/{s.First}/status",
            new { newStatus = "TODO", reason = "Restore", concurrencyToken = cancelled.ConcurrencyToken });
        Assert.Equal(HttpStatusCode.Forbidden, unauthorized.StatusCode);
        var noReason = await leader.PutAsJsonAsync($"/api/v1/tasks/{s.First}/status",
            new { newStatus = "TODO", reason = " ", concurrencyToken = cancelled.ConcurrencyToken });
        Assert.Equal(HttpStatusCode.Conflict, noReason.StatusCode);
        await Change(leader, s.First, "TODO", "Restore original implementation");
        var items = (await Impacts(leader, s.Second)).Items;
        Assert.Equal(2, items.Count);
        Assert.Contains(items, x => x.Id == impact.Id && x.Status == "CONFIRMED_VALID");
        Assert.Contains(items, x => x.Id != impact.Id && x.Status == "PENDING" && !x.CanResolve);
        Assert.Equal("DONE", (await Task(leader, s.Second)).Status);
    }

    private static async Task<TaskDto> Task(HttpClient client, long id) =>
        (await client.GetFromJsonAsync<TaskDto>($"/api/v1/tasks/{id}"))!;
    private static async Task<TaskDependencyImpactsDto> Impacts(HttpClient client, long id) =>
        (await client.GetFromJsonAsync<TaskDependencyImpactsDto>($"/api/v1/tasks/{id}/dependency-impacts"))!;
    private static async Task<HttpResponseMessage> Resolve(HttpClient client, long taskId, long impactId, string decision, string? token) =>
        await client.PostAsJsonAsync($"/api/v1/tasks/{taskId}/dependency-impacts/{impactId}/resolve",
            new { decision, reason = "Reviewed schema compatibility", concurrencyToken = token });
    private static async Task<TaskDto> Change(HttpClient client, long id, string status, string? reason)
    {
        var task = await Task(client, id);
        var response = await client.PutAsJsonAsync($"/api/v1/tasks/{id}/status", new { newStatus = status, reason, concurrencyToken = task.ConcurrencyToken });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<TaskDto>())!;
    }

    private async Task<Scenario> Seed()
    {
        var accounts = await database.SeedAsync();
        await using var db = database.CreateContext();
        var organizationId = (await db.Departments.FindAsync(accounts.DepartmentId))!.OrganizationId;
        var member = new M.User { Email = $"task-member-{Guid.NewGuid():N}@example.test", FullName = "Member", PasswordHash = "unused",
            Status = "ACTIVE", AcademicProfileStatus = "VERIFIED", DepartmentId = accounts.DepartmentId,
            UserRoleUsers = [new M.UserRole { RoleId = await db.Roles.Where(r => r.Code == AppRoles.Student).Select(r => r.Id).SingleAsync() }] };
        var semester = new M.AcademicSemester { OrganizationId = organizationId, Code = Guid.NewGuid().ToString("N"), Name = "Tasks",
            Status = "ACTIVE", StartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-5)), EndDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)) };
        var milestone = new M.Milestone { Project = new M.Project { Code = Guid.NewGuid().ToString("N"), Title = "Task project", Status = "ACTIVE",
            CreatedBy = accounts.Student, RegisteredAt = DateTime.UtcNow, Team = new M.Team { AcademicSemester = semester,
                Code = Guid.NewGuid().ToString("N"), Name = "Task team", Status = "ELIGIBLE", CreatedBy = accounts.Student,
                TeamMembers = [new M.TeamMember { UserId = accounts.Student, IsLeader = true, JoinedAt = DateTime.UtcNow },
                    new M.TeamMember { User = member, JoinedAt = DateTime.UtcNow }] } },
            Title = "Execution", Status = "PLANNED", CreatedBy = accounts.Student };
        M.Task New(string title) => new() { Milestone = milestone, Title = title, Status = "DONE", Priority = "MEDIUM", CreatedBy = accounts.Student,
            CompletedAt = DateTime.UtcNow.AddDays(-1), TaskAssignees = [new M.TaskAssignee { User = member, AssignedBy = accounts.Student, AssignedAt = DateTime.UtcNow.AddDays(-2) }] };
        var first = New("Schema"); var second = New("SQL"); var third = New("Integration");
        second.TaskDependencyTasks.Add(new M.TaskDependency { DependsOnTask = first, DependencyType = "FINISH_TO_START" });
        third.TaskDependencyTasks.Add(new M.TaskDependency { DependsOnTask = second, DependencyType = "FINISH_TO_START" });
        db.Tasks.AddRange(first, second, third);
        await db.SaveChangesAsync();
        return new(accounts.Student, member.Id, accounts.OtherLecturer, first.Id, second.Id, third.Id);
    }
    private sealed record Scenario(long Leader, long Member, long Outsider, long First, long Second, long Third);
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
