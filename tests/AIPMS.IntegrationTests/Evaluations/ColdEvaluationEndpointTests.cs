using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AIPMS.Application.Features.Cold;
using AIPMS.Application.Features.Evaluations.DTOs;
using AIPMS.Application.Features.Results.DTOs;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.IntegrationTests.Evaluations;

public sealed partial class ColdSubmissionEndpointTests
{
    private static async Task<EvaluationSchemeDto> ColdScheme(HttpClient staff, EvaluationScenario s, long major,
        string scope = "MAJOR_SPECIFIC", int required = 1, bool mapped = false)
    {
        var draft = await Body<EvaluationSchemeDto>(await staff.PostAsJsonAsync("/api/v1/evaluation-schemes",
            new SaveEvaluationSchemeRequest(s.ProjectId, s.PeriodId, "COLD and defense demo", 5,
                [new("Defense", "COMMON", null, s.RubricId, mapped ? 50 : 100, mapped ? 50 : 100, 1),
                 new("COLD", scope, scope == "COMMON" ? null : major, s.RubricId, mapped ? 50 : 0, mapped ? 50 : 0, required, Stage: "COLD")])));
        return await Body<EvaluationSchemeDto>(await staff.PostAsJsonAsync($"/api/v1/evaluation-schemes/{draft.Id}/publish", new SchemeTokenRequest(draft.ConcurrencyToken)));
    }
    private static Task<HttpResponseMessage> AssignCold(HttpClient staff, EvaluationScenario s, SchemeComponentDto c, long evaluator) =>
        staff.PostAsJsonAsync($"/api/v1/projects/{s.ProjectId}/evaluation-assignments",
            new AssignEvaluatorRequest(evaluator, s.PeriodId, "COMMITTEE", c.Scope, c.MajorId,
                c.Scope == "INDIVIDUAL" ? s.Scope.Users.Student : null, c.Id));
    private static async Task<EvaluationDraftDto> ScoreCold(HttpClient evaluator, EvaluationScenario s, long assignmentId, decimal score)
    {
        var draft = await Body<EvaluationDraftDto>(await evaluator.PostAsync($"/api/v1/evaluation-assignments/{assignmentId}/evaluation", null));
        return await Body<EvaluationDraftDto>(await evaluator.PutAsJsonAsync($"/api/v1/evaluations/{draft.Id}/draft",
            new SaveEvaluationDraftRequest(draft.ConcurrencyToken, "Synthetic COLD score", [new(s.Criteria[0], score, null), new(s.Criteria[1], score * 2, null)])));
    }
    private static async Task<ColdChecklistDto> LockCold(HttpClient staff, long projectId)
    {
        var checklist = await Body<ColdChecklistDto>(await staff.GetAsync(ChecklistUrl(projectId)));
        return await Body<ColdChecklistDto>(await staff.PostAsJsonAsync(LockUrl(projectId), new ColdTokenRequest(checklist.ConcurrencyToken)));
    }

