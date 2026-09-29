using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AIPMS.Application.Features.Evaluations.DTOs;
using AIPMS.Application.Features.Projects.DTOs;
using AIPMS.Application.Features.Results.DTOs;
using AIPMS.Application.Features.Semesters.DTOs;
using AIPMS.Application.Features.Teams.DTOs;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.IntegrationTests.Evaluations;

public sealed partial class PolicyEvaluationEndpointTests(EvaluationDraftDatabaseFixture database) : IClassFixture<EvaluationDraftDatabaseFixture>
{
    private static async Task<T> Body<T>(HttpResponseMessage response)
    { Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync()); return (await response.Content.ReadFromJsonAsync<T>())!; }

    private async Task<(EvaluationScenario Scenario, long Major)> Seed()
    {
        var s = await database.Seed(); await using var db = database.CreateContext();
        var project = await db.Projects.Include(p => p.Team).Include(p => p.ProjectMajors).SingleAsync(p => p.Id == s.ProjectId);
        var major = project.ProjectMajors.Single().MajorId;
        var student = await db.Users.SingleAsync(u => u.Id == s.Scope.Users.Student);
        student.MajorId = major; student.AcademicProfileStatus = "VERIFIED";
        db.TeamMembers.Add(new() { TeamId = project.TeamId, AcademicSemesterId = s.Scope.SemesterId, UserId = student.Id, IsLeader = true });
        db.Set<ProjectRegistrationSnapshot>().Add(new() { ProjectId = project.Id, ProjectPeriodId = s.PeriodId,
            LeadDepartmentId = s.Scope.Users.DepartmentId, SubmittedBy = student.Id, SubmittedAt = EvaluationDraftDatabaseFixture.Now.AddDays(-1),
            SnapshotJson = JsonSerializer.Serialize(new RegistrationEvidence(
                new TeamAcademicScopeDto("SINGLE_MAJOR", major, s.Scope.Users.DepartmentId, [new MajorRequirementDto(major, 1, 5, "Engineering")], Guid.NewGuid()),
                new(1, 5, 1, "test"), 1, EvaluationDraftDatabaseFixture.Now.AddDays(-2), EvaluationDraftDatabaseFixture.Now.AddDays(2),
                [new(student.Id, student.FullName, major, true)], [s.Scope.Users.DepartmentId])) });
        await db.SaveChangesAsync(); return (s, major);
    }
    private static SaveEvaluationSchemeRequest Input(EvaluationScenario s, long major) => new(s.ProjectId, s.PeriodId, "Scoped final evaluation", 5,
        [new("Common", "COMMON", null, s.RubricId, 100, 70, 1), new("Individual", "INDIVIDUAL", major, s.RubricId, 0, 30, 1)]);
    private static async Task<EvaluationSchemeDto> Scheme(HttpClient staff, EvaluationScenario s, long major)
    {
        var draft = await Body<EvaluationSchemeDto>(await staff.PostAsJsonAsync("/api/v1/evaluation-schemes", Input(s, major)));
        return await Body<EvaluationSchemeDto>(await staff.PostAsJsonAsync($"/api/v1/evaluation-schemes/{draft.Id}/publish", new SchemeTokenRequest(draft.ConcurrencyToken)));
    }
    private static async Task<EvaluationDraftDto> Evaluate(HttpClient staff, HttpClient lecturer, EvaluationScenario s, SchemeComponentDto c, decimal score)
    {
        var assignment = await Body<EvaluationAssignmentDto>(await staff.PostAsJsonAsync($"/api/v1/projects/{s.ProjectId}/evaluation-assignments",
            new AssignEvaluatorRequest(s.Scope.Users.Lecturer, s.PeriodId, "LECTURER", c.Scope, c.MajorId,
                c.Scope == "INDIVIDUAL" ? s.Scope.Users.Student : null, c.Id)));
        Assert.Equal(c.Scope, assignment.Scope);
        var draft = await Body<EvaluationDraftDto>(await lecturer.PostAsync($"/api/v1/evaluation-assignments/{assignment.Id}/evaluation", null));
        draft = await Body<EvaluationDraftDto>(await lecturer.PutAsJsonAsync($"/api/v1/evaluations/{draft.Id}/draft",
            new SaveEvaluationDraftRequest(draft.ConcurrencyToken, "Final", [new(s.Criteria[0], score, null), new(s.Criteria[1], score * 2, null)])));
        return await Body<EvaluationDraftDto>(await lecturer.PostAsJsonAsync($"/api/v1/evaluations/{draft.Id}/finalize", new { draft.ConcurrencyToken }));
    }

    [Fact]
    public async Task Scoped_scores_publish_individual_result_without_changing_project_score_and_hide_other_students()
    {
        var (s, major) = await Seed(); using var app = new EvaluationFactory(database);
        using var staff = app.CreateAuthenticatedClient(s.Scope.Users.Staff); using var lecturer = app.CreateAuthenticatedClient(s.Scope.Users.Lecturer);
        using var student = app.CreateAuthenticatedClient(s.Scope.Users.Student); using var other = app.CreateAuthenticatedClient(s.Scope.Users.OtherLecturer);
        var scheme = await Scheme(staff, s, major);
        var projectUrl = $"/api/v1/projects/{s.ProjectId}/result";
        var studentUrl = $"/api/v1/projects/{s.ProjectId}/students/{s.Scope.Users.Student}/result";
        var missing = await Body<ProjectResultPreviewDto>(await staff.GetAsync(projectUrl + "/preview")); Assert.False(missing.CanPublish);
        await Evaluate(staff, lecturer, s, scheme.Components.Single(c => c.Scope == "COMMON"), 8);
        await Evaluate(staff, lecturer, s, scheme.Components.Single(c => c.Scope == "INDIVIDUAL"), 6);
        var preview = await Body<ProjectResultPreviewDto>(await staff.GetAsync(studentUrl + "/preview")); Assert.Equal(7.4m, preview.TotalScore);
        Assert.Equal(HttpStatusCode.Forbidden, (await student.GetAsync(studentUrl + "/preview")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await other.GetAsync(studentUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await staff.PostAsJsonAsync(studentUrl, new PublishProjectResultRequest(new string('a',64)))).StatusCode);
        using (var fail = new EvaluationFactory(database, failAudit: true))
        using (var failingStaff = fail.CreateAuthenticatedClient(s.Scope.Users.Staff))
            Assert.Equal(HttpStatusCode.InternalServerError, (await failingStaff.PostAsJsonAsync(studentUrl, new PublishProjectResultRequest(preview.ConfirmationToken))).StatusCode);
        await using (var verify = database.CreateContext()) Assert.False(await verify.Set<StudentResult>().AnyAsync(r => r.ProjectId == s.ProjectId));
        await Body<StudentResultDto>(await staff.PostAsJsonAsync(studentUrl, new PublishProjectResultRequest(preview.ConfirmationToken)));
        var read = await Body<StudentResultDto>(await student.GetAsync(studentUrl)); Assert.Equal(7.4m, read.TotalScore); Assert.Equal("{}", read.SnapshotJson);
        var projectPreview = await Body<ProjectResultPreviewDto>(await staff.GetAsync(projectUrl + "/preview")); Assert.Equal(8m, projectPreview.TotalScore);
        await Body<ProjectResultDto>(await staff.PostAsJsonAsync(projectUrl, new PublishProjectResultRequest(projectPreview.ConfirmationToken)));
        await using var db = database.CreateContext();
        Assert.Equal(2, await db.Set<StudentResultEvaluation>().CountAsync(e => db.Set<StudentResult>().Any(r => r.Id == e.ResultId && r.ProjectId == s.ProjectId)));
        Assert.True(await db.Notifications.AnyAsync(n => n.NotificationType == "STUDENT_RESULT_PUBLISHED"));
    }

    [Fact]
    public async Task Scheme_lock_scope_assignment_and_legacy_guards()
    {
        var (s, major) = await Seed(); using var app = new EvaluationFactory(database); using var staff = app.CreateAuthenticatedClient(s.Scope.Users.Staff);
        using var student = app.CreateAuthenticatedClient(s.Scope.Users.Student);
        Assert.Equal(HttpStatusCode.Forbidden, (await student.PostAsJsonAsync("/api/v1/evaluation-schemes", Input(s, major))).StatusCode);
        var scheme = await Scheme(staff, s, major);
        Assert.Equal(HttpStatusCode.Conflict, (await staff.PutAsJsonAsync($"/api/v1/evaluation-schemes/{scheme.Id}", Input(s, major) with { ConcurrencyToken = scheme.ConcurrencyToken })).StatusCode);
        var url = $"/api/v1/projects/{s.ProjectId}/evaluation-assignments";
        Assert.Equal(HttpStatusCode.Conflict, (await staff.PostAsJsonAsync(url, new AssignEvaluatorRequest(s.Scope.Users.Lecturer, s.PeriodId, "LECTURER"))).StatusCode);
        var common = scheme.Components.First(c => c.Scope == "COMMON");
        Assert.Equal(HttpStatusCode.Conflict, (await staff.PostAsJsonAsync(url, new AssignEvaluatorRequest(s.Scope.Users.Lecturer, s.PeriodId, "LECTURER", "MAJOR_SPECIFIC", major, null, common.Id))).StatusCode);
        var input = new AssignEvaluatorRequest(s.Scope.Users.Lecturer, s.PeriodId, "LECTURER", "COMMON", null, null, common.Id);
        var requests = await Task.WhenAll(staff.PostAsJsonAsync(url, input), staff.PostAsJsonAsync(url, input));
        Assert.Single(requests.Where(r => r.StatusCode == HttpStatusCode.Created)); Assert.Single(requests.Where(r => r.StatusCode == HttpStatusCode.Conflict));
        var successor = await Body<EvaluationSchemeDto>(await staff.PostAsJsonAsync($"/api/v1/evaluation-schemes/{scheme.Id}/versions", new SchemeTokenRequest(scheme.ConcurrencyToken)));
        Assert.Equal(HttpStatusCode.Conflict, (await staff.PostAsJsonAsync($"/api/v1/evaluation-schemes/{successor.Id}/publish", new SchemeTokenRequest(successor.ConcurrencyToken))).StatusCode);
    }

    [Fact]
    public async Task Policy_successor_uses_expected_version_and_audit_failure_rolls_back()
    {
        var (s, _) = await Seed(); using var app = new EvaluationFactory(database); using var staff = app.CreateAuthenticatedClient(s.Scope.Users.Staff);
        var url = $"/api/v1/project-periods/{s.PeriodId}";
        var before = await Body<PeriodPolicyDto>(await staff.GetAsync(url + "/effective-policy"));
        var input = new UpdatePeriodPolicyRequest(before.Version, "SUCCESSOR", before.Policy with { MaxProjectsPerSupervisor = 7 },
            new(EvaluationDraftDatabaseFixture.Now), new(EvaluationDraftDatabaseFixture.Now.AddHours(12)));
        using (var fail = new EvaluationFactory(database, failAudit: true))
        using (var failedStaff = fail.CreateAuthenticatedClient(s.Scope.Users.Staff))
            Assert.Equal(HttpStatusCode.InternalServerError, (await failedStaff.PutAsJsonAsync(url + "/policy", input)).StatusCode);
        var draft = await Body<PeriodPolicyDto>(await staff.PutAsJsonAsync(url + "/policy", input));
        Assert.Equal(before.Version+1, draft.Version);
        Assert.Equal(HttpStatusCode.Conflict, (await staff.PutAsJsonAsync(url + "/policy", input)).StatusCode);
        var published = await Body<PeriodPolicyDto>(await staff.PutAsJsonAsync(url + "/policy", input with { ExpectedVersion = draft.Version, Operation = "PUBLISH", ConcurrencyToken = draft.ConcurrencyToken }));
        Assert.Equal("PUBLISHED", published.Status);
        var active = await Body<PeriodPolicyDto>(await staff.GetAsync(url + "/effective-policy")); Assert.Equal(published.Id, active.Id);
        Assert.Equal(7, active.Policy.MaxProjectsPerSupervisor);
    }
}
