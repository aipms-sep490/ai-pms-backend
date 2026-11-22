using System.Net;
using System.Net.Http.Json;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Cold;
using AIPMS.Application.Features.Evaluations.DTOs;
using AIPMS.Application.Features.Results.DTOs;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.IntegrationTests.Evaluations;

public sealed class FinalGradingAndPublicationRegressionTests(EvaluationDraftDatabaseFixture database) : IClassFixture<EvaluationDraftDatabaseFixture>
{
    private static async Task<T> Body<T>(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private async Task<(EvaluationScenario Scenario, long Major)> Seed()
    {
        var s = await database.Seed(lockedSubmission: true, confirmedCommittee: true);
        await using var db = database.CreateContext();
        var project = await db.Projects.Include(p => p.Team).Include(p => p.ProjectMajors).SingleAsync(p => p.Id == s.ProjectId);
        var major = project.ProjectMajors.Single().MajorId;
        return (s, major);
    }

    [Fact]
    public async Task Regression_01_Cold_report_templates_returns_200_and_supports_scope()
    {
        var (s, major) = await Seed();
        using var app = new EvaluationFactory(database);
        using var staff = app.CreateAuthenticatedClient(s.Scope.Users.Staff);

        // Verify GET /cold-report-templates returns 200 (not 500 Invalid column name 'scope')
        var getRes = await staff.GetAsync($"/api/v1/cold-report-templates?projectPeriodId={s.PeriodId}&page=1&pageSize=20");
        Assert.Equal(HttpStatusCode.OK, getRes.StatusCode);
        var paged = await Body<PagedResult<ColdTemplateDto>>(getRes);
        Assert.NotNull(paged);

        // Verify POST /cold-report-templates with Scope returns 201 Created
        var createRes = await staff.PostAsJsonAsync("/api/v1/cold-report-templates", new CreateColdTemplateRequest(
            major, s.PeriodId, "Regression Test Template", [new("REPORT_01", "Technical Report", true)], "MAJOR_SPECIFIC"));
        Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);
        var created = await Body<ColdTemplateDto>(createRes);
        Assert.Equal("MAJOR_SPECIFIC", created.Scope);
        Assert.Equal(major, created.MajorId);
    }

    [Fact]
    public async Task Regression_02_to_10_Evaluation_to_Publication_and_Student_View_End_To_End()
    {
        var (s, major) = await Seed();
        using var app = new EvaluationFactory(database);
        using var staff = app.CreateAuthenticatedClient(s.Scope.Users.Staff);
        using var lecturer = app.CreateAuthenticatedClient(s.Scope.Users.Lecturer);
        using var student = app.CreateAuthenticatedClient(s.Scope.Users.Student);
        using var otherStudent = app.CreateAuthenticatedClient(s.Scope.Users.OtherLecturer); // Non-team user

        var projectUrl = $"/api/v1/projects/{s.ProjectId}/result";
        var studentResultUrl = $"/api/v1/projects/{s.ProjectId}/students/{s.Scope.Users.Student}/result";

        // Regression 09 / TEST-P0-06: Student cannot view unpublished score (returns 403 RESULT_NOT_PUBLISHED)
        var preRead = await student.GetAsync(studentResultUrl);
        Assert.Equal(HttpStatusCode.Forbidden, preRead.StatusCode);

        // 1. Setup Approved Scheme (Official)
        var schemeDraft = await Body<EvaluationSchemeDto>(await staff.PostAsJsonAsync("/api/v1/evaluation-schemes", new SaveEvaluationSchemeRequest(
            s.ProjectId, s.PeriodId, "End-to-End Scored Scheme", 5,
            [
                new("Common Stage", "COMMON", null, s.RubricId, 100, 60, 1, Stage: "ON_GOING"),
                new("Individual Stage", "INDIVIDUAL", major, s.RubricId, 0, 40, 1, Stage: "ON_GOING")
            ],
            Policy: new(false, "APPROVED", "QD-2026/AIPMS-OFFICIAL", CalculationVersion: "LEGACY_V1")
        )));

        var scheme = await Body<EvaluationSchemeDto>(await staff.PostAsJsonAsync(
            $"/api/v1/evaluation-schemes/{schemeDraft.Id}/publish", new SchemeTokenRequest(schemeDraft.ConcurrencyToken)));
        Assert.Equal("PUBLISHED", scheme.Status);

        // Regression 06: Publish is rejected when evaluations are not finalized
        var unreadyPreview = await Body<ProjectResultPreviewDto>(await staff.GetAsync(studentResultUrl + "/preview"));
        Assert.False(unreadyPreview.CanPublish);
        Assert.Null(unreadyPreview.TotalScore);
        Assert.Contains(unreadyPreview.Blockers, b => b.StartsWith("EVALUATION_NOT_FINALIZED") || b.StartsWith("MISSING_EVALUATOR"));

        var publishBlocked = await staff.PostAsJsonAsync(studentResultUrl, new PublishProjectResultRequest(unreadyPreview.ConfirmationToken));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, publishBlocked.StatusCode);

