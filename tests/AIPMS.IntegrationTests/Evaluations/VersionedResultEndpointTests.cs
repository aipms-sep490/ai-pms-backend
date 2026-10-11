using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AIPMS.Application.Features.Evaluations.DTOs;
using AIPMS.Application.Features.Results.DTOs;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.IntegrationTests.Evaluations;

public sealed partial class PolicyEvaluationEndpointTests
{
    [Fact]
    public async Task Default_demo_scheme_can_be_locked_and_scored_but_cannot_publish_official_results()
    {
        var (s, major) = await Seed();
        using var app = new EvaluationFactory(database);
        using var staff = app.CreateAuthenticatedClient(s.Scope.Users.Staff);
        using var lecturer = app.CreateAuthenticatedClient(s.Scope.Users.Lecturer);
        var draft = await Body<EvaluationSchemeDto>(await staff.PostAsJsonAsync("/api/v1/evaluation-schemes",
            Input(s, major) with { Policy = null }));
        Assert.True(draft.Policy!.IsDemo);
        Assert.Null(draft.LockedAt);
        var scheme = await Body<EvaluationSchemeDto>(await staff.PostAsJsonAsync($"/api/v1/evaluation-schemes/{draft.Id}/publish", new SchemeTokenRequest(draft.ConcurrencyToken)));
        Assert.NotNull(scheme.LockedAt);
        Assert.Equal("DEMO", scheme.Policy!.ApprovalStatus);
        var url = $"/api/v1/projects/{s.ProjectId}/students/{s.Scope.Users.Student}/result";
        var pending = await Body<ProjectResultPreviewDto>(await staff.GetAsync(url + "/preview"));
        Assert.Null(pending.TotalScore);
        Assert.Equal("PENDING", pending.Breakdown!.Readiness);
        await Evaluate(staff, lecturer, s, scheme.Components.Single(c => c.Scope == "COMMON"), 0);
        await Evaluate(staff, lecturer, s, scheme.Components.Single(c => c.Scope == "INDIVIDUAL"), 0);
        var preview = await Body<ProjectResultPreviewDto>(await staff.GetAsync(url + "/preview"));
        Assert.Equal(0m, preview.TotalScore);
        Assert.Equal("READY", preview.Breakdown!.Readiness);
        Assert.False(preview.CanPublish);
        Assert.Contains("DEMO_SCHEME_NOT_OFFICIAL", preview.Blockers);
        Assert.Equal(HttpStatusCode.Conflict, (await staff.PostAsJsonAsync(url, new PublishProjectResultRequest(preview.ConfirmationToken))).StatusCode);
        var projectUrl = $"/api/v1/projects/{s.ProjectId}/result";
        var project = await Body<ProjectResultPreviewDto>(await staff.GetAsync(projectUrl + "/preview"));
        Assert.Equal(0m, project.TotalScore);
        Assert.Equal(HttpStatusCode.Conflict, (await staff.PostAsJsonAsync(projectUrl, new PublishProjectResultRequest(project.ConfirmationToken))).StatusCode);
        await using var db = database.CreateContext();
        Assert.Empty(await db.Set<StudentResult>().Where(r => r.ProjectId == s.ProjectId).ToArrayAsync());
        Assert.Empty(await db.Set<ProjectResult>().Where(r => r.ProjectId == s.ProjectId).ToArrayAsync());
        Assert.Equal("FINAL_SUBMISSION", (await db.Projects.FindAsync(s.ProjectId))!.Status);
    }

