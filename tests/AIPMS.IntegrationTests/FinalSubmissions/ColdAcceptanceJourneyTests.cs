using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AIPMS.Application.Features.Cold;
using AIPMS.Application.Features.Evaluations.DTOs;
using AIPMS.Application.Features.FinalSubmissions.DTOs;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.IntegrationTests.FinalSubmissions;

public sealed partial class FinalSubmissionEndpointTests
{
    [Fact]
    public async Task Real_final_and_cold_upload_lock_committee_scoring_readback_are_separate_evidence_packages()
    {
        using var app = new Factory(database);
        var ready = await Prepare(app); var s = ready.Scenario;
        await SeedEvaluationRoster(s, s.PeriodId);
        using var staff = app.CreateAuthenticatedClient(s.Users.Staff);
        using var leader = app.CreateAuthenticatedClient(s.Users.Student);
        using var member = app.CreateAuthenticatedClient(s.MemberId);
        using var evaluator = app.CreateAuthenticatedClient(s.Users.NewLecturer);
        long major;
        await using (var db = database.CreateContext())
            major = await db.ProjectMajors.Where(m => m.ProjectId == s.ProjectId).Select(m => m.MajorId).SingleAsync();
        var template = await Body<ColdTemplateDto>(await staff.PostAsJsonAsync("/api/v1/cold-report-templates",
            new CreateColdTemplateRequest(major, s.PeriodId, "Individual review", [new("REPORT", "Individual report")])));
        var coldUrl = $"/api/v1/projects/{s.ProjectId}/cold";
        var checklist = await Body<ColdChecklistDto>(await staff.PostAsJsonAsync(coldUrl + "/checklist",
            new ApplyColdChecklistRequest(s.PeriodId, new(Now.AddMinutes(30)),
                [new(template.Id, "INDIVIDUAL", s.Users.Student), new(template.Id, "INDIVIDUAL", s.MemberId)])));
        foreach (var item in checklist.Items)
        {
            var owner = item.StudentId == s.Users.Student ? leader : member;
            var draft = await Body<ColdSubmissionDto>(await owner.PostAsJsonAsync($"/api/v1/cold-checklist-items/{item.Id}/draft", new SaveColdDraftRequest(0)));
            using var form = new MultipartFormDataContent();
            var bytes = new ByteArrayContent("%PDF-1.7\nCOLD acceptance"u8.ToArray()); bytes.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
            form.Add(bytes, "file", "cold.pdf"); form.Add(new StringContent(draft.ConcurrencyToken), "concurrencyToken");
            draft = await Body<ColdSubmissionDto>(await owner.PostAsync($"/api/v1/cold-submissions/{draft.Id}/files", form));
            await Body<ColdSubmissionDto>(await owner.PostAsJsonAsync($"/api/v1/cold-submissions/{draft.Id}/submit", new ColdTokenRequest(draft.ConcurrencyToken)));
        }
        checklist = await Body<ColdChecklistDto>(await staff.GetAsync(coldUrl + "/checklist"));
        var locked = await Body<ColdChecklistDto>(await staff.PostAsJsonAsync(coldUrl + "/lock", new ColdTokenRequest(checklist.ConcurrencyToken)));
        var final = await Body<FinalSubmissionDto>(await leader.PostAsJsonAsync(Route(s.ProjectId), ready.Input));
        var rubric = await Body<RubricDto>(await staff.PostAsJsonAsync("/api/v1/rubrics",
            new CreateRubricRequest(s.Users.DepartmentId, s.SemesterId, Guid.NewGuid().ToString("N"), "COLD rubric", null,
                [new("Report quality", null, 100, 10, 0, true)])));
        rubric = await Body<RubricDto>(await staff.PostAsJsonAsync($"/api/v1/rubrics/{rubric.Id}/publish", new ChangeRubricStatusRequest(rubric.ConcurrencyToken)));
        long periodId;
        await using (var db = database.CreateContext())
        {
            var period = new M.ProjectPeriod { AcademicSemesterId = s.SemesterId, Code = "EVAL", Name = "COLD evaluation",
                PeriodType = "EVALUATION", Status = "ACTIVE", StartAt = Now.AddHours(-1), EndAt = Now.AddDays(1), RubricId = rubric.Id };
            db.Add(period); await db.SaveChangesAsync(); periodId = period.Id;
        }
        var scheme = await Body<EvaluationSchemeDto>(await staff.PostAsJsonAsync("/api/v1/evaluation-schemes",
            new SaveEvaluationSchemeRequest(s.ProjectId, periodId, "Separate stage", 5,
                [new("Defense", "COMMON", null, rubric.Id, 100, 100, 1), new("COLD", "INDIVIDUAL", major, rubric.Id, 0, 0, 1, Stage: "COLD")])));
        scheme = await Body<EvaluationSchemeDto>(await staff.PostAsJsonAsync($"/api/v1/evaluation-schemes/{scheme.Id}/publish", new SchemeTokenRequest(scheme.ConcurrencyToken)));
        var assignment = await Body<EvaluationAssignmentDto>(await staff.PostAsJsonAsync($"/api/v1/projects/{s.ProjectId}/evaluation-assignments",
            new AssignEvaluatorRequest(s.Users.NewLecturer, periodId, "COMMITTEE", "INDIVIDUAL", major, s.Users.Student, scheme.Components.Single(c => c.Stage == "COLD").Id)));
        var evidenceUrl = $"/api/v1/evaluation-assignments/{assignment.Id}/evidence";
        var evidence = await Body<EvaluationAssignmentEvidenceDto>(await evaluator.GetAsync(evidenceUrl));
        Assert.Single(evidence.Items); Assert.Equal(s.Users.Student, evidence.Items.Single().StudentId);
        var otherFile = locked.Items.Single(i => i.StudentId == s.MemberId).Latest!.Files.Single().Id;
        Assert.Equal(HttpStatusCode.NotFound, (await evaluator.GetAsync(evidenceUrl + $"/files/{otherFile}")).StatusCode);
        var draftScore = await Body<EvaluationDraftDto>(await evaluator.PostAsync($"/api/v1/evaluation-assignments/{assignment.Id}/evaluation", null));
        draftScore = await Body<EvaluationDraftDto>(await evaluator.PutAsJsonAsync($"/api/v1/evaluations/{draftScore.Id}/draft",
            new SaveEvaluationDraftRequest(draftScore.ConcurrencyToken, null, [new(rubric.Criteria[0].Id, 8, null)])));
        var responses = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => evaluator.PostAsJsonAsync($"/api/v1/evaluations/{draftScore.Id}/finalize", new { draftScore.ConcurrencyToken })));
        Assert.Single(responses.Where(r => r.IsSuccessStatusCode)); Assert.Single(responses.Where(r => r.StatusCode == HttpStatusCode.Conflict));
        var readback = await Body<EvaluationDraftDto>(await evaluator.GetAsync($"/api/v1/evaluations/{draftScore.Id}"));
        Assert.Equal(final.Id, readback.Finalization!.Evidence.FinalSubmissionId); Assert.Equal(locked.Id, readback.Finalization.Evidence.ColdPackageId);
        Assert.Single((await Body<ColdProgressDto>(await leader.GetAsync(coldUrl + "/progress"))).Grading);
        var otherProgress = await Body<ColdProgressDto>(await member.GetAsync(coldUrl + "/progress"));
        Assert.Null(otherProgress.Grading.Single().Score); Assert.Equal(s.MemberId, otherProgress.Grading.Single().StudentId);
        await using var verify = database.CreateContext();
        Assert.Single(await verify.Set<EvaluationFinalization>().Where(f => f.EvaluationId == draftScore.Id).ToArrayAsync());
        Assert.True(await verify.AuditLogs.AnyAsync(a => a.Action == "COLD_PACKAGE_LOCKED" && a.EntityId == locked.Id.ToString()));
        Assert.True(await verify.AuditLogs.AnyAsync(a => a.Action == "EVALUATION_FINALIZED" && a.EntityId == draftScore.Id.ToString()));
    }
}
