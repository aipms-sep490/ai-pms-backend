using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Evaluations.DTOs;
using AIPMS.Application.Features.FinalSubmissions.DTOs;
using AIPMS.Application.Features.Projects.DTOs;
using AIPMS.Application.Features.Results.DTOs;
using AIPMS.Application.Features.Teams.DTOs;
using AIPMS.Infrastructure.Persistence.Models;
using AIPMS.Application.Features.WorkflowContext.DTOs;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.IntegrationTests.FinalSubmissions;

public sealed partial class FinalSubmissionEndpointTests
{
    private async Task SeedEvaluationRoster(FinalDraftScenario s, long periodId)
    {
        await using var db = database.CreateContext();
        var majorId = await db.ProjectMajors.Where(m => m.ProjectId == s.ProjectId).Select(m => m.MajorId).SingleAsync();
        var members = await db.TeamMembers.Include(m => m.User).Where(m => m.TeamId == s.TeamId && m.LeftAt == null).ToListAsync();
        foreach (var membership in members)
        { membership.User.MajorId = majorId; membership.User.AcademicProfileStatus = "VERIFIED"; }
        db.Add(new ProjectRegistrationSnapshot { ProjectId = s.ProjectId, ProjectPeriodId = periodId,
            LeadDepartmentId = s.Users.DepartmentId, SubmittedBy = s.Users.Student, SubmittedAt = Now.AddDays(-1),
            SnapshotJson = JsonSerializer.Serialize(new RegistrationEvidence(
                new TeamAcademicScopeDto("SINGLE_MAJOR", majorId, s.Users.DepartmentId, [new(majorId, 1, 5, "Project delivery")], Guid.NewGuid()),
                new(1, 5, 1, "acceptance"), await db.AcademicSemesters.Where(x => x.Id == s.SemesterId).Select(x => x.OrganizationId).SingleAsync(),
                Now.AddDays(-3), Now.AddDays(-1), members.Select(m => new RegisteredMemberDto(m.UserId, m.User.FullName, majorId, m.IsLeader)).ToArray(),
                [s.Users.DepartmentId])) });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Uploaded_final_package_flows_through_scoring_publication_archive_and_reload()
    {
        using var app = new Factory(database);
        var ready = await Prepare(app);
        var s = ready.Scenario;
        using var leader = app.CreateAuthenticatedClient(s.Users.Student, roles: ["STUDENT"]);
        using var staff = app.CreateAuthenticatedClient(s.Users.Staff, roles: ["DEPARTMENT_STAFF"]);
        using var evaluator = app.CreateAuthenticatedClient(s.Users.Lecturer, roles: ["LECTURER"]);
        using var member = app.CreateAuthenticatedClient(s.MemberId, roles: ["STUDENT"]);
        using var outside = app.CreateAuthenticatedClient(s.Users.OutsideStaff, roles: ["DEPARTMENT_STAFF"]);
        using var anonymous = app.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(Route(s.ProjectId))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.PostAsJsonAsync(Route(s.ProjectId), ready.Input)).StatusCode);
        var checklist = await Body<FinalSubmissionChecklistDto>(await leader.GetAsync(Route(s.ProjectId) + "/checklist"));
        Assert.True(checklist.CanSubmit);
        var package = await Body<FinalSubmissionDto>(await leader.PostAsJsonAsync(Route(s.ProjectId), ready.Input));
        Assert.NotEmpty(package.Items);
        var file = Assert.Single(package.Items[0].Files);
        var rubric = await Body<RubricDto>(await staff.PostAsJsonAsync("/api/v1/rubrics",
            new CreateRubricRequest(s.Users.DepartmentId, s.SemesterId, "R-" + Guid.NewGuid().ToString("N"),
                "Acceptance rubric", null, [new("Project quality", null, 100, 10, 0, true)])));
        rubric = await Body<RubricDto>(await staff.PostAsJsonAsync($"/api/v1/rubrics/{rubric.Id}/publish", new ChangeRubricStatusRequest(rubric.ConcurrencyToken)));
        // Advance the clock and persist phase configuration, without mutating project lifecycle state.
        app.Clock.Now = Now.AddDays(2);
        long periodId;
        await using (var db = database.CreateContext())
        {
            await db.ProjectPeriods.Where(p => p.AcademicSemesterId == s.SemesterId).ExecuteUpdateAsync(x => x.SetProperty(p => p.Status, "CLOSED"));
            var period = new AIPMS.Infrastructure.Persistence.Generated.Models.ProjectPeriod
            {
                AcademicSemesterId = s.SemesterId, Code = "EVAL-" + Guid.NewGuid().ToString("N"), Name = "Acceptance evaluation",
                PeriodType = "EVALUATION", Status = "ACTIVE", StartAt = app.Clock.Now.AddHours(-1), EndAt = app.Clock.Now.AddDays(1), RubricId = rubric.Id
            };
            db.ProjectPeriods.Add(period);
            await db.SaveChangesAsync();
            periodId = period.Id;
        }
        await SeedEvaluationRoster(s, periodId);
        var candidatePath = $"/api/v1/projects/{s.ProjectId}/eligible-evaluators?periodId={periodId}";
        var candidates = await Body<PagedResult<EligibleEvaluatorDto>>(await staff.GetAsync(candidatePath));
        Assert.Contains(candidates.Items, c => c.UserId == s.Users.Lecturer && c.EvaluationTypes.Contains("LECTURER"));
        Assert.Equal(HttpStatusCode.Forbidden, (await outside.GetAsync(candidatePath)).StatusCode);
        var schemeDraft = await Body<EvaluationSchemeDto>(await staff.PostAsJsonAsync("/api/v1/evaluation-schemes",
            new SaveEvaluationSchemeRequest(s.ProjectId, periodId, "Acceptance scheme", 5, [new("Project quality", "COMMON", null, rubric.Id, 100, 100, 1)])));
        var scheme = await Body<EvaluationSchemeDto>(await staff.PostAsJsonAsync($"/api/v1/evaluation-schemes/{schemeDraft.Id}/publish", new SchemeTokenRequest(schemeDraft.ConcurrencyToken)));
        var assignment = await Body<EvaluationAssignmentDto>(await staff.PostAsJsonAsync($"/api/v1/projects/{s.ProjectId}/evaluation-assignments",
            new AssignEvaluatorRequest(s.Users.Lecturer, periodId, "LECTURER", "COMMON", null, null, scheme.Components.Single().Id)));
        var resultPath = $"/api/v1/projects/{s.ProjectId}/result";
        Assert.Equal(HttpStatusCode.NotFound, (await member.GetAsync(resultPath)).StatusCode);
        var draft = await Body<EvaluationDraftDto>(await evaluator.PostAsync($"/api/v1/evaluation-assignments/{assignment.Id}/evaluation", null));
        draft = await Body<EvaluationDraftDto>(await evaluator.PutAsJsonAsync($"/api/v1/evaluations/{draft.Id}/draft",
            new SaveEvaluationDraftRequest(draft.ConcurrencyToken, "Accepted", [new(rubric.Criteria[0].Id, 8.5m, null)])));
        await Body<EvaluationDraftDto>(await evaluator.PostAsJsonAsync($"/api/v1/evaluations/{draft.Id}/finalize", new FinalizeEvaluationRequest(draft.ConcurrencyToken)));
        var finalized = await staff.PostAsJsonAsync($"/api/v1/evaluation-assignments/{assignment.Id}/revoke", new RevokeEvaluatorRequest(assignment.ConcurrencyToken, "Cannot revoke finalized"));
        Assert.Equal(HttpStatusCode.Conflict, finalized.StatusCode);
        using (var json = JsonDocument.Parse(await finalized.Content.ReadAsStringAsync()))
            Assert.Equal("FINALIZED_ASSIGNMENT", json.RootElement.GetProperty("code").GetString());
        var preview = await Body<ProjectResultPreviewDto>(await staff.GetAsync(resultPath + "/preview"));
        Assert.True(preview.CanPublish);
        var result = await Body<ProjectResultDto>(await staff.PostAsJsonAsync(resultPath, new PublishProjectResultRequest(preview.ConfirmationToken)));
        Assert.Equal(8.5m, result.TotalScore);
        Assert.Equal(result.Id, (await Body<ProjectResultDto>(await member.GetAsync(resultPath))).Id);
        var actions = await Body<ProjectWorkflowActionsDto>(await staff.GetAsync($"/api/v1/projects/{s.ProjectId}/actions"));
        Assert.True(Assert.Single(actions.Actions.Where(a => a.Code == "archive_project")).Allowed);
        var memberActions = await Body<ProjectWorkflowActionsDto>(await member.GetAsync($"/api/v1/projects/{s.ProjectId}/actions"));
        Assert.False(Assert.Single(memberActions.Actions.Where(a => a.Code == "archive_project")).Allowed);
        var archived = await Body<ProjectDto>(await staff.PostAsJsonAsync($"/api/v1/projects/{s.ProjectId}/archive", new ArchiveProjectRequest(actions.ConcurrencyToken, "Acceptance completed")));
        Assert.Equal("ARCHIVED", archived.Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await outside.GetAsync($"/api/v1/projects/{s.ProjectId}/actions")).StatusCode);
        Assert.Equal("Original final report", await (await staff.GetAsync(Route(s.ProjectId) + $"/files/{file.Id}/download")).Content.ReadAsStringAsync());
        await database.Migrate();
        using var reopened = new Factory(database);
        reopened.Clock.Now = app.Clock.Now;
        using var reload = reopened.CreateAuthenticatedClient(s.Users.Staff, roles: ["DEPARTMENT_STAFF"]);
        var persisted = await Body<ProjectDto>(await reload.GetAsync($"/api/v1/projects/{s.ProjectId}"));
        Assert.Equal("ARCHIVED", persisted.Status);
        var history = await Body<ProjectStatusHistoryDto[]>(await reload.GetAsync($"/api/v1/projects/{s.ProjectId}/history"));
        Assert.Contains(history, h => h.NewStatus == "ARCHIVED" && h.Reason == "Acceptance completed");
        Assert.Equal(result.Id, (await Body<ProjectResultDto>(await reload.GetAsync(resultPath))).Id);
        await using var verify = database.CreateContext();
        Assert.Single(await verify.AuditLogs.Where(a => a.Action == "PROJECT_ARCHIVED" && a.EntityId == s.ProjectId.ToString()).ToArrayAsync());
    }

    [Fact]
    public async Task Archive_checks_persisted_roles_scope_state_token_and_rolls_back_audit_failure()
    {
        using var app = new Factory(database);
        var s = await database.Seed();
        using var staff = app.CreateAuthenticatedClient(s.Users.Staff, roles: ["DEPARTMENT_STAFF"]);
        var url = $"/api/v1/projects/{s.ProjectId}";
        var active = await Body<ProjectWorkflowActionsDto>(await staff.GetAsync(url + "/actions"));
        Assert.Contains("ARCHIVE_NOT_ALLOWED", Assert.Single(active.Actions.Where(a => a.Code == "archive_project")).Reasons);
        var blocked = await staff.PostAsJsonAsync(url + "/archive", new ArchiveProjectRequest(active.ConcurrencyToken, null));
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        using (var json = JsonDocument.Parse(await blocked.Content.ReadAsStringAsync()))
            Assert.Equal("ARCHIVE_NOT_ALLOWED", json.RootElement.GetProperty("code").GetString());
        await using (var db = database.CreateContext())
            await db.Projects.Where(p => p.Id == s.ProjectId).ExecuteUpdateAsync(x => x.SetProperty(p => p.Status, "COMPLETED"));
        var current = await Body<ProjectWorkflowActionsDto>(await staff.GetAsync(url + "/actions"));
        Assert.True(Assert.Single(current.Actions.Where(a => a.Code == "archive_project")).Allowed);
        var stale = await staff.PostAsJsonAsync(url + "/archive", new ArchiveProjectRequest(active.ConcurrencyToken, null));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using (var json = JsonDocument.Parse(await stale.Content.ReadAsStringAsync()))
            Assert.Equal("STALE_CONCURRENCY_TOKEN", json.RootElement.GetProperty("code").GetString());
        foreach (var id in new[] { s.Users.Student, s.Users.OutsideStaff, s.Users.Lecturer })
        {
            using var forged = app.CreateAuthenticatedClient(id, roles: ["ADMIN"]);
            Assert.Equal(HttpStatusCode.Forbidden, (await forged.PostAsJsonAsync(url + "/archive", new ArchiveProjectRequest(current.ConcurrencyToken, null))).StatusCode);
        }
        app.FailAudit = true;
        Assert.Equal(HttpStatusCode.InternalServerError, (await staff.PostAsJsonAsync(url + "/archive", new ArchiveProjectRequest(current.ConcurrencyToken, null))).StatusCode);
        await using (var db = database.CreateContext())
        {
            Assert.Equal("COMPLETED", (await db.Projects.FindAsync(s.ProjectId))!.Status);
            Assert.False(await db.ProjectStatusHistories.AnyAsync(h => h.ProjectId == s.ProjectId && h.NewStatus == "ARCHIVED"));
            Assert.False(await db.AuditLogs.AnyAsync(a => a.EntityId == s.ProjectId.ToString() && a.Action == "PROJECT_ARCHIVED"));
        }
        app.FailAudit = false;
        var responses = await Task.WhenAll(staff.PostAsJsonAsync(url + "/archive", new ArchiveProjectRequest(current.ConcurrencyToken, "First")),
            staff.PostAsJsonAsync(url + "/archive", new ArchiveProjectRequest(current.ConcurrencyToken, "Second")));
        Assert.Single(responses.Where(r => r.IsSuccessStatusCode));
        Assert.Single(responses.Where(r => r.StatusCode == HttpStatusCode.Conflict));
        await using var final = database.CreateContext();
        Assert.Single(await final.ProjectStatusHistories.Where(h => h.ProjectId == s.ProjectId && h.NewStatus == "ARCHIVED").ToArrayAsync());
    }
}