    [Fact]
    public async Task Explicit_evaluator_weights_require_each_configured_slot_and_preserve_frozen_result_readback()
    {
        var (s, major) = await Seed();
        using var app = new EvaluationFactory(database);
        using var staff = app.CreateAuthenticatedClient(s.Scope.Users.Staff);
        using var student = app.CreateAuthenticatedClient(s.Scope.Users.Student);
        var input = Input(s, major) with {
            Policy = new(false, "APPROVED", "SYNTHETIC-V2-TEST-ONLY"),
            Components = [new("Weighted common", "COMMON", null, s.RubricId, 100, 100, 2,
                [new(s.Scope.Users.Lecturer, 75), new(s.Scope.Users.NewLecturer, 25)])] };
        var draft = await Body<EvaluationSchemeDto>(await staff.PostAsJsonAsync("/api/v1/evaluation-schemes", input));
        var scheme = await Body<EvaluationSchemeDto>(await staff.PostAsJsonAsync($"/api/v1/evaluation-schemes/{draft.Id}/publish", new SchemeTokenRequest(draft.ConcurrencyToken)));
        var component = scheme.Components.Single();
        var studentUrl = $"/api/v1/projects/{s.ProjectId}/students/{s.Scope.Users.Student}/result";
        var projectUrl = $"/api/v1/projects/{s.ProjectId}/result";
        async Task Score(long evaluatorId, decimal score)
        {
            using var evaluator = app.CreateAuthenticatedClient(evaluatorId);
            var assignment = await Body<EvaluationAssignmentDto>(await staff.PostAsJsonAsync($"/api/v1/projects/{s.ProjectId}/evaluation-assignments",
                new AssignEvaluatorRequest(evaluatorId, s.PeriodId, "LECTURER", "COMMON", null, null, component.Id)));
            var evaluation = await Body<EvaluationDraftDto>(await evaluator.PostAsync($"/api/v1/evaluation-assignments/{assignment.Id}/evaluation", null));
            evaluation = await Body<EvaluationDraftDto>(await evaluator.PutAsJsonAsync($"/api/v1/evaluations/{evaluation.Id}/draft",
                new SaveEvaluationDraftRequest(evaluation.ConcurrencyToken, "Private evaluator comment",
                    [new(s.Criteria[0], score, "Private criterion"), new(s.Criteria[1], score * 2, null)])));
            await Body<EvaluationDraftDto>(await evaluator.PostAsJsonAsync($"/api/v1/evaluations/{evaluation.Id}/finalize", new { evaluation.ConcurrencyToken }));
        }
        await Score(s.Scope.Users.Lecturer, 8);
        var missing = await Body<ProjectResultPreviewDto>(await staff.GetAsync(studentUrl + "/preview"));
        Assert.Null(missing.TotalScore);
        Assert.False(missing.CanPublish);
        await Score(s.Scope.Users.NewLecturer, 4);
        var preview = await Body<ProjectResultPreviewDto>(await staff.GetAsync(studentUrl + "/preview"));
        Assert.Equal(7m, preview.TotalScore);
        Assert.Equal(7m, preview.Breakdown!.UnroundedTotal);
        Assert.Equal(new decimal[] { 75, 25 }, preview.Contributions.Select(c => c.WeightPercent));
        Assert.True(preview.CanPublish);
        Assert.Equal(HttpStatusCode.Conflict, (await staff.PostAsJsonAsync(studentUrl, new PublishProjectResultRequest(missing.ConfirmationToken))).StatusCode);
        await Body<StudentResultDto>(await staff.PostAsJsonAsync(studentUrl, new PublishProjectResultRequest(preview.ConfirmationToken)));
        Assert.Equal(HttpStatusCode.Conflict, (await staff.PostAsJsonAsync(studentUrl, new PublishProjectResultRequest(preview.ConfirmationToken))).StatusCode);
        var project = await Body<ProjectResultPreviewDto>(await staff.GetAsync(projectUrl + "/preview"));
        await Body<ProjectResultDto>(await staff.PostAsJsonAsync(projectUrl, new PublishProjectResultRequest(project.ConfirmationToken)));
        await using (var db = database.CreateContext())
        {
            (await db.Set<EvaluationScheme>().FindAsync(scheme.Id))!.Name = "Changed after publication";
            (await db.Set<EvaluationSchemeComponent>().FindAsync(component.Id))!.Name = "Changed component";
            (await db.Users.FindAsync(s.Scope.Users.Student))!.MajorId = null;
            await db.SaveChangesAsync();
        }
        var historical = await Body<StudentResultDto>(await student.GetAsync(studentUrl));
        Assert.Equal(7m, historical.TotalScore);
        Assert.Equal(input.Name, historical.Breakdown!.SchemeName);
        Assert.Equal("Weighted common", historical.Breakdown.Components.Single().Name);
        Assert.Equal("{}", historical.SnapshotJson);
        Assert.DoesNotContain("Private", JsonSerializer.Serialize(historical));
        var projectHistory = await Body<ProjectResultDto>(await staff.GetAsync(projectUrl));
        Assert.Null(projectHistory.FrozenInputs);
        Assert.Equal(input.Name, projectHistory.Breakdown!.SchemeName);
    }

    [Fact]
    public async Task Unknown_legacy_approval_remains_read_only_and_successor_does_not_inherit_approval()
    {
        var (s, major) = await Seed();
        using var app = new EvaluationFactory(database);
        using var staff = app.CreateAuthenticatedClient(s.Scope.Users.Staff);
        var scheme = await Scheme(staff, s, major);
        await using (var db = database.CreateContext())
        {
            var row = (await db.Set<EvaluationScheme>().FindAsync(scheme.Id))!;
            row.GovernanceJson = null; row.LockedAt = null;
            await db.SaveChangesAsync();
        }
        var preview = await Body<ProjectResultPreviewDto>(await staff.GetAsync($"/api/v1/projects/{s.ProjectId}/result/preview"));
        Assert.False(preview.CanPublish);
        Assert.Contains("ACADEMIC_APPROVAL_UNKNOWN", preview.Blockers);
        Assert.True(preview.Breakdown!.IsLegacy);
        var successor = await Body<EvaluationSchemeDto>(await staff.PostAsJsonAsync($"/api/v1/evaluation-schemes/{scheme.Id}/versions", new SchemeTokenRequest(scheme.ConcurrencyToken)));
        Assert.True(successor.Policy!.IsDemo);
        Assert.Equal("DECIMAL_V2", successor.Policy.CalculationVersion);
        Assert.Equal("DECIMAL_V2", successor.CalculationRule);
        Assert.Null(successor.Policy.ApprovalReference);
        Assert.Null(successor.LockedAt);
    }

    [Fact]
    public async Task Malformed_evaluator_weights_and_approval_metadata_return_validation_errors()
    {
        var (s, major) = await Seed();
        using var app = new EvaluationFactory(database);
        using var staff = app.CreateAuthenticatedClient(s.Scope.Users.Staff);
        Assert.Equal(HttpStatusCode.BadRequest, (await staff.PostAsJsonAsync("/api/v1/evaluation-schemes",
            Input(s, major) with { Policy = new(false, "APPROVED", null) })).StatusCode);
        var bad = Input(s, major) with { Components = [new("Bad", "COMMON", null, s.RubricId, 100, 100, 1, [null!])] };
        Assert.Equal(HttpStatusCode.BadRequest, (await staff.PostAsJsonAsync("/api/v1/evaluation-schemes", bad)).StatusCode);
    }
}
