using System.Net;
using System.Net.Http.Json;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Features.AiAssistant.Abstractions;
using AIPMS.Application.Features.AiAssistant.DTOs;
using AIPMS.Application.Features.Tasks.Commands;
using AIPMS.Application.Features.Tasks.DTOs;
using AIPMS.Application.Features.Dashboards.DTOs;
using AIPMS.Infrastructure.Persistence.Models;
using AIPMS.IntegrationTests.Evaluations;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.IntegrationTests.AiAssistant;

public sealed class AiHistoryTests(EvaluationDraftDatabaseFixture database) : IClassFixture<EvaluationDraftDatabaseFixture>
{
    private static async Task<T> Body<T>(HttpResponseMessage r) { Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync()); return (await r.Content.ReadFromJsonAsync<T>())!; }
    private static string Url(long project) => $"/api/v1/projects/{project}/ai";
    private async Task<(EvaluationScenario S, long Milestone, long Major)> Seed()
    {
        var s = await database.Seed(false); await using var db = database.CreateContext(); (await db.Projects.FindAsync(s.ProjectId))!.Status = "ACTIVE";
        var milestone = new M.Milestone { ProjectId = s.ProjectId, Title = "Private whole-project milestone", Status = "PLANNED", CreatedBy = s.Scope.Users.Student };
        db.Add(milestone); await db.SaveChangesAsync(); return (s, milestone.Id, await db.ProjectMajors.Where(m => m.ProjectId == s.ProjectId).Select(m => m.MajorId).SingleAsync());
    }
    private static async Task<TaskDto> CreateTask(HttpClient c, long milestone, long major) => await Body<TaskDto>(await c.PostAsJsonAsync("/api/v1/tasks",
        new CreateTaskCommand(milestone, null, "Scoped task", null, "MEDIUM", null, null, [], [new(major, "PRIMARY")])));

    [Fact]
    public async Task Ask_persists_safe_history_and_audit_with_current_scope_and_no_academic_mutation()
    {
        var x = await Seed(); using var app = new EvaluationFactory(database); using var leader = app.CreateAuthenticatedClient(x.S.Scope.Users.Student);
        await CreateTask(leader, x.Milestone, x.Major);
        var result = await Body<ProjectAssistantResponseDto>(await leader.PostAsJsonAsync(Url(x.S.ProjectId) + "/ask", new AskProjectAssistantRequest("What is blocked?")));
        Assert.NotNull(result.RunId); Assert.Equal("COMPLETED", result.GenerationStatus);
        var history = await Body<AiRunDto>(await leader.GetAsync(Url(x.S.ProjectId) + $"/runs/{result.RunId}"));
        Assert.Equal("GROUNDED_LOCAL_V1", history.ModelVersion); Assert.Equal(result.Answer, history.Answer!.Answer); Assert.NotEmpty(history.Evidence);
        Assert.Single((await Body<PagedResult<AiRunDto>>(await leader.GetAsync(Url(x.S.ProjectId) + "/runs"))).Items);
        using var admin = app.CreateAuthenticatedClient(x.S.Scope.Users.Admin);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync(Url(x.S.ProjectId) + "/runs")).StatusCode);
        await using var db = database.CreateContext(); Assert.Equal("ACTIVE", (await db.Projects.FindAsync(x.S.ProjectId))!.Status);
        var stored = (await db.Set<AiRun>().FindAsync(result.RunId!.Value))!; Assert.DoesNotContain("What is blocked?", stored.ResultJson!);
        Assert.True(await db.AuditLogs.AnyAsync(a => a.Action == "AI_RUN_COMPLETED" && a.EntityId == result.RunId.ToString()));
    }

    [Fact]
    public async Task Mentor_provider_context_is_major_only_and_history_redacts_when_discipline_removed()
    {
        var evidenceLog = new ApiAcceptanceEvidence("D-ai-scope-security");
        var x = await Seed(); var capture = new Provider(); using var app = new Factory(database, capture) { AcceptanceEvidence = evidenceLog }; using var leader = app.CreateAuthenticatedClient(x.S.Scope.Users.Student);
        var task = await CreateTask(leader, x.Milestone, x.Major); long assignment;
        await using (var db = database.CreateContext())
        {
            var profile = new M.SupervisorProfile { UserId = x.S.Scope.Users.NewLecturer, MaxActiveProjects = 5, IsAvailable = true };
            var a = new M.SupervisorAssignment { ProjectId = x.S.ProjectId, SupervisorProfile = profile, AssignmentType = "DISCIPLINE_MENTOR", MajorId = x.Major,
                SupervisorRequest = new() { ProjectId = x.S.ProjectId, SupervisorProfile = profile, RequestedBy = x.S.Scope.Users.Student, Status = "ACCEPTED", RequestedAt = EvaluationDraftDatabaseFixture.Now }, AssignedAt = EvaluationDraftDatabaseFixture.Now };
            db.Add(a); db.Tasks.Add(new() { MilestoneId = x.Milestone, Title = "Secret unclassified work", Status = "TODO", CreatedBy = x.S.Scope.Users.Student });
            await db.SaveChangesAsync(); assignment = a.Id;
        }
        using var mentor = app.CreateAuthenticatedClient(x.S.Scope.Users.NewLecturer, roles: ["LECTURER"]);
        var result = await Body<ProjectAssistantResponseDto>(await mentor.PostAsJsonAsync(Url(x.S.ProjectId) + "/ask", new AskProjectAssistantRequest("List tasks")));
        Assert.Contains("Scoped task", capture.Prompt); Assert.DoesNotContain("Secret unclassified", capture.Prompt); Assert.DoesNotContain("Private whole-project", capture.Prompt);
        Assert.All(result.Evidence, e => Assert.Equal("TASK", e.SourceType));
        var dashboard = await Body<ActorDashboardDto>(await mentor.GetAsync("/api/v1/dashboards/me")); Assert.Equal(1, dashboard.OpenTasks);
        var legacy = await Body<SupervisorDashboardDto>(await mentor.GetAsync("/api/v1/dashboards/supervisor"));
        Assert.Empty(legacy.Projects.Items); Assert.Equal(0, legacy.OverdueTasks);
        Assert.Equal(HttpStatusCode.NotFound, (await leader.GetAsync(Url(x.S.ProjectId) + $"/runs/{result.RunId}")).StatusCode);
        await using (var db = database.CreateContext()) { await db.TaskDisciplines.Where(d => d.TaskId == task.Id).ExecuteDeleteAsync(); }
        var hidden = await Body<AiRunDto>(await mentor.GetAsync(Url(x.S.ProjectId) + $"/runs/{result.RunId}")); Assert.Null(hidden.Answer); Assert.Empty(hidden.Evidence); Assert.Equal("AI_HISTORY_SCOPE_CHANGED", hidden.ErrorCode);
        await using (var db = database.CreateContext()) { (await db.SupervisorAssignments.FindAsync(assignment))!.EndedAt = DateTime.UtcNow; await db.SaveChangesAsync(); }
        Assert.Equal(HttpStatusCode.Forbidden, (await mentor.GetAsync(Url(x.S.ProjectId) + $"/runs/{result.RunId}")).StatusCode);
        Assert.Equal(0, (await Body<ActorDashboardDto>(await mentor.GetAsync("/api/v1/dashboards/me"))).OpenTasks);
        await evidenceLog.SaveAsync(new { provider = "capture test double", majorOnlyInput = true, crossActor = 404,
            revoked = 403, reclassifiedHistoryRedacted = true, legacyWholeProjectCards = 0 });
    }

    [Fact]
    public async Task Provider_failure_returns_persisted_fallback_without_secret_and_insufficient_skips_provider()
    {
        var evidenceLog = new ApiAcceptanceEvidence("F-ai-fallback");
        var x = await Seed(); var provider = new Provider { Fail = true }; using var app = new Factory(database, provider) { AcceptanceEvidence = evidenceLog }; using var leader = app.CreateAuthenticatedClient(x.S.Scope.Users.Student);
        var fallback = await Body<ProjectAssistantResponseDto>(await leader.PostAsJsonAsync(Url(x.S.ProjectId) + "/ask", new AskProjectAssistantRequest("Show progress")));
        Assert.Equal("FALLBACK", fallback.GenerationStatus); Assert.Equal(1, provider.Calls);
        var row = await Body<AiRunDto>(await leader.GetAsync(Url(x.S.ProjectId) + $"/runs/{fallback.RunId}")); Assert.Equal("AI_PROVIDER_FALLBACK", row.ErrorCode);
        Assert.DoesNotContain("secret-test-marker", System.Text.Json.JsonSerializer.Serialize(row));
        await using (var db = database.CreateContext()) { await db.Milestones.Where(m => m.Id == x.Milestone).ExecuteDeleteAsync(); }
        var insufficient = await Body<ProjectAssistantResponseDto>(await leader.PostAsJsonAsync(Url(x.S.ProjectId) + "/ask", new AskProjectAssistantRequest("Show progress")));
        Assert.True(insufficient.InsufficientEvidence); Assert.Equal("INSUFFICIENT_EVIDENCE", insufficient.GenerationStatus); Assert.Equal(1, provider.Calls);
        await using var verify = database.CreateContext();
        Assert.Equal(2, await verify.Set<AiRun>().CountAsync(r => r.ProjectId == x.S.ProjectId));
        var runIds = await verify.Set<AiRun>().Where(r => r.ProjectId == x.S.ProjectId).Select(r => r.Id.ToString()).ToArrayAsync();
        Assert.Equal(2, await verify.AuditLogs.CountAsync(a => a.Action == "AI_RUN_COMPLETED" && runIds.Contains(a.EntityId)));
        await evidenceLog.SaveAsync(new { provider = "failure injection test double", runs = 2, completionAudits = 2,
            auditIds = await verify.AuditLogs.Where(a => a.Action == "AI_RUN_COMPLETED" && runIds.Contains(a.EntityId)).Select(a => a.Id).ToArrayAsync(),
            statuses = new[] { "FALLBACK", "INSUFFICIENT_EVIDENCE" }, academicStatus = "ACTIVE", secretsReturned = false });
    }

    [Fact]
    public async Task Scope_revoked_during_provider_call_returns_forbidden_and_stores_no_result()
    {
        var x = await Seed(); var provider = new Provider(); using var app = new Factory(database, provider); using var leader = app.CreateAuthenticatedClient(x.S.Scope.Users.Student);
        var revoked = false;
        provider.During = async () =>
        {
            await using var db = database.CreateContext();
            var teamId = await db.Projects.Where(p => p.Id == x.S.ProjectId).Select(p => p.TeamId).SingleAsync();
            var membership = await db.TeamMembers.SingleAsync(m => m.TeamId == teamId && m.UserId == x.S.Scope.Users.Student);
            // SQL stores membership timestamps to whole seconds; avoid a clock/rounding boundary in the fixture.
            membership.LeftAt = membership.JoinedAt.AddMinutes(1);
            await db.SaveChangesAsync(); revoked = true;
        };
        Assert.Equal(HttpStatusCode.Forbidden, (await leader.PostAsJsonAsync(Url(x.S.ProjectId) + "/ask", new AskProjectAssistantRequest("Show progress"))).StatusCode);
        Assert.True(revoked);
        await using var verify = database.CreateContext(); var row = await verify.Set<AiRun>().SingleAsync(r => r.ProjectId == x.S.ProjectId);
        Assert.Equal("FAILED", row.Status); Assert.Null(row.ResultJson); Assert.Equal("[]", row.EvidenceJson);
    }

    [Fact]
    public async Task Actor_dashboard_counts_server_scope_and_platform_admin_has_no_academic_totals()
    {
        var x = await Seed(); using var app = new EvaluationFactory(database); using var leader = app.CreateAuthenticatedClient(x.S.Scope.Users.Student);
        await CreateTask(leader, x.Milestone, x.Major); await CreateTask(leader, x.Milestone, x.Major);
        var dashboard = await Body<ActorDashboardDto>(await leader.GetAsync("/api/v1/dashboards/me"));
        Assert.Equal(1, dashboard.ProjectCount); Assert.Equal(2, dashboard.OpenTasks); Assert.True(dashboard.Complete);
        using var admin = app.CreateAuthenticatedClient(x.S.Scope.Users.Admin);
        var platform = await Body<ActorDashboardDto>(await admin.GetAsync("/api/v1/dashboards/me"));
        Assert.Equal("PLATFORM", platform.Scope); Assert.Equal(0, platform.ProjectCount); Assert.Equal(0, platform.OpenTasks); Assert.NotNull(platform.ActiveAccounts);
        Assert.Equal(HttpStatusCode.BadRequest, (await leader.GetAsync("/api/v1/dashboards/me?semesterId=-1")).StatusCode);
        using var outsider = app.CreateAuthenticatedClient(x.S.Scope.Users.NewLecturer);
        Assert.Equal(0, (await Body<ActorDashboardDto>(await outsider.GetAsync("/api/v1/dashboards/me"))).OpenTasks);
    }

    private sealed class Provider : IAiTextGenerationProvider
    {
        public string Prompt { get; private set; } = ""; public bool Fail { get; init; } public int Calls { get; private set; } public Func<Task>? During { get; set; }
        public async Task<string> GenerateTextAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default)
        { Calls++; Prompt = userPrompt; if (During is not null) await During(); if (Fail) throw new IOException("secret-test-marker"); return "Verified scoped evidence."; }
    }
    [Fact]
    public async Task Audit_failure_rolls_back_generated_answer_and_preserves_academic_state()
    {
        var x = await Seed(); using var app = new AuditFailureFactory(database); using var leader = app.CreateAuthenticatedClient(x.S.Scope.Users.Student);
        Assert.Equal(HttpStatusCode.InternalServerError, (await leader.PostAsJsonAsync(Url(x.S.ProjectId) + "/ask", new AskProjectAssistantRequest("Show progress"))).StatusCode);
        await using var db = database.CreateContext(); var run = await db.Set<AiRun>().SingleAsync(r => r.ProjectId == x.S.ProjectId);
        Assert.Equal("RUNNING", run.Status); Assert.Null(run.ResultJson); Assert.Equal("[]", run.EvidenceJson);
        Assert.False(await db.AuditLogs.AnyAsync(a => a.Action == "AI_RUN_COMPLETED" && a.EntityId == run.Id.ToString()));
        Assert.Equal("ACTIVE", (await db.Projects.FindAsync(x.S.ProjectId))!.Status);
    }

    [Fact]
    public async Task Report_history_is_scoped_and_inactive_frozen_department_denies_ai_and_dashboard()
    {
        var x = await Seed(); var foreign = await Seed(); long reportId;
        await using (var db = database.CreateContext())
        {
            var report = new M.ProgressReport { ProjectId = x.S.ProjectId, SubmittedBy = x.S.Scope.Users.Student,
                ReportType = "WEEKLY", PeriodStart = new(2026, 9, 1), PeriodEnd = new(2026, 9, 7), Summary = "Completed scoped work", Status = "DRAFT" };
            db.Add(report); await db.SaveChangesAsync(); reportId = report.Id;
        }
        using var app = new EvaluationFactory(database); using var leader = app.CreateAuthenticatedClient(x.S.Scope.Users.Student);
        using var foreignLeader = app.CreateAuthenticatedClient(foreign.S.Scope.Users.Student);
        var result = await Body<ReportSummaryDto>(await leader.GetAsync(Url(x.S.ProjectId) + $"/reports/{reportId}/summary"));
        Assert.NotNull(result.RunId);
        var history = await Body<AiRunDto>(await leader.GetAsync(Url(x.S.ProjectId) + $"/runs/{result.RunId}")); Assert.NotNull(history.ReportSummary);
        Assert.Equal(HttpStatusCode.NotFound, (await foreignLeader.GetAsync(Url(foreign.S.ProjectId) + $"/reports/{reportId}/summary")).StatusCode);
        await using (var db = database.CreateContext()) { (await db.Departments.FindAsync(x.S.Scope.Users.DepartmentId))!.IsActive = false; await db.SaveChangesAsync(); }
        Assert.Equal(HttpStatusCode.Forbidden, (await leader.GetAsync(Url(x.S.ProjectId) + $"/runs/{result.RunId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await leader.GetAsync("/api/v1/dashboards/me")).StatusCode);
    }

    private sealed class AuditFailureFactory(EvaluationDraftDatabaseFixture db) : EvaluationFactory(db)
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        { base.ConfigureWebHost(builder); builder.ConfigureServices(s => { s.RemoveAll<IAuditTrail>(); s.AddSingleton<IAuditTrail, FailAudit>(); }); }
    }
    private sealed class FailAudit : IAuditTrail
    {
        public Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Injected audit failure");
    }
    private sealed class Factory(EvaluationDraftDatabaseFixture db, Provider provider) : EvaluationFactory(db)
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        { base.ConfigureWebHost(builder); builder.ConfigureServices(s => { s.RemoveAll<IAiTextGenerationProvider>(); s.AddSingleton<IAiTextGenerationProvider>(provider); }); }
    }
}
