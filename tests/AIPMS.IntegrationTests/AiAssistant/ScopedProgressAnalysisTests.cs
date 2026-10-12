using System.Net;
using System.Net.Http.Json;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.AiAssistant.DTOs;
using AIPMS.Application.Features.Projects.DTOs;
using AIPMS.Infrastructure.Persistence.Models;
using AIPMS.IntegrationTests.Evaluations;
using Microsoft.EntityFrameworkCore;
using AIPMS.Application.Abstractions.Auditing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.IntegrationTests.AiAssistant;

public sealed class ScopedProgressAnalysisTests(EvaluationDraftDatabaseFixture database) : IClassFixture<EvaluationDraftDatabaseFixture>
{
    private static async Task<T> Body<T>(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }
    private static string Root(long project) => $"/api/v1/projects/{project}";

    private async Task<(EvaluationScenario Scenario, long Task, long Assignment)> Seed()
    {
        var s = await database.Seed(false);
        await using var db = database.CreateContext();
        var major = await db.ProjectMajors.Where(m => m.ProjectId == s.ProjectId).Select(m => m.MajorId).SingleAsync();
        var milestone = new M.Milestone { ProjectId = s.ProjectId, Title = "Private milestone", Status = "PLANNED", CreatedBy = s.Scope.Users.Student };
        var task = new M.Task { Milestone = milestone, Title = "Visible task", Status = "TODO", CreatedBy = s.Scope.Users.Student,
            DueAt = EvaluationDraftDatabaseFixture.Now.AddDays(-1) };
        db.AddRange(task, new M.Task { Milestone = milestone, Title = "Unclassified private task", Status = "TODO", CreatedBy = s.Scope.Users.Student });
        await db.SaveChangesAsync();
        db.TaskDisciplines.Add(new() { TaskId = task.Id, MajorId = major, Role = "PRIMARY", CreatedBy = s.Scope.Users.Student });
        var profile = new M.SupervisorProfile { UserId = s.Scope.Users.NewLecturer, MaxActiveProjects = 5, IsAvailable = true };
        var assignment = new M.SupervisorAssignment { ProjectId = s.ProjectId, SupervisorProfile = profile,
            AssignmentType = "DISCIPLINE_MENTOR", MajorId = major, AssignedAt = EvaluationDraftDatabaseFixture.Now,
            SupervisorRequest = new() { ProjectId = s.ProjectId, SupervisorProfile = profile,
                RequestedBy = s.Scope.Users.Student, Status = "ACCEPTED", RequestedAt = EvaluationDraftDatabaseFixture.Now } };
        db.Add(assignment);
        await db.SaveChangesAsync();
        return (s, task.Id, assignment.Id);
    }

    [Fact]
    public async Task Mentor_get_is_major_only_and_unknown_project_metrics_are_not_zero()
    {
        var x = await Seed(); using var app = new EvaluationFactory(database);
        using var mentor = app.CreateAuthenticatedClient(x.Scenario.Scope.Users.NewLecturer, roles: ["LECTURER"]);
        var result = await Body<ProjectProgressAnalysisDto>(await mentor.GetAsync(Root(x.Scenario.ProjectId) + "/progress-analysis"));
        Assert.Equal("MAJOR", result.ContextScope); Assert.Single(result.MajorIds);
        Assert.Equal(1, result.ProgressSummary.TotalTasks); Assert.Equal(1, result.ProgressSummary.OverdueTasks);
        Assert.Null(result.ProgressSummary.TotalMilestones); Assert.Null(result.ProgressSummary.CompletedMilestones);
        Assert.Null(result.RiskScore); Assert.Null(result.FeatureSnapshot.MeetingFrequencyCount);
        Assert.Null(result.FeatureSnapshot.MissingReportCount); Assert.Equal("INSUFFICIENT_DATA", result.DataStatus);
        Assert.Contains("PROJECT_RISK", result.UnavailableMetrics); Assert.Null(result.RunId);
        await using var db = database.CreateContext(); Assert.False(await db.Set<AiRun>().AnyAsync(r => r.ProjectId == x.Scenario.ProjectId));
    }

