using System.Net;
using System.Net.Http.Json;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Sprints;
using AIPMS.Application.Features.Tasks.Commands;
using AIPMS.Application.Features.Tasks.DTOs;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.IntegrationTests.Evaluations;

public sealed class SprintEndpointTests(EvaluationDraftDatabaseFixture database) : IClassFixture<EvaluationDraftDatabaseFixture>
{
    private static DateTime Now => EvaluationDraftDatabaseFixture.Now;
    private static string Url(long project) => $"/api/v1/projects/{project}/sprints";
    private static async Task<T> Body<T>(HttpResponseMessage r)
    { Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync()); return (await r.Content.ReadFromJsonAsync<T>())!; }
    private async Task<(EvaluationScenario S, long Milestone, long Primary, long Major)> Seed()
    {
        var s = await database.Seed(false); await using var db = database.CreateContext(); (await db.Projects.FindAsync(s.ProjectId))!.Status = "ACTIVE";
        var milestone = new M.Milestone { ProjectId = s.ProjectId, Title = "Sprint fixture", Status = "PLANNED", CreatedBy = s.Scope.Users.Student };
        db.Add(milestone); await db.SaveChangesAsync();
        return (s, milestone.Id, await db.SupervisorAssignments.Where(a => a.ProjectId == s.ProjectId).Select(a => a.SupervisorProfile.UserId).SingleAsync(),
            await db.ProjectMajors.Where(m => m.ProjectId == s.ProjectId).Select(m => m.MajorId).SingleAsync());
    }
    private static Task<SprintDto> Create(HttpClient client, long project, string name = "Sprint") =>
        SendCreate(client, project, new(name, new(Now), new(Now.AddDays(14))));
    private static async Task<SprintDto> SendCreate(HttpClient c, long p, SaveSprintRequest r) => await Body<SprintDto>(await c.PostAsJsonAsync(Url(p), r));
    private static async Task<TaskDto> Task(HttpClient c, long milestone, long major, string title = "Task") =>
        await Body<TaskDto>(await c.PostAsJsonAsync("/api/v1/tasks", new CreateTaskCommand(milestone, null, title, null, "MEDIUM", null, null, [], [new(major, "PRIMARY")])));
    private static async Task<TaskDto> Board(HttpClient c, TaskDto task, long? sprint, int? points, params string[] labels) =>
        await Body<TaskDto>(await c.PutAsJsonAsync($"/api/v1/tasks/{task.Id}/board", new SaveTaskBoardRequest(sprint, points, labels, task.ConcurrencyToken!)));

    [Fact]
    public async System.Threading.Tasks.Task Lifecycle_uses_real_status_history_explicit_carryover_and_keeps_milestone()
    {
        using var app = new EvaluationFactory(database); var x = await Seed(); using var leader = app.CreateAuthenticatedClient(x.S.Scope.Users.Student);
        var s = await Create(leader, x.S.ProjectId); var next = await Create(leader, x.S.ProjectId, "Next");
        var a = await Task(leader, x.Milestone, x.Major, "First"); var b = await Task(leader, x.Milestone, x.Major, "Second");
        Assert.NotEqual(a.TaskCode, b.TaskCode); Assert.StartsWith("TASK-", a.TaskCode);
        a = await Board(leader, a, s.Id, 3, "api"); b = await Board(leader, b, s.Id, null, "docs");
        var planned = await Body<SprintBurndownDto>(await leader.GetAsync(Url(x.S.ProjectId) + $"/{s.Id}/burndown"));
        Assert.Null(planned.HistoryStartsAt); Assert.Empty(planned.Points);
        s = await Body<SprintDto>(await leader.PostAsJsonAsync(Url(x.S.ProjectId) + $"/{s.Id}/start", new SprintTokenRequest(s.ConcurrencyToken)));
        var chart = await Body<SprintBurndownDto>(await leader.GetAsync(Url(x.S.ProjectId) + $"/{s.Id}/burndown"));
        Assert.Equal(3, chart.Points.Last().RemainingPoints); Assert.Equal(1, chart.Points.Last().UnestimatedTasks);
        a = await Board(leader, a, s.Id, 5, "api");
        foreach (var status in new[] { "IN_PROGRESS", "DONE" })
            a = await Body<TaskDto>(await leader.PutAsJsonAsync($"/api/v1/tasks/{a.Id}/status", new UpdateTaskStatusCommand(a.Id, status, "Fixture", a.ConcurrencyToken)));
        Assert.Equal(HttpStatusCode.Conflict, (await leader.PostAsJsonAsync(Url(x.S.ProjectId) + $"/{s.Id}/complete", new CompleteSprintRequest(s.ConcurrencyToken, []))).StatusCode);
        s = await Body<SprintDto>(await leader.PostAsJsonAsync(Url(x.S.ProjectId) + $"/{s.Id}/complete", new CompleteSprintRequest(s.ConcurrencyToken, [new(b.Id, next.Id, b.ConcurrencyToken!)])));
        Assert.Equal("COMPLETED", s.Status);
        await using var db = database.CreateContext();
        Assert.Equal(x.Milestone, (await db.Tasks.FindAsync(b.Id))!.MilestoneId); Assert.Equal("TODO", (await db.Tasks.FindAsync(b.Id))!.Status);
        Assert.Equal(next.Id, (await db.Tasks.FindAsync(b.Id))!.SprintId); Assert.Equal(s.Id, (await db.Tasks.FindAsync(a.Id))!.SprintId);
        var events = await db.Set<SprintTaskEvent>().Where(e => e.SprintId == s.Id).ToArrayAsync();
        Assert.Equal(2, events.Count(e => e.Kind == "BASELINE")); Assert.Single(events, e => e.Kind == "ESTIMATE");
        Assert.Equal(2, events.Count(e => e.Kind == "STATUS")); Assert.Single(events, e => e.Kind == "SCOPE" && !e.InScope);
        Assert.True(await db.AuditLogs.AnyAsync(log => log.Action == "SPRINT_COMPLETED" && log.EntityId == s.Id.ToString()));
    }

    [Fact]
    public async System.Threading.Tasks.Task Concurrent_starts_have_one_winner_stale_token_and_foreign_carryover_fail()
    {
        using var app = new EvaluationFactory(database); var x = await Seed(); var other = await Seed(); using var leader = app.CreateAuthenticatedClient(x.S.Scope.Users.Student);
        using var foreign = app.CreateAuthenticatedClient(other.S.Scope.Users.Student);
        var a = await Create(leader, x.S.ProjectId); var b = await Create(leader, x.S.ProjectId); var foreignSprint = await Create(foreign, other.S.ProjectId);
        var starts = await System.Threading.Tasks.Task.WhenAll(leader.PostAsJsonAsync(Url(x.S.ProjectId) + $"/{a.Id}/start", new SprintTokenRequest(a.ConcurrencyToken)),
            leader.PostAsJsonAsync(Url(x.S.ProjectId) + $"/{b.Id}/start", new SprintTokenRequest(b.ConcurrencyToken)));
        Assert.Single(starts, r => r.StatusCode == HttpStatusCode.Conflict);
        var active = await Body<SprintDto>(starts.Single(r => r.IsSuccessStatusCode));
        var t = await Task(leader, x.Milestone, x.Major); t = await Board(leader, t, active.Id, 8);
        Assert.Equal(HttpStatusCode.Conflict, (await leader.PostAsJsonAsync(Url(x.S.ProjectId) + $"/{active.Id}/complete", new CompleteSprintRequest(active.ConcurrencyToken, [new(t.Id, foreignSprint.Id, t.ConcurrencyToken!)]))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await leader.PostAsJsonAsync(Url(x.S.ProjectId) + $"/{active.Id}/complete", new CompleteSprintRequest(active.ConcurrencyToken, [new(t.Id, null, Guid.NewGuid().ToString())]))).StatusCode);
        await Body<SprintDto>(await leader.PostAsJsonAsync(Url(x.S.ProjectId) + $"/{active.Id}/complete", new CompleteSprintRequest(active.ConcurrencyToken, [new(t.Id, null, t.ConcurrencyToken!)])));
        Assert.Equal(HttpStatusCode.Conflict, (await leader.PostAsJsonAsync(Url(x.S.ProjectId) + $"/{active.Id}/complete", new CompleteSprintRequest(active.ConcurrencyToken, []))).StatusCode);
    }

    [Fact]
    public async System.Threading.Tasks.Task Board_filters_before_count_and_paging_and_validates_metadata()
    {
        using var app = new EvaluationFactory(database); var x = await Seed(); using var leader = app.CreateAuthenticatedClient(x.S.Scope.Users.Student);
        var s = await Create(leader, x.S.ProjectId); var a = await Task(leader, x.Milestone, x.Major); var b = await Task(leader, x.Milestone, x.Major);
        a = await Board(leader, a, s.Id, 3, "api"); b = await Board(leader, b, null, 5, "docs");
        var result = await Body<PagedResult<TaskDto>>(await leader.GetAsync($"/api/v1/projects/{x.S.ProjectId}/task-board?sprintId={s.Id}&label=api&majorId={x.Major}&pageSize=1"));
        Assert.Equal(1, result.TotalCount); Assert.Equal(a.Id, result.Items.Single().Id); Assert.Equal("api", result.Items.Single().Labels!.Single());
        result = await Body<PagedResult<TaskDto>>(await leader.GetAsync($"/api/v1/projects/{x.S.ProjectId}/task-board?backlog=true")); Assert.Equal(b.Id, result.Items.Single().Id);
        Assert.Equal(HttpStatusCode.BadRequest, (await leader.PutAsJsonAsync($"/api/v1/tasks/{a.Id}/board", new SaveTaskBoardRequest(s.Id, 4, [], a.ConcurrencyToken!))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await leader.PutAsJsonAsync($"/api/v1/tasks/{a.Id}/board", new SaveTaskBoardRequest(s.Id, 3, ["Invalid Label"], a.ConcurrencyToken!))).StatusCode);
        var read = await Body<TaskDto>(await leader.GetAsync($"/api/v1/tasks/{a.Id}")); Assert.Equal("api", read.Labels!.Single()); Assert.Equal(s.Id, read.SprintId);
    }

    [Fact]
    public async System.Threading.Tasks.Task Mentor_can_read_own_major_but_not_manage_or_view_whole_burndown()
    {
        using var app = new EvaluationFactory(database); var x = await Seed(); using var leader = app.CreateAuthenticatedClient(x.S.Scope.Users.Student);
        var s = await Create(leader, x.S.ProjectId); var t = await Task(leader, x.Milestone, x.Major);
        await using (var db = database.CreateContext())
        {
            var profile = new M.SupervisorProfile { UserId = x.S.Scope.Users.NewLecturer };
            db.SupervisorAssignments.Add(new() { ProjectId = x.S.ProjectId, SupervisorProfile = profile, AssignmentType = "DISCIPLINE_MENTOR", MajorId = x.Major,
                SupervisorRequest = new() { ProjectId = x.S.ProjectId, SupervisorProfile = profile, RequestedBy = x.S.Scope.Users.Student, Status = "ACCEPTED", RequestedAt = Now, RespondedAt = Now }, AssignedAt = Now });
            db.Tasks.Add(new() { MilestoneId = x.Milestone, Title = "Unclassified", Status = "TODO", CreatedBy = x.S.Scope.Users.Student }); await db.SaveChangesAsync();
        }
        using var mentor = app.CreateAuthenticatedClient(x.S.Scope.Users.NewLecturer); using var admin = app.CreateAuthenticatedClient(x.S.Scope.Users.Admin);
        Assert.Equal(t.Id, (await Body<PagedResult<TaskDto>>(await mentor.GetAsync($"/api/v1/projects/{x.S.ProjectId}/task-board"))).Items.Single().Id);
        Assert.Equal(HttpStatusCode.Forbidden, (await mentor.PostAsJsonAsync(Url(x.S.ProjectId) + $"/{s.Id}/start", new SprintTokenRequest(s.ConcurrencyToken))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await mentor.GetAsync(Url(x.S.ProjectId) + $"/{s.Id}/burndown")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync(Url(x.S.ProjectId))).StatusCode);
        await using (var db = database.CreateContext()) { (await db.SupervisorAssignments.SingleAsync(a => a.ProjectId == x.S.ProjectId && a.AssignmentType == "DISCIPLINE_MENTOR")).EndedAt = Now; await db.SaveChangesAsync(); }
        Assert.Equal(HttpStatusCode.Forbidden, (await mentor.GetAsync(Url(x.S.ProjectId))).StatusCode);
    }

    [Fact]
    public async System.Threading.Tasks.Task Audit_failure_rolls_back_start_and_board_event_and_archive_denies_write()
    {
        using var app = new EvaluationFactory(database); using var broken = new EvaluationFactory(database, failAudit: true);
        var x = await Seed(); using var leader = app.CreateAuthenticatedClient(x.S.Scope.Users.Student); using var bad = broken.CreateAuthenticatedClient(x.S.Scope.Users.Student);
        var s = await Create(leader, x.S.ProjectId); var t = await Task(leader, x.Milestone, x.Major); t = await Board(leader, t, s.Id, 2);
        Assert.Equal(HttpStatusCode.InternalServerError, (await bad.PostAsJsonAsync(Url(x.S.ProjectId) + $"/{s.Id}/start", new SprintTokenRequest(s.ConcurrencyToken))).StatusCode);
        await using (var db = database.CreateContext()) { Assert.Equal("PLANNED", (await db.Set<Sprint>().FindAsync(s.Id))!.Status); Assert.False(await db.Set<SprintTaskEvent>().AnyAsync(e => e.SprintId == s.Id)); }
        await Body<SprintDto>(await leader.PostAsJsonAsync(Url(x.S.ProjectId) + $"/{s.Id}/start", new SprintTokenRequest(s.ConcurrencyToken)));
        Assert.Equal(HttpStatusCode.InternalServerError, (await bad.PutAsJsonAsync($"/api/v1/tasks/{t.Id}/board", new SaveTaskBoardRequest(s.Id, 13, [], t.ConcurrencyToken!))).StatusCode);
        await using (var db = database.CreateContext()) { Assert.Equal(2, (await db.Tasks.FindAsync(t.Id))!.StoryPoints); Assert.Single(await db.Set<SprintTaskEvent>().Where(e => e.SprintId == s.Id).ToArrayAsync());
            (await db.Projects.FindAsync(x.S.ProjectId))!.Status = "ARCHIVED"; await db.SaveChangesAsync(); }
        Assert.Equal(HttpStatusCode.Conflict, (await leader.PutAsJsonAsync($"/api/v1/tasks/{t.Id}/board", new SaveTaskBoardRequest(s.Id, 5, [], t.ConcurrencyToken!))).StatusCode);
    }

    [Fact]
    public async System.Threading.Tasks.Task Burndown_preserves_prior_estimates_and_completed_history_after_current_task_changes()
    {
        var clock = new SprintClock(); using var app = new SprintFactory(database, clock); var x = await Seed(); using var leader = app.CreateAuthenticatedClient(x.S.Scope.Users.Student);
        var s = await Create(leader, x.S.ProjectId); var t = await Task(leader, x.Milestone, x.Major); t = await Board(leader, t, s.Id, 3);
        s = await Body<SprintDto>(await leader.PostAsJsonAsync(Url(x.S.ProjectId) + $"/{s.Id}/start", new SprintTokenRequest(s.ConcurrencyToken)));
        clock.At = Now.AddDays(1); t = await Board(leader, t, s.Id, 8);
        clock.At = Now.AddDays(2); t = await Body<TaskDto>(await leader.PutAsJsonAsync($"/api/v1/tasks/{t.Id}/status", new UpdateTaskStatusCommand(t.Id, "IN_PROGRESS", null, t.ConcurrencyToken)));
        clock.At = Now.AddDays(3); t = await Body<TaskDto>(await leader.PutAsJsonAsync($"/api/v1/tasks/{t.Id}/status", new UpdateTaskStatusCommand(t.Id, "DONE", null, t.ConcurrencyToken)));
        await Body<SprintDto>(await leader.PostAsJsonAsync(Url(x.S.ProjectId) + $"/{s.Id}/complete", new CompleteSprintRequest(s.ConcurrencyToken, [])));
        var chart = await Body<SprintBurndownDto>(await leader.GetAsync(Url(x.S.ProjectId) + $"/{s.Id}/burndown"));
        Assert.Equal(new[] { 3, 8, 8, 0 }, chart.Points.Select(p => p.RemainingPoints));
        Assert.Equal(Now, chart.HistoryStartsAt); Assert.Equal(Now.AddDays(3), chart.Points.Last().At);
        clock.At = Now.AddDays(4); await Body<TaskDto>(await leader.PutAsJsonAsync($"/api/v1/tasks/{t.Id}/status", new UpdateTaskStatusCommand(t.Id, "IN_PROGRESS", null, t.ConcurrencyToken)));
        var historical = await Body<SprintBurndownDto>(await leader.GetAsync(Url(x.S.ProjectId) + $"/{s.Id}/burndown"));
        Assert.Equal(chart.Points, historical.Points);
        Assert.Equal(HttpStatusCode.Conflict, (await leader.DeleteAsync($"/api/v1/tasks/{t.Id}")).StatusCode);
    }

    [Fact]
    public async System.Threading.Tasks.Task Concurrent_estimate_updates_use_task_token_and_primary_has_same_manager_authority()
    {
        using var app = new EvaluationFactory(database); var x = await Seed(); using var leader = app.CreateAuthenticatedClient(x.S.Scope.Users.Student); using var primary = app.CreateAuthenticatedClient(x.Primary);
        var s = await Create(primary, x.S.ProjectId); var t = await Task(leader, x.Milestone, x.Major);
        var updates = await System.Threading.Tasks.Task.WhenAll(primary.PutAsJsonAsync($"/api/v1/tasks/{t.Id}/board", new SaveTaskBoardRequest(s.Id, 3, [], t.ConcurrencyToken!)),
            leader.PutAsJsonAsync($"/api/v1/tasks/{t.Id}/board", new SaveTaskBoardRequest(s.Id, 5, [], t.ConcurrencyToken!)));
        Assert.Single(updates, r => r.StatusCode == HttpStatusCode.OK); Assert.Single(updates, r => r.StatusCode == HttpStatusCode.Conflict);
        using var anon = app.CreateClient(); Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync(Url(x.S.ProjectId))).StatusCode);
    }
    [Fact]
    public async System.Threading.Tasks.Task Database_enforces_active_sprint_and_Fibonacci_constraints_and_task_code_stability()
    {
        using var app = new EvaluationFactory(database); var x = await Seed(); using var leader = app.CreateAuthenticatedClient(x.S.Scope.Users.Student);
        var s = await Create(leader, x.S.ProjectId); var next = await Create(leader, x.S.ProjectId);
        var task = await Task(leader, x.Milestone, x.Major);
        await Body<SprintDto>(await leader.PostAsJsonAsync(Url(x.S.ProjectId) + $"/{s.Id}/start", new SprintTokenRequest(s.ConcurrencyToken)));
        await using var db = database.CreateContext();
        await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.sprints SET status='ACTIVE',started_at={Now} WHERE id={next.Id}"));
        await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.tasks SET story_points=4 WHERE id={task.Id}"));
        var changed = await Board(leader, task, s.Id, 8, "stable"); Assert.Equal(task.TaskCode, changed.TaskCode);
        var other = await Task(leader, x.Milestone, x.Major); Assert.NotEqual(other.TaskCode, task.TaskCode);
        Assert.Equal("PLANNED", (await db.Set<Sprint>().FindAsync(next.Id))!.Status);
    }
    private sealed class SprintClock : TimeProvider
    {
        public DateTime At { get; set; } = Now;
        public override DateTimeOffset GetUtcNow() => new(At);
    }
    private sealed class SprintFactory(EvaluationDraftDatabaseFixture db, SprintClock clock) : EvaluationFactory(db)
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services => { services.RemoveAll<TimeProvider>(); services.AddSingleton<TimeProvider>(clock); });
        }
    }
}