        // 2. Assign Evaluators
        var commonComp = scheme.Components.Single(c => c.Scope == "COMMON");
        var indivComp = scheme.Components.Single(c => c.Scope == "INDIVIDUAL");

        var assignCommon = await Body<EvaluationAssignmentDto>(await staff.PostAsJsonAsync(
            $"/api/v1/projects/{s.ProjectId}/evaluation-assignments",
            new AssignEvaluatorRequest(s.Scope.Users.Lecturer, s.PeriodId, "LECTURER", commonComp.Scope, null, null, commonComp.Id)));

        var assignIndiv = await Body<EvaluationAssignmentDto>(await staff.PostAsJsonAsync(
            $"/api/v1/projects/{s.ProjectId}/evaluation-assignments",
            new AssignEvaluatorRequest(s.Scope.Users.Lecturer, s.PeriodId, "LECTURER", indivComp.Scope, major, s.Scope.Users.Student, indivComp.Id)));

        // Regression 02: Evaluation Draft is created when eligible
        var draftCommon = await Body<EvaluationDraftDto>(await lecturer.PostAsync($"/api/v1/evaluation-assignments/{assignCommon.Id}/evaluation", null));
        Assert.NotNull(draftCommon);
        Assert.Equal("DRAFT", draftCommon.Status);

        var draftIndiv = await Body<EvaluationDraftDto>(await lecturer.PostAsync($"/api/v1/evaluation-assignments/{assignIndiv.Id}/evaluation", null));
        Assert.NotNull(draftIndiv);
        Assert.Equal("DRAFT", draftIndiv.Status);

        // Regression 04: Evaluator enters valid criterion scores and finalizes
        draftCommon = await Body<EvaluationDraftDto>(await lecturer.PutAsJsonAsync(
            $"/api/v1/evaluations/{draftCommon.Id}/draft",
            new SaveEvaluationDraftRequest(draftCommon.ConcurrencyToken, "Good Common", [new(s.Criteria[0], 8, null), new(s.Criteria[1], 16, null)])));

        var finalizedCommon = await Body<EvaluationDraftDto>(await lecturer.PostAsJsonAsync(
            $"/api/v1/evaluations/{draftCommon.Id}/finalize", new { draftCommon.ConcurrencyToken }));
        Assert.Equal("FINALIZED", finalizedCommon.Status);
        Assert.NotNull(finalizedCommon.TotalScore);

        draftIndiv = await Body<EvaluationDraftDto>(await lecturer.PutAsJsonAsync(
            $"/api/v1/evaluations/{draftIndiv.Id}/draft",
            new SaveEvaluationDraftRequest(draftIndiv.ConcurrencyToken, "Good Individual", [new(s.Criteria[0], 9, null), new(s.Criteria[1], 18, null)])));

        var finalizedIndiv = await Body<EvaluationDraftDto>(await lecturer.PostAsJsonAsync(
            $"/api/v1/evaluations/{draftIndiv.Id}/finalize", new { draftIndiv.ConcurrencyToken }));
        Assert.Equal("FINALIZED", finalizedIndiv.Status);
        Assert.NotNull(finalizedIndiv.TotalScore);

        // Regression 05: Final score is calculated with numeric value and breakdown
        var preview = await Body<ProjectResultPreviewDto>(await staff.GetAsync(studentResultUrl + "/preview"));
        Assert.NotNull(preview.TotalScore);
        Assert.True(preview.TotalScore > 0);
        Assert.True(preview.CanPublish);
        Assert.Empty(preview.Blockers);
        Assert.Equal("PASSED", preview.Outcome);
        Assert.NotNull(preview.Breakdown);

        // Regression 07: Publish succeeds when eligible
        var publishRes = await staff.PostAsJsonAsync(studentResultUrl, new PublishProjectResultRequest(preview.ConfirmationToken));
        Assert.Equal(HttpStatusCode.OK, publishRes.StatusCode);
        var publishedResult = await Body<StudentResultDto>(publishRes);
        Assert.Equal(preview.TotalScore, publishedResult.TotalScore);
        Assert.Equal("PASSED", publishedResult.Outcome);

        // Regression 08: Student views their published result with exact score
        var studentViewRes = await student.GetAsync(studentResultUrl);
        Assert.Equal(HttpStatusCode.OK, studentViewRes.StatusCode);
        var studentResult = await Body<StudentResultDto>(studentViewRes);
        Assert.Equal(preview.TotalScore, studentResult.TotalScore);
        Assert.Equal(s.Scope.Users.Student, studentResult.StudentId);
        Assert.Equal("{}", studentResult.SnapshotJson); // Audit snapshot redacted for student privacy

        // Regression 10: Non-team member / outsider cannot access result (Forbidden)
        var forbiddenRes = await otherStudent.GetAsync(studentResultUrl);
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenRes.StatusCode);
    }
}