    [Fact]
    public async Task Recorded_analysis_persists_typed_result_and_audit_but_get_does_not_create_runs()
    {
        var x = await Seed(); using var app = new EvaluationFactory(database);
        using var leader = app.CreateAuthenticatedClient(x.Scenario.Scope.Users.Student);
        var url = Root(x.Scenario.ProjectId);
        var transient = await Body<ProjectProgressAnalysisDto>(await leader.GetAsync(url + "/progress-analysis"));
        Assert.Null(transient.RunId); Assert.Equal(2, transient.ProgressSummary.TotalTasks);
        var result = await Body<ProjectProgressAnalysisDto>(await leader.PostAsync(url + "/ai/progress-analysis", null));
        Assert.NotNull(result.RunId);
        var saved = await Body<AiRunDto>(await leader.GetAsync(url + $"/ai/runs/{result.RunId}"));
        Assert.Equal("PROGRESS_ANALYSIS", saved.Kind); Assert.Equal("RULE_BASED", saved.ModelVersion);
        Assert.Equal(result.RuleVersion, saved.ConfigVersion); Assert.Null(saved.Answer); Assert.Null(saved.ReportSummary);
        Assert.Equal(result.ProgressSummary, saved.ProgressAnalysis!.ProgressSummary);
        var list = await Body<PagedResult<AiRunDto>>(await leader.GetAsync(url + "/ai/runs")); Assert.Single(list.Items);
        await using var db = database.CreateContext();
        Assert.True(await db.AuditLogs.AnyAsync(a => a.Action == "AI_RUN_COMPLETED" && a.EntityId == result.RunId.ToString()));
        Assert.Single(await db.Set<AiRun>().Where(r => r.ProjectId == x.Scenario.ProjectId).ToArrayAsync());
    }

    [Fact]
    public async Task Reclassified_evidence_redacts_typed_history_and_revoked_mentor_cannot_reopen_it()
    {
        var x = await Seed(); using var app = new EvaluationFactory(database);
        using var mentor = app.CreateAuthenticatedClient(x.Scenario.Scope.Users.NewLecturer, roles: ["LECTURER"]);
        var root = Root(x.Scenario.ProjectId);
        var result = await Body<ProjectProgressAnalysisDto>(await mentor.PostAsync(root + "/ai/progress-analysis", null));
        var url = root + $"/ai/runs/{result.RunId}";
        var saved = await Body<AiRunDto>(await mentor.GetAsync(url));
        Assert.Equal("INSUFFICIENT_EVIDENCE", saved.Status); Assert.All(saved.Evidence, e => Assert.Equal("TASK", e.SourceType));
        using var leader = app.CreateAuthenticatedClient(x.Scenario.Scope.Users.Student);
        Assert.Equal(HttpStatusCode.NotFound, (await leader.GetAsync(url)).StatusCode);
        await using (var db = database.CreateContext()) { await db.TaskDisciplines.Where(d => d.TaskId == x.Task).ExecuteDeleteAsync(); }
        var redacted = await Body<AiRunDto>(await mentor.GetAsync(url));
        Assert.Null(redacted.ProgressAnalysis); Assert.Empty(redacted.Evidence); Assert.Equal("AI_HISTORY_SCOPE_CHANGED", redacted.ErrorCode);
        await using (var db = database.CreateContext()) { (await db.SupervisorAssignments.FindAsync(x.Assignment))!.EndedAt = EvaluationDraftDatabaseFixture.Now.AddMinutes(1); await db.SaveChangesAsync(); }
        Assert.Equal(HttpStatusCode.Forbidden, (await mentor.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await mentor.GetAsync(root + "/progress-analysis")).StatusCode);
    }

    [Fact]
    public async Task Platform_admin_foreign_actor_and_anonymous_cannot_read_or_generate_project_analysis()
    {
        var x = await Seed(); var foreign = await database.Seed(false); using var app = new EvaluationFactory(database);
        using var admin = app.CreateAuthenticatedClient(x.Scenario.Scope.Users.Admin);
        using var stranger = app.CreateAuthenticatedClient(foreign.Scope.Users.Student);
        using var anonymous = app.CreateClient();
        foreach (var client in new[] { admin, stranger, anonymous })
        {
            var expected = client == anonymous ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden;
            Assert.Equal(expected, (await client.GetAsync(Root(x.Scenario.ProjectId) + "/progress-analysis")).StatusCode);
            Assert.Equal(expected, (await client.PostAsync(Root(x.Scenario.ProjectId) + "/ai/progress-analysis", null)).StatusCode);
        }
        await using var db = database.CreateContext(); Assert.False(await db.Set<AiRun>().AnyAsync(r => r.ProjectId == x.Scenario.ProjectId));
    }

