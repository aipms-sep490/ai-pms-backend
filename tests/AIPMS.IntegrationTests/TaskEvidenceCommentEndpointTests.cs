using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using AIPMS.Application.Abstractions.Storage;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.Deliverables.DTOs;
using AIPMS.Application.Features.TaskComments.DTOs;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Generated.Models;
using AIPMS.IntegrationTests.Supervisors;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TaskEntity = AIPMS.Infrastructure.Persistence.Generated.Models.Task;
using Task = System.Threading.Tasks.Task;

namespace AIPMS.IntegrationTests;

public sealed class TaskEvidenceCommentEndpointTests(SupervisorDatabaseFixture database)
    : IClassFixture<SupervisorDatabaseFixture>
{
    [Fact]
    public async Task Active_member_can_upload_list_download_evidence_and_comment_with_project_access_read()
    {
        var seeded = await SeedAsync();
        using var app = new TaskEvidenceFactory(database);
        using var student = app.CreateAuthenticatedClient(seeded.Student);
        using var admin = app.CreateAuthenticatedClient(seeded.Admin, roles: AppRoles.Admin);

        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("TASK"), "ParentType");
        form.Add(new StringContent(seeded.TaskId.ToString()), "ParentId");
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes("task evidence"));
        content.Headers.ContentType = new("text/plain");
        form.Add(content, "File", "evidence.txt");
        var upload = await student.PostAsync("/api/v1/files", form);
        Assert.True(upload.IsSuccessStatusCode, await upload.Content.ReadAsStringAsync());
        var file = await upload.Content.ReadFromJsonAsync<ProjectFileDto>();
        Assert.NotNull(file);

        var evidence = await admin.GetFromJsonAsync<PagedResult<ProjectFileDto>>($"/api/v1/tasks/{seeded.TaskId}/evidence");
        Assert.Single(evidence!.Items);
        var download = await admin.GetAsync($"/api/v1/files/{file!.Id}/download");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal("task evidence", await download.Content.ReadAsStringAsync());

        var comment = await student.PostAsJsonAsync($"/api/v1/tasks/{seeded.TaskId}/comments", new { content = "  Ready for review  " });
        Assert.Equal(HttpStatusCode.OK, comment.StatusCode);
        var comments = await admin.GetFromJsonAsync<PagedResult<TaskCommentDto>>($"/api/v1/tasks/{seeded.TaskId}/comments");
        Assert.Equal("Ready for review", Assert.Single(comments!.Items).Content);

        await using var db = database.CreateContext();
        Assert.Equal(1, await db.AuditLogs.CountAsync(a => a.Action == "TASK_COMMENT_CREATED"));
        Assert.Equal(1, await db.Files.CountAsync(f => f.TaskId == seeded.TaskId));
    }

    [Fact]
    public async Task Evidence_and_comments_are_rejected_after_project_leaves_active_state()
    {
        var seeded = await SeedAsync();
        using var app = new TaskEvidenceFactory(database);
        using var student = app.CreateAuthenticatedClient(seeded.Student);
        await using (var db = database.CreateContext())
        {
            (await db.Projects.FindAsync(seeded.ProjectId))!.Status = "APPROVED";
            await db.SaveChangesAsync();
        }

        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("TASK"), "ParentType");
        form.Add(new StringContent(seeded.TaskId.ToString()), "ParentId");
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes("blocked"));
        content.Headers.ContentType = new("text/plain");
        form.Add(content, "File", "blocked.txt");
        Assert.Equal(HttpStatusCode.Conflict, (await student.PostAsync("/api/v1/files", form)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await student.PostAsJsonAsync($"/api/v1/tasks/{seeded.TaskId}/comments", new { content = "blocked" })).StatusCode);
    }

    private async Task<Scenario> SeedAsync()
    {
        var accounts = await database.SeedAsync();
        await using var db = database.CreateContext();
        var organizationId = (await db.Departments.FindAsync(accounts.DepartmentId))!.OrganizationId;
        var semester = new AcademicSemester { OrganizationId = organizationId, Code = Guid.NewGuid().ToString("N"), Name = "Task semester",
            Status = "ACTIVE", StartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-5)), EndDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)) };
        var milestone = new Milestone { Project = new Project { Code = Guid.NewGuid().ToString("N"), Title = "Task project", Status = "ACTIVE",
            CreatedBy = accounts.Student, RegisteredAt = DateTime.UtcNow, Team = new Team { AcademicSemester = semester,
                Code = Guid.NewGuid().ToString("N"), Name = "Task team", Status = "ELIGIBLE", CreatedBy = accounts.Student,
                TeamMembers = [new TeamMember { UserId = accounts.Student, IsLeader = true, JoinedAt = DateTime.UtcNow }] } },
            Title = "Execution", Status = "PLANNED", CreatedBy = accounts.Student };
        var task = new TaskEntity { Milestone = milestone, Title = "Implement", Status = "TODO", Priority = "MEDIUM", CreatedBy = accounts.Student };
        db.Tasks.Add(task);
        await db.SaveChangesAsync();
        return new(accounts.Admin, accounts.Student, milestone.ProjectId, task.Id);
    }

    private sealed record Scenario(long Admin, long Student, long ProjectId, long TaskId);
}

internal sealed class TaskEvidenceFactory(SupervisorDatabaseFixture database) : AipmsWebApplicationFactory
{
    public MemoryFileStorage Storage { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
            new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = database.ConnectionString }));
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IFileStorage>();
            services.AddSingleton<IFileStorage>(Storage);
        });
    }
}

internal sealed class MemoryFileStorage : IFileStorage
{
    private readonly ConcurrentDictionary<string, byte[]> objects = new();

    public async Task WriteAsync(string key, Stream content, CancellationToken ct)
    {
        await using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct);
        if (!objects.TryAdd(key, buffer.ToArray())) throw new IOException("Object exists.");
    }

    public Task<Stream> OpenReadAsync(string key, CancellationToken ct) =>
        objects.TryGetValue(key, out var bytes)
            ? Task.FromResult<Stream>(new MemoryStream(bytes, writable: false))
            : throw new FileNotFoundException();

    public Task DeleteAsync(string key, CancellationToken ct)
    {
        objects.TryRemove(key, out _);
        return Task.CompletedTask;
    }
}
