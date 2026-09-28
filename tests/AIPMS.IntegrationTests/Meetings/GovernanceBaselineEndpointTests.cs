using System.Net;
using System.Net.Http.Json;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Meetings.DTOs;
using AIPMS.Application.Features.Milestones.DTOs;
using AIPMS.Application.Features.ProgressReports.DTOs;
using AIPMS.Application.Features.Tasks.DTOs;
using AIPMS.IntegrationTests.Supervisors;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.IntegrationTests.Meetings;

public sealed class GovernanceBaselineEndpointTests(SupervisorDatabaseFixture database) : IClassFixture<SupervisorDatabaseFixture>
{
    private sealed class Factory(SupervisorDatabaseFixture database, bool failAudit = false, bool requireTokens = true) : AipmsWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = database.ConnectionString,
                ["ExecutionConcurrency:RequireTokens"] = requireTokens.ToString(),
                ["NotificationEmail:Enabled"] = "false", ["ScheduledNotifications:Enabled"] = "false"
            }));
            if (failAudit) builder.ConfigureServices(s => { s.RemoveAll<IAuditTrail>(); s.AddSingleton<IAuditTrail, FailingAudit>(); });
        }
    }

    private sealed class FailingAudit : IAuditTrail
    {
        public Task RecordAsync(AuditEntry entry, CancellationToken ct = default) => throw new InvalidOperationException("Injected audit failure.");
    }

    private sealed record Scenario(SupervisorScenario Users, long Project, long Meeting, long Milestone, long Task, long Report);

    private async Task<Scenario> Seed()
    {
        var s = await database.SeedAsync();
        await using var db = database.CreateContext();
        var organization = await db.Departments.Where(d => d.Id == s.DepartmentId).Select(d => d.OrganizationId).SingleAsync();
        var now = DateTime.UtcNow;
        var semester = new M.AcademicSemester { OrganizationId = organization, Code = Guid.NewGuid().ToString("N"),
            Name = "Governance run", Status = "ACTIVE", StartDate = DateOnly.FromDateTime(now.AddDays(-10)), EndDate = DateOnly.FromDateTime(now.AddDays(90)) };
        db.AcademicSemesters.Add(semester);
        await db.SaveChangesAsync();
        var project = new M.Project { Code = Guid.NewGuid().ToString("N"), Title = "Governance fixture", CreatedBy = s.Student,
            Status = "ACTIVE", Team = new() { AcademicSemesterId = semester.Id, Code = Guid.NewGuid().ToString("N"), Name = "Team", CreatedBy = s.Student, Status = "LOCKED",
                TeamMembers = [new() { AcademicSemesterId = semester.Id, UserId = s.Student, IsLeader = true }] } };
        db.Projects.Add(project); await db.SaveChangesAsync();
        var request = new M.SupervisorRequest { ProjectId = project.Id, SupervisorProfileId = s.ProfileId, RequestedBy = s.Student, Status = "ACCEPTED" };
        db.SupervisorRequests.Add(request); await db.SaveChangesAsync();
        db.SupervisorAssignments.Add(new() { ProjectId = project.Id, SupervisorProfileId = s.ProfileId, SupervisorRequestId = request.Id, IsPrimary = true });
        db.ProjectPeriods.Add(new() { AcademicSemesterId = semester.Id, Code = "EXEC", Name = "Execution", PeriodType = "EXECUTION", Status = "ACTIVE", StartAt = now.AddDays(-10), EndAt = now.AddDays(90) });
        var meeting = new M.Meeting { ProjectId = project.Id, CreatedBy = s.Student, Title = "Completed meeting", Status = "COMPLETED", StartAt = now.AddDays(-1) };
        var milestone = new M.Milestone { ProjectId = project.Id, CreatedBy = s.Student, Title = "Milestone", Status = "PLANNED" };
        var task = new M.Task { Milestone = milestone, CreatedBy = s.Student, Title = "Task", Status = "TODO", Priority = "MEDIUM" };
        var report = new M.ProgressReport { ProjectId = project.Id, SubmittedBy = s.Student, ReportType = "WEEKLY", Status = "DRAFT", Summary = "Before",
            PeriodStart = DateOnly.FromDateTime(now.AddDays(-7)), PeriodEnd = DateOnly.FromDateTime(now.AddDays(-1)) };
        db.Meetings.Add(meeting); db.Tasks.Add(task); db.ProgressReports.Add(report); await db.SaveChangesAsync();
        return new(s, project.Id, meeting.Id, milestone.Id, task.Id, report.Id);
    }

    [Theory]
    [InlineData("task")]
    [InlineData("milestone")]
    [InlineData("report")]
    [InlineData("meeting")]
    public async Task Two_writers_one_wins_and_stale_write_has_no_audit(string resource)
    {
        var s = await Seed(); using var factory = new Factory(database);
        using var client = factory.CreateAuthenticatedClient(s.Users.Student);
        string url; object body;
        if (resource == "task")
        {
            var x = (await client.GetFromJsonAsync<TaskDto>($"/api/v1/tasks/{s.Task}"))!;
            url = $"/api/v1/tasks/{s.Task}/status"; body = new { newStatus = "IN_PROGRESS", reason = "Progress", x.ConcurrencyToken };
        }
        else if (resource == "milestone")
        {
            var x = (await client.GetFromJsonAsync<MilestoneDto>($"/api/v1/milestones/{s.Milestone}"))!;
            url = $"/api/v1/milestones/{s.Milestone}"; body = new { title = "After", description = "Changed", startDate = (string?)null, dueDate = (string?)null, status = "IN_PROGRESS", sortOrder = 1, x.ConcurrencyToken };
        }
        else if (resource == "report")
        {
            var x = (await client.GetFromJsonAsync<ProgressReportDetailDto>($"/api/v1/progress-reports/{s.Report}"))!;
            url = $"/api/v1/progress-reports/{s.Report}"; body = new UpdateProgressReportRequest("After", null, null, null, x.ConcurrencyToken);
        }
        else
        {
            var x = (await client.GetFromJsonAsync<MeetingDetailDto>($"/api/v1/meetings/{s.Meeting}"))!;
            url = $"/api/v1/meetings/{s.Meeting}/notes"; body = new UpdateMeetingNotesRequest("After", null, x.ConcurrencyToken);
        }
        var replies = await Task.WhenAll(client.PutAsJsonAsync(url, body), client.PutAsJsonAsync(url, body));
        Assert.Single(replies.Where(x => x.StatusCode == HttpStatusCode.OK));
        Assert.Single(replies.Where(x => x.StatusCode == HttpStatusCode.Conflict));
        await using var db = database.CreateContext();
        Assert.Equal(1, await db.AuditLogs.CountAsync(x => x.ActorUserId == s.Users.Student));
    }

    [Fact]
    public async Task Decision_is_append_only_and_actions_enforce_token_assignee_and_cancelled_state()
    {
        var s = await Seed(); using var factory = new Factory(database); using var client = factory.CreateAuthenticatedClient(s.Users.Student);
        var root = $"/api/v1/meetings/{s.Meeting}";
        var meeting = (await client.GetFromJsonAsync<MeetingDetailDto>(root))!;
        var create = new CreateMeetingDecisionRequest("  Approved proposal outline  ", meeting.ConcurrencyToken!);
        var reply = await client.PostAsJsonAsync(root + "/decisions", create);
        Assert.Equal(HttpStatusCode.OK, reply.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(root + "/decisions", create)).StatusCode);
        var list = (await client.GetFromJsonAsync<PagedResult<MeetingDecisionDto>>(root + "/decisions"))!;
        Assert.Equal("Approved proposal outline", Assert.Single(list.Items).Content);
        meeting = (await client.GetFromJsonAsync<MeetingDetailDto>(root))!;
        var actionInput = new SaveMeetingActionItemRequest("Prepare evidence", null, s.Users.Student, DateTime.UtcNow.AddDays(1), "OPEN", meeting.ConcurrencyToken!);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(root + "/action-items", actionInput with { AssigneeUserId = s.Users.OtherLecturer })).StatusCode);
        var actionResponse = await client.PostAsJsonAsync(root + "/action-items", actionInput);
        Assert.Equal(HttpStatusCode.OK, actionResponse.StatusCode);
        var action = (await actionResponse.Content.ReadFromJsonAsync<MeetingActionItemDto>())!;
        var update = actionInput with { Status = "DONE", ConcurrencyToken = action.ConcurrencyToken };
        var replies = await Task.WhenAll(client.PutAsJsonAsync(root + $"/action-items/{action.Id}", update), client.PutAsJsonAsync(root + $"/action-items/{action.Id}", update));
        Assert.Single(replies.Where(x => x.StatusCode == HttpStatusCode.OK));
        Assert.Single(replies.Where(x => x.StatusCode == HttpStatusCode.Conflict));
        await using var db = database.CreateContext();
        var row = await db.Meetings.FindAsync(s.Meeting); row!.Status = "CANCELLED"; await db.SaveChangesAsync();
        var token = row.ConcurrencyToken.ToString("N");
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(root + "/decisions", create with { ConcurrencyToken = token })).StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Task_audit_failure_rolls_back_status_history_and_token_with_or_without_required_tokens(bool requireTokens)
    {
        var s = await Seed();
        using var normal = new Factory(database, requireTokens: requireTokens);
        using var client = normal.CreateAuthenticatedClient(s.Users.Student);
        var before = (await client.GetFromJsonAsync<TaskDto>($"/api/v1/tasks/{s.Task}"))!;
        using var broken = new Factory(database, failAudit: true, requireTokens: requireTokens);
        using var writer = broken.CreateAuthenticatedClient(s.Users.Student);
        var response = await writer.PutAsJsonAsync($"/api/v1/tasks/{s.Task}/status",
            new { newStatus = "IN_PROGRESS", reason = "Must roll back", concurrencyToken = requireTokens ? before.ConcurrencyToken : null });
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var after = (await client.GetFromJsonAsync<TaskDto>($"/api/v1/tasks/{s.Task}"))!;
        Assert.Equal(before.Status, after.Status);
        Assert.Equal(before.ConcurrencyToken, after.ConcurrencyToken);
        await using var db = database.CreateContext();
        Assert.False(await db.TaskStatusHistories.AnyAsync(x => x.TaskId == s.Task));
        Assert.False(await db.AuditLogs.AnyAsync(x => x.ActorUserId == s.Users.Student));
    }

    [Theory]
    [InlineData(true, null, HttpStatusCode.Conflict)]
    [InlineData(false, null, HttpStatusCode.OK)]
    [InlineData(false, "invalid", HttpStatusCode.Conflict)]
    [InlineData(false, "00000000-0000-0000-0000-000000000001", HttpStatusCode.Conflict)]
    public async Task Token_rollout_requires_tokens_only_in_strict_mode_but_always_enforces_supplied_tokens(
        bool requireTokens, string? token, HttpStatusCode expected)
    {
        var s = await Seed();
        using var factory = new Factory(database, requireTokens: requireTokens);
        using var client = factory.CreateAuthenticatedClient(s.Users.Student);
        var before = (await client.GetFromJsonAsync<TaskDto>($"/api/v1/tasks/{s.Task}"))!;
        var response = await client.PutAsJsonAsync($"/api/v1/tasks/{s.Task}/status",
            new { newStatus = "IN_PROGRESS", reason = "Rollout", concurrencyToken = token });
        Assert.Equal(expected, response.StatusCode);
        var after = (await client.GetFromJsonAsync<TaskDto>($"/api/v1/tasks/{s.Task}"))!;
        if (expected == HttpStatusCode.OK)
        {
            Assert.Equal("IN_PROGRESS", after.Status);
            Assert.NotEqual(before.ConcurrencyToken, after.ConcurrencyToken);
        }
        else
        {
            Assert.Equal(before.Status, after.Status);
            Assert.Equal(before.ConcurrencyToken, after.ConcurrencyToken);
        }
    }

    [Fact]
    public async Task Reorder_with_one_stale_token_rolls_back_every_milestone()
    {
        var s = await Seed();
        await using var db = database.CreateContext();
        var second = new M.Milestone { ProjectId = s.Project, CreatedBy = s.Users.Student, Title = "Second", Status = "PLANNED", SortOrder = 2 };
        db.Milestones.Add(second);
        await db.SaveChangesAsync();
        var first = await db.Milestones.AsNoTracking().SingleAsync(x => x.Id == s.Milestone);
        var token = first.ConcurrencyToken;
        using var factory = new Factory(database);
        using var client = factory.CreateAuthenticatedClient(s.Users.Student);
        var response = await client.PostAsJsonAsync($"/api/v1/milestones/project/{s.Project}/reorder", new[]
        {
            new { milestoneId = first.Id, sortOrder = 2, concurrencyToken = token.ToString("N") },
            new { milestoneId = second.Id, sortOrder = 1, concurrencyToken = Guid.NewGuid().ToString("N") }
        });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var after = await db.Milestones.AsNoTracking().SingleAsync(x => x.Id == first.Id);
        Assert.Equal(first.SortOrder, after.SortOrder);
        Assert.Equal(token, after.ConcurrencyToken);
        Assert.False(await db.AuditLogs.AnyAsync(x => x.ActorUserId == s.Users.Student));
    }

    [Fact]
    public async Task Legacy_participant_writes_invalidate_existing_meeting_tokens()
    {
        var s = await Seed();
        await using (var db = database.CreateContext())
        {
            var row = await db.Meetings.FindAsync(s.Meeting);
            row!.Status = "SCHEDULED";
            await db.SaveChangesAsync();
        }
        using var factory = new Factory(database, requireTokens: false);
        using var client = factory.CreateAuthenticatedClient(s.Users.Student);
        var root = $"/api/v1/meetings/{s.Meeting}";
        var before = (await client.GetFromJsonAsync<MeetingDetailDto>(root))!;
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync(root + "/participants",
            new { userId = s.Users.Lecturer, attendanceStatus = "INVITED" })).StatusCode);
        var added = (await client.GetFromJsonAsync<MeetingDetailDto>(root))!;
        Assert.NotEqual(before.ConcurrencyToken, added.ConcurrencyToken);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(root + "/complete?concurrencyToken=" + before.ConcurrencyToken, new { })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync(root + $"/participants/{s.Users.Lecturer}")).StatusCode);
        var removed = (await client.GetFromJsonAsync<MeetingDetailDto>(root))!;
        Assert.NotEqual(added.ConcurrencyToken, removed.ConcurrencyToken);
        Assert.Empty(removed.Participants);
    }

    [Fact]
    public async Task Action_audit_failure_rolls_back_action_token_and_notification()
    {
        var s = await Seed();
        using var normal = new Factory(database);
        using var client = normal.CreateAuthenticatedClient(s.Users.Student);
        var root = $"/api/v1/meetings/{s.Meeting}";
        var before = (await client.GetFromJsonAsync<MeetingDetailDto>(root))!;
        using var broken = new Factory(database, failAudit: true);
        using var writer = broken.CreateAuthenticatedClient(s.Users.Student);
        var response = await writer.PostAsJsonAsync(root + "/action-items",
            new SaveMeetingActionItemRequest("Must roll back", null, s.Users.Lecturer, null, "OPEN", before.ConcurrencyToken!));
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(before.ConcurrencyToken, (await client.GetFromJsonAsync<MeetingDetailDto>(root))!.ConcurrencyToken);
        await using var db = database.CreateContext();
        Assert.False(await db.MeetingActionItems.AnyAsync(x => x.MeetingId == s.Meeting));
        Assert.False(await db.Notifications.AnyAsync(x => x.CreatedBy == s.Users.Student));
        Assert.False(await db.AuditLogs.AnyAsync(x => x.ActorUserId == s.Users.Student));
    }

    [Fact]
    public async Task Swagger_exposes_governance_routes_and_execution_tokens()
    {
        using var factory = new Factory(database);
        using var client = factory.CreateClient();
        using var swagger = System.Text.Json.JsonDocument.Parse(await client.GetStringAsync("/swagger/v1/swagger.json"));
        var paths = swagger.RootElement.GetProperty("paths");
        foreach (var suffix in new[] { "decisions", "action-items" })
        {
            var route = paths.GetProperty("/api/v1/meetings/{meetingId}/" + suffix);
            Assert.True(route.TryGetProperty("get", out _));
            Assert.True(route.TryGetProperty("post", out _));
        }
        var update = paths.GetProperty("/api/v1/meetings/{meetingId}/action-items/{id}");
        Assert.True(update.TryGetProperty("put", out _));
        Assert.True(update.TryGetProperty("patch", out _));
        var schemas = swagger.RootElement.GetProperty("components").GetProperty("schemas");
        foreach (var dto in new[] { "TaskDto", "MilestoneDto", "ProgressReportDetailDto", "MeetingDetailDto", "MeetingActionItemDto" })
            Assert.True(schemas.GetProperty(dto).GetProperty("properties").TryGetProperty("concurrencyToken", out _));
    }

    [Fact]
    public async Task Audit_failure_rolls_back_data_and_tokens_and_outside_actor_is_denied()
    {
        var s = await Seed(); using var normal = new Factory(database); using var client = normal.CreateAuthenticatedClient(s.Users.Student);
        var root = $"/api/v1/meetings/{s.Meeting}";
        var before = (await client.GetFromJsonAsync<MeetingDetailDto>(root))!;
        using var broken = new Factory(database, true); using var writer = broken.CreateAuthenticatedClient(s.Users.Student);
        Assert.Equal(HttpStatusCode.InternalServerError, (await writer.PutAsJsonAsync(root + "/notes", new UpdateMeetingNotesRequest("Must rollback", null, before.ConcurrencyToken))).StatusCode);
        Assert.Equal(HttpStatusCode.InternalServerError, (await writer.PostAsJsonAsync(root + "/decisions", new CreateMeetingDecisionRequest("Must rollback", before.ConcurrencyToken!))).StatusCode);
        var after = (await client.GetFromJsonAsync<MeetingDetailDto>(root))!;
        Assert.Equal(before.ConcurrencyToken, after.ConcurrencyToken); Assert.Equal(before.MeetingNotes, after.MeetingNotes);
        Assert.Empty((await client.GetFromJsonAsync<PagedResult<MeetingDecisionDto>>(root + "/decisions"))!.Items);
        using var outside = normal.CreateAuthenticatedClient(s.Users.OtherLecturer, roles: "LECTURER");
        Assert.Equal(HttpStatusCode.Forbidden, (await outside.GetAsync(root + "/decisions")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await outside.PostAsJsonAsync(root + "/decisions", new CreateMeetingDecisionRequest("No", before.ConcurrencyToken!))).StatusCode);
    }
}