    [Fact]
    public async Task Only_invited_meetings_are_used_and_removing_invitation_redacts_history()
    {
        var x = await Seed(); long invited;
        await using (var db = database.CreateContext())
        {
            var meeting = new M.Meeting { ProjectId = x.Scenario.ProjectId, Title = "Invited", Status = "SCHEDULED",
                StartAt = EvaluationDraftDatabaseFixture.Now.AddDays(-1), CreatedBy = x.Scenario.Scope.Users.Student,
                MeetingParticipants = [new() { UserId = x.Scenario.Scope.Users.Student }] };
            db.Add(meeting);
            db.Add(new M.Meeting { ProjectId = x.Scenario.ProjectId, Title = "Private", Status = "SCHEDULED",
                StartAt = EvaluationDraftDatabaseFixture.Now.AddDays(-1), CreatedBy = x.Scenario.Scope.Users.Student });
            await db.SaveChangesAsync(); invited = meeting.Id;
        }
        using var app = new EvaluationFactory(database); using var leader = app.CreateAuthenticatedClient(x.Scenario.Scope.Users.Student);
        var root = Root(x.Scenario.ProjectId);
        var result = await Body<ProjectProgressAnalysisDto>(await leader.PostAsync(root + "/ai/progress-analysis", null));
        var saved = await Body<AiRunDto>(await leader.GetAsync(root + $"/ai/runs/{result.RunId}"));
        Assert.Single(saved.Evidence.Where(e => e.SourceType == "MEETING"));
        Assert.Equal($"MTG-{invited}", saved.Evidence.Single(e => e.SourceType == "MEETING").SourceId);
        await using (var db = database.CreateContext()) { await db.MeetingParticipants.Where(p => p.MeetingId == invited).ExecuteDeleteAsync(); }
        var hidden = await Body<AiRunDto>(await leader.GetAsync(root + $"/ai/runs/{result.RunId}"));
        Assert.Null(hidden.ProgressAnalysis); Assert.Equal("AI_HISTORY_SCOPE_CHANGED", hidden.ErrorCode);
    }

    [Fact]
    public async Task Source_limit_rejects_incomplete_totals_instead_of_truncating()
    {
        var x = await Seed();
        await using (var db = database.CreateContext())
        {
            var milestone = await db.Tasks.Where(t => t.Id == x.Task).Select(t => t.MilestoneId).SingleAsync();
            db.Tasks.AddRange(Enumerable.Range(0, 1999).Select(_ => new M.Task { MilestoneId = milestone,
                Title = "Synthetic bounded-query fixture", Status = "TODO", CreatedBy = x.Scenario.Scope.Users.Student }));
            await db.SaveChangesAsync();
        }
        using var app = new EvaluationFactory(database); using var leader = app.CreateAuthenticatedClient(x.Scenario.Scope.Users.Student);
        var response = await leader.GetAsync(Root(x.Scenario.ProjectId) + "/progress-analysis");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("AI_ANALYSIS_SOURCE_LIMIT", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Audit_failure_does_not_commit_analysis_or_evidence()
    {
        var x = await Seed(); using var app = new AuditFailureFactory(database);
        using var leader = app.CreateAuthenticatedClient(x.Scenario.Scope.Users.Student);
        Assert.Equal(HttpStatusCode.InternalServerError,
            (await leader.PostAsync(Root(x.Scenario.ProjectId) + "/ai/progress-analysis", null)).StatusCode);
        await using var db = database.CreateContext();
        var row = await db.Set<AiRun>().SingleAsync(r => r.ProjectId == x.Scenario.ProjectId);
        Assert.Equal("RUNNING", row.Status); Assert.Null(row.ResultJson); Assert.Equal("[]", row.EvidenceJson);
        Assert.Equal("RULE_BASED", row.ModelVersion);
        Assert.False(await db.AuditLogs.AnyAsync(a => a.Action == "AI_RUN_COMPLETED" && a.EntityId == row.Id.ToString()));
    }

    private sealed class AuditFailureFactory(EvaluationDraftDatabaseFixture database) : EvaluationFactory(database)
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(s => { s.RemoveAll<IAuditTrail>(); s.AddSingleton<IAuditTrail, FailedAudit>(); });
        }
    }
    private sealed class FailedAudit : IAuditTrail
    {
        public Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Injected audit failure");
    }
}