    [Theory]
    [InlineData("COMMON")]
    [InlineData("MAJOR_SPECIFIC")]
    [InlineData("INDIVIDUAL")]
    public async Task Committee_assignment_reads_only_locked_cold_version_and_revocation_removes_access(string scope)
    {
        using var app = new ColdFactory(database);
        var (s, checklist, template) = await Prepare(app, scope == "INDIVIDUAL" ? scope : "MAJOR_SPECIFIC", finalPackage: true);
        using var staff = app.CreateAuthenticatedClient(s.Scope.Users.Staff);
        using var student = app.CreateAuthenticatedClient(s.Scope.Users.Student);
        using var evaluator = app.CreateAuthenticatedClient(s.Scope.Users.Lecturer);
        using var outsider = app.CreateAuthenticatedClient(s.Scope.Users.NewLecturer);
        var scheme = await ColdScheme(staff, s, template.MajorId, scope);
        var component = scheme.Components.Single(c => c.Stage == "COLD");
        Assert.Equal(HttpStatusCode.Conflict, (await AssignCold(staff, s, component, s.Scope.Users.Lecturer)).StatusCode);
        var submitted = await SubmitReport(student, checklist.Items.Single().Id);
        var locked = await LockCold(staff, s.ProjectId);
        await using (var db = database.CreateContext())
        {
            var primary = await db.SupervisorAssignments.Where(a => a.ProjectId == s.ProjectId && a.IsPrimary && a.EndedAt == null)
                .Select(a => a.SupervisorProfile.UserId).SingleAsync();
            Assert.Equal(HttpStatusCode.Conflict, (await AssignCold(staff, s, component, primary)).StatusCode);
        }
        var assignment = await Body<EvaluationAssignmentDto>(await AssignCold(staff, s, component, s.Scope.Users.Lecturer));
        Assert.Equal("COLD", assignment.Stage); Assert.Equal(locked.Id, assignment.ColdPackageId);
        var url = $"/api/v1/evaluation-assignments/{assignment.Id}/evidence";
        var evidence = await Body<EvaluationAssignmentEvidenceDto>(await evaluator.GetAsync(url));
        Assert.Null(evidence.FinalSubmissionId); Assert.Equal(locked.Id, evidence.ColdPackageId);
        var file = Assert.Single(evidence.Items); Assert.Equal(submitted.Id, file.ColdSubmissionId);
        Assert.Null(file.DeliverableVersionId); Assert.Equal("COLD_SUBMISSION", file.SourceType);
        Assert.DoesNotContain("storage", JsonSerializer.Serialize(evidence), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.Forbidden, (await outsider.GetAsync(url)).StatusCode);
        await using (var db = database.CreateContext())
        {
            (await db.Files.FindAsync(file.FileId))!.StoragePath = "changed-live-file";
            await db.SaveChangesAsync();
        }
        Assert.Equal("%PDF-1.7\nSynthetic report", await (await evaluator.GetAsync(file.DownloadUrl)).Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Forbidden, (await evaluator.GetAsync(submitted.Files.Single().DownloadUrl)).StatusCode);
        await Body<EvaluationAssignmentDto>(await staff.PostAsJsonAsync($"/api/v1/evaluation-assignments/{assignment.Id}/revoke",
            new RevokeEvaluatorRequest(assignment.ConcurrencyToken, "Synthetic revocation")));
        Assert.Equal(HttpStatusCode.Forbidden, (await evaluator.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await evaluator.GetAsync(file.DownloadUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await evaluator.PostAsync($"/api/v1/evaluation-assignments/{assignment.Id}/evaluation", null)).StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cold_scores_require_all_slots_preserve_zero_and_only_use_explicit_result_mapping(bool mapped)
    {
        using var app = new ColdFactory(database); var (s, checklist, template) = await Prepare(app, finalPackage: true);
        using var staff = app.CreateAuthenticatedClient(s.Scope.Users.Staff);
        using var student = app.CreateAuthenticatedClient(s.Scope.Users.Student);
        using var evaluator = app.CreateAuthenticatedClient(s.Scope.Users.Lecturer);
        using var second = app.CreateAuthenticatedClient(s.Scope.Users.NewLecturer);
        await SubmitReport(student, checklist.Items.Single().Id); await LockCold(staff, s.ProjectId);
        var scheme = await ColdScheme(staff, s, template.MajorId, required: 2, mapped: mapped);
        var cold = scheme.Components.Single(c => c.Stage == "COLD");
        var progressUrl = $"/api/v1/projects/{s.ProjectId}/cold/progress";
        var missing = await Body<ColdProgressDto>(await student.GetAsync(progressUrl));
        Assert.Null(missing.Grading.Single().Score); Assert.False(missing.Ready);
        var a = await Body<EvaluationAssignmentDto>(await AssignCold(staff, s, cold, s.Scope.Users.Lecturer));
        var b = await Body<EvaluationAssignmentDto>(await AssignCold(staff, s, cold, s.Scope.Users.NewLecturer));
        var first = await ScoreCold(evaluator, s, a.Id, 0);
        first = await Body<EvaluationDraftDto>(await evaluator.PostAsJsonAsync($"/api/v1/evaluations/{first.Id}/finalize", new { first.ConcurrencyToken }));
        Assert.Equal("COLD", first.Finalization!.Evidence.Stage);
        Assert.Empty(first.Finalization.Evidence.DeliverableVersionIds); Assert.Single(first.Finalization.Evidence.ColdSubmissionIds!);
        Assert.Null((await Body<ColdProgressDto>(await staff.GetAsync(progressUrl))).Grading.Single().Score);
        var last = await ScoreCold(second, s, b.Id, 0);
        await Body<EvaluationDraftDto>(await second.PostAsJsonAsync($"/api/v1/evaluations/{last.Id}/finalize", new { last.ConcurrencyToken }));
        var progress = await Body<ColdProgressDto>(await student.GetAsync(progressUrl));
        Assert.True(progress.Ready); Assert.Equal(0m, progress.Grading.Single().Score);
        Assert.Equal(mapped, progress.Grading.Single().ContributesToResult);
        var defense = scheme.Components.Single(c => c.Stage == "DEFENSE");
        var d = await Body<EvaluationAssignmentDto>(await AssignCold(staff, s, defense, s.Scope.Users.Lecturer));
        var draft = await ScoreCold(evaluator, s, d.Id, 8);
        await Body<EvaluationDraftDto>(await evaluator.PostAsJsonAsync($"/api/v1/evaluations/{draft.Id}/finalize", new { draft.ConcurrencyToken }));
        var preview = await Body<ProjectResultPreviewDto>(await staff.GetAsync($"/api/v1/projects/{s.ProjectId}/result/preview"));
        Assert.Equal(mapped ? 4m : 8m, preview.TotalScore);
        Assert.False(preview.CanPublish); Assert.Contains("DEMO_SCHEME_NOT_OFFICIAL", preview.Blockers);
        await using var db = database.CreateContext();
        Assert.True(await db.AuditLogs.AnyAsync(audit => audit.Action == "EVALUATION_FINALIZED" && audit.EntityId == first.Id.ToString()));
        Assert.Empty(await db.Set<StudentResult>().Where(r => r.ProjectId == s.ProjectId).ToArrayAsync());
        Assert.Equal("FINAL_SUBMISSION", (await db.Projects.FindAsync(s.ProjectId))!.Status);
    }

    [Fact]
    public async Task Cold_finalization_audit_failure_rolls_back_and_stale_retry_conflicts()
    {
        using var app = new ColdFactory(database); var (s, checklist, template) = await Prepare(app, finalPackage: true);
        using var staff = app.CreateAuthenticatedClient(s.Scope.Users.Staff);
        using var student = app.CreateAuthenticatedClient(s.Scope.Users.Student);
        using var evaluator = app.CreateAuthenticatedClient(s.Scope.Users.Lecturer);
        await SubmitReport(student, checklist.Items.Single().Id); await LockCold(staff, s.ProjectId);
        var scheme = await ColdScheme(staff, s, template.MajorId);
        var assignment = await Body<EvaluationAssignmentDto>(await AssignCold(staff, s, scheme.Components.Single(c => c.Stage == "COLD"), s.Scope.Users.Lecturer));
        var draft = await ScoreCold(evaluator, s, assignment.Id, 7);
        var url = $"/api/v1/evaluations/{draft.Id}/finalize";
        app.FailAction = "EVALUATION_FINALIZED";
        Assert.Equal(HttpStatusCode.InternalServerError, (await evaluator.PostAsJsonAsync(url, new { draft.ConcurrencyToken })).StatusCode);
        await using (var db = database.CreateContext())
        {
            Assert.False(await db.Set<EvaluationFinalization>().AnyAsync(f => f.EvaluationId == draft.Id));
            Assert.False(await db.AuditLogs.AnyAsync(a => a.Action == "EVALUATION_FINALIZED" && a.EntityId == draft.Id.ToString()));
        }
        app.FailAction = null;
        await Body<EvaluationDraftDto>(await evaluator.PostAsJsonAsync(url, new { draft.ConcurrencyToken }));
        Assert.Equal(HttpStatusCode.Conflict, (await evaluator.PostAsJsonAsync(url, new { draft.ConcurrencyToken })).StatusCode);
        await database.Migrate(); await database.Migrate();
        Assert.Equal("FINALIZED", (await Body<EvaluationDraftDto>(await evaluator.GetAsync($"/api/v1/evaluations/{draft.Id}"))).Status);
    }

    [Fact]
    public async Task Changed_locked_cold_manifest_blocks_read_scoring_capability_and_finalization()
    {
        using var app = new ColdFactory(database); var (s, checklist, template) = await Prepare(app, finalPackage: true);
        using var staff = app.CreateAuthenticatedClient(s.Scope.Users.Staff);
        using var student = app.CreateAuthenticatedClient(s.Scope.Users.Student);
        using var evaluator = app.CreateAuthenticatedClient(s.Scope.Users.Lecturer);
        var submitted = await SubmitReport(student, checklist.Items.Single().Id); await LockCold(staff, s.ProjectId);
        var scheme = await ColdScheme(staff, s, template.MajorId);
        var assignment = await Body<EvaluationAssignmentDto>(await AssignCold(staff, s, scheme.Components.Single(c => c.Stage == "COLD"), s.Scope.Users.Lecturer));
        var draft = await ScoreCold(evaluator, s, assignment.Id, 8);
        await using (var db = database.CreateContext())
        {
            (await db.Set<ColdSubmissionFile>().SingleAsync(f => f.SubmissionId == submitted.Id)).StorageKey = "tampered-locked-key";
            await db.SaveChangesAsync();
        }
        var detail = await Body<EvaluationAssignmentDetailDto>(await evaluator.GetAsync($"/api/v1/evaluation-assignments/{assignment.Id}"));
        Assert.False(detail.CanScore);
        Assert.Equal(HttpStatusCode.Conflict, (await evaluator.GetAsync($"/api/v1/evaluation-assignments/{assignment.Id}/evidence")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await evaluator.PostAsJsonAsync($"/api/v1/evaluations/{draft.Id}/finalize", new { draft.ConcurrencyToken })).StatusCode);
        await using var verify = database.CreateContext();
        Assert.False(await verify.Set<EvaluationFinalization>().AnyAsync(f => f.EvaluationId == draft.Id));
    }
}
