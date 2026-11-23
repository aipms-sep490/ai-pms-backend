using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Cold;
using AIPMS.Application.Features.Evaluations.DTOs;
using AIPMS.Application.Features.Results.DTOs;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.IntegrationTests.Evaluations;

public sealed class PrP0BScoringAndOptionalDefenseTests(EvaluationDraftDatabaseFixture database) : IClassFixture<EvaluationDraftDatabaseFixture>
{
    private static DateTime Now => EvaluationDraftDatabaseFixture.Now;

    private static async Task<T> Body<T>(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private static async Task<string?> ReadProblemCode(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        if (doc.RootElement.TryGetProperty("code", out var code)) return code.GetString();
        return null;
    }

    private static async Task<(EvaluationScenario Scenario, long MajorId, long PrimaryUserId, long[] GateIds, ColdChecklistDto Checklist)> SeedPol01Project(
        EvaluationFactory app, EvaluationDraftDatabaseFixture database)
    {
        var s = await database.Seed(lockedSubmission: true, confirmedCommittee: false);
        long majorId;
        long primaryUserId;
        long[] gateIds;
        await using (var db = database.CreateContext())
        {
            majorId = await db.ProjectMajors.Where(m => m.ProjectId == s.ProjectId).Select(m => m.MajorId).SingleAsync();
            primaryUserId = await db.SupervisorAssignments
                .Where(a => a.ProjectId == s.ProjectId && a.IsPrimary && a.EndedAt == null)
                .Select(a => a.SupervisorProfile.UserId).SingleAsync();

            db.Set<DefenseCommittee>().Add(new DefenseCommittee
            {
                ProjectId = s.ProjectId,
                Name = "Non-blocking V4 Committee",
                Status = "CONFIRMED",
                CreatedBy = s.Scope.Users.Staff,
                CreatedAt = Now,
                Members =
                [
                    new() { UserId = s.Scope.Users.Lecturer, MajorId = majorId },
                    new() { UserId = s.Scope.Users.NewLecturer, MajorId = majorId }
                ]
            });

            var template = new ReviewGateTemplate
            {
                ProjectPeriodId = s.PeriodId,
                Code = "POL03_" + Guid.NewGuid().ToString("N")[..8],
                Name = "POL-03 Gate Template",
                Mode = "SINGLE_MAJOR",
                Major = "IT",
                Version = 1,
                Status = "PUBLISHED",
                CreatedBy = s.Scope.Users.Staff,
                CreatedAt = Now,
                UpdatedAt = Now
            };
            db.ReviewGateTemplates.Add(template);
            await db.SaveChangesAsync();

            var g1 = new ProjectGateInstance { ProjectId = s.ProjectId, TemplateId = template.Id, TemplateVersion = 1, Code = "G1", Sequence = 1, Title = "Gate 1", Objective = "G1", Status = "PASSED", CreatedAt = Now };
            var g2 = new ProjectGateInstance { ProjectId = s.ProjectId, TemplateId = template.Id, TemplateVersion = 1, Code = "G2", Sequence = 2, Title = "Gate 2", Objective = "G2", Status = "PASSED", CreatedAt = Now };
            var g3 = new ProjectGateInstance { ProjectId = s.ProjectId, TemplateId = template.Id, TemplateVersion = 1, Code = "G3", Sequence = 3, Title = "Gate 3", Objective = "G3", Status = "PASSED", CreatedAt = Now };
            db.ProjectGateInstances.AddRange(g1, g2, g3);
            await db.SaveChangesAsync();
            gateIds = [g1.Id, g2.Id, g3.Id];
        }

        using var staff = app.CreateAuthenticatedClient(s.Scope.Users.Staff, roles: ["DEPARTMENT_STAFF"]);
        using var student = app.CreateAuthenticatedClient(s.Scope.Users.Student);

        var coldTemplate = await Body<ColdTemplateDto>(await staff.PostAsJsonAsync("/api/v1/cold-report-templates",
            new CreateColdTemplateRequest(null, s.PeriodId, "POL-01 Cold Report", [new("REPORT", "Cold Package Report")], "COMMON")));
        var checklist = await Body<ColdChecklistDto>(await staff.PostAsJsonAsync($"/api/v1/projects/{s.ProjectId}/cold/checklist",
            new ApplyColdChecklistRequest(s.PeriodId, new(Now.AddHours(4)), [new(coldTemplate.Id, "COMMON")])));

        var item = Assert.Single(checklist.Items);
        var coldDraft = await Body<ColdSubmissionDto>(await student.PostAsJsonAsync($"/api/v1/cold-checklist-items/{item.Id}/draft",
            new SaveColdDraftRequest(0, "Final cold evidence")));
        using (var form = new MultipartFormDataContent())
        {
            var file = new ByteArrayContent(Encoding.UTF8.GetBytes("%PDF-1.7\nPOL01 report"));
            file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
            form.Add(file, "file", "cold-report.pdf");
            form.Add(new StringContent(coldDraft.ConcurrencyToken), "concurrencyToken");
            coldDraft = await Body<ColdSubmissionDto>(await student.PostAsync($"/api/v1/cold-submissions/{coldDraft.Id}/files", form));
        }
        await Body<ColdSubmissionDto>(await student.PostAsJsonAsync($"/api/v1/cold-submissions/{coldDraft.Id}/submit",
            new ColdTokenRequest(coldDraft.ConcurrencyToken)));
        checklist = await Body<ColdChecklistDto>(await student.GetAsync($"/api/v1/projects/{s.ProjectId}/cold/checklist"));
        checklist = await Body<ColdChecklistDto>(await student.PostAsJsonAsync($"/api/v1/projects/{s.ProjectId}/cold/lock",
            new ColdTokenRequest(checklist.ConcurrencyToken)));

        return (s, majorId, primaryUserId, gateIds, checklist);
    }

    private static async Task<EvaluationDraftDto> ScoreAndFinalize(HttpClient evaluator, EvaluationScenario s, long assignmentId, decimal scoreOn10, string comment)
    {
        var draft = await Body<EvaluationDraftDto>(await evaluator.PostAsync($"/api/v1/evaluation-assignments/{assignmentId}/evaluation", null));
        draft = await Body<EvaluationDraftDto>(await evaluator.PutAsJsonAsync($"/api/v1/evaluations/{draft.Id}/draft",
            new SaveEvaluationDraftRequest(draft.ConcurrencyToken, comment,
                [new(s.Criteria[0], scoreOn10, null), new(s.Criteria[1], scoreOn10 * 2m, null)])));
        return await Body<EvaluationDraftDto>(await evaluator.PostAsJsonAsync($"/api/v1/evaluations/{draft.Id}/finalize",
            new { draft.ConcurrencyToken }));
    }

    [Fact]
    public async Task Pol06_nonblocking_defense_Pol01_30_50_20_Pol03_gvhd_20_and_atomic_batch_publish_work_end_to_end()
    {
        using var app = new EvaluationFactory(database);
        var (s, majorId, primaryId, gateIds, _) = await SeedPol01Project(app, database);

        using var staff = app.CreateAuthenticatedClient(s.Scope.Users.Staff, roles: ["DEPARTMENT_STAFF"]);
        using var primary = app.CreateAuthenticatedClient(primaryId);
        using var evaluator = app.CreateAuthenticatedClient(s.Scope.Users.Lecturer);
        using var student = app.CreateAuthenticatedClient(s.Scope.Users.Student);

        // 1. Official POL-01 single-major scheme (30% Cold + 50% Defense + 20% GVHD G1-G3, floor 4.00)
        var schemeDraft = await Body<EvaluationSchemeDto>(await staff.PostAsJsonAsync("/api/v1/evaluation-schemes",
            new SaveEvaluationSchemeRequest(
                s.ProjectId, s.PeriodId, "POL-01 Official Single Major", 5.00m,
                [
                    new("Cold Document Review", "COMMON", null, s.RubricId, 37.5m, 30m, 1, Stage: "COLD", TargetScope: "PROJECT"),
                    new("Defense Presentation", "COMMON", null, s.RubricId, 62.5m, 50m, 1, Stage: "DEFENSE", TargetScope: "PROJECT"),
                    new("GVHD Individual Process", "INDIVIDUAL", majorId, s.RubricId, 0m, 20m, 1, Stage: "ON_GOING", TargetScope: "STUDENT")
                ],
                Policy: new(false, "APPROVED", "QD-POL01-2026", "POL_SINGLE_30_50_20",
                    AIPMS.Application.Features.Evaluations.Services.VersionedResultCalculator.PolV4Version,
                    FailingComponentThreshold: 4.00m,
                    IndividualGateWeights: [new(1, 20m), new(2, 35m), new(3, 45m)]))));
        var scheme = await Body<EvaluationSchemeDto>(await staff.PostAsJsonAsync(
            $"/api/v1/evaluation-schemes/{schemeDraft.Id}/publish", new SchemeTokenRequest(schemeDraft.ConcurrencyToken)));

        // 2. POL-03 Negative RBAC: Evaluator and Student cannot write GVHD 20%; Student cannot read pre-publish
        var evalWriteAttempt = await evaluator.PutAsJsonAsync(
            $"/api/v1/projects/{s.ProjectId}/gates/{gateIds[0]}/individual-process-scores/{s.Scope.Users.Student}",
            new UpsertIndividualProcessScoreRequest(8.0m, "Unauthorized"));
        Assert.Equal(HttpStatusCode.Forbidden, evalWriteAttempt.StatusCode);

        var studentPreReadProcess = await student.GetAsync($"/api/v1/projects/{s.ProjectId}/individual-process-scores");
        Assert.Equal(HttpStatusCode.Forbidden, studentPreReadProcess.StatusCode);
        Assert.Equal("RESULT_NOT_PUBLISHED", await ReadProblemCode(studentPreReadProcess));

        // 3. Primary Supervisor scores & finalizes G1=7.00 (20%), G2=8.00 (35%), G3=9.00 (45%) => cumulative = 8.25
        decimal[] gateValues = [7.00m, 8.00m, 9.00m];
        for (int i = 0; i < 3; i++)
        {
            var saved = await Body<GateIndividualProcessScoreItemDto>(await primary.PutAsJsonAsync(
                $"/api/v1/projects/{s.ProjectId}/gates/{gateIds[i]}/individual-process-scores/{s.Scope.Users.Student}",
                new UpsertIndividualProcessScoreRequest(gateValues[i], $"Good progress at G{i + 1}")));
            var fin = await Body<GateIndividualProcessScoreItemDto>(await primary.PostAsJsonAsync(
                $"/api/v1/projects/{s.ProjectId}/gates/{gateIds[i]}/individual-process-scores/{s.Scope.Users.Student}/finalize",
                new FinalizeIndividualProcessScoreRequest(saved.ConcurrencyToken)));
            Assert.Equal("FINALIZED", fin.Status);

            // Immutability check: cannot edit finalized gate score
            var editFinalized = await primary.PutAsJsonAsync(
                $"/api/v1/projects/{s.ProjectId}/gates/{gateIds[i]}/individual-process-scores/{s.Scope.Users.Student}",
                new UpsertIndividualProcessScoreRequest(9.50m, "Tamper", ConcurrencyToken: fin.ConcurrencyToken));
            Assert.Equal(HttpStatusCode.Conflict, editFinalized.StatusCode);
            Assert.Equal(WorkflowErrorCodes.ImmutableVersion, await ReadProblemCode(editFinalized));
        }

        // Evaluator sees finalized 20% cumulative read-only
        var evalProcessView = await Body<ProjectIndividualProcessScoresDto>(
            await evaluator.GetAsync($"/api/v1/projects/{s.ProjectId}/individual-process-scores"));
        Assert.False(evalProcessView.CanEdit);
        Assert.Equal(8.25m, Assert.Single(evalProcessView.Students).CumulativeScore);

        // 4. Cold scoring + TEST-P0-07 (422 INCOMPLETE_SCORES on missing criterion) + TEST-P0-06 (Cold score hidden from Student)
        var coldComp = scheme.Components.Single(c => c.Stage == "COLD");
        var defenseComp = scheme.Components.Single(c => c.Stage == "DEFENSE");

        var coldAssign = await Body<EvaluationAssignmentDto>(await staff.PostAsJsonAsync(
            $"/api/v1/projects/{s.ProjectId}/evaluation-assignments",
            new AssignEvaluatorRequest(s.Scope.Users.Lecturer, s.PeriodId, "COMMITTEE", coldComp.Scope, null, null, coldComp.Id)));

        var incompleteDraft = await Body<EvaluationDraftDto>(await evaluator.PostAsync(
            $"/api/v1/evaluation-assignments/{coldAssign.Id}/evaluation", null));
        incompleteDraft = await Body<EvaluationDraftDto>(await evaluator.PutAsJsonAsync(
            $"/api/v1/evaluations/{incompleteDraft.Id}/draft",
            new SaveEvaluationDraftRequest(incompleteDraft.ConcurrencyToken, "Partial", [new(s.Criteria[0], 8.0m, null)])));
        var incompleteFinalizeRes = await evaluator.PostAsJsonAsync(
            $"/api/v1/evaluations/{incompleteDraft.Id}/finalize", new { incompleteDraft.ConcurrencyToken });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, incompleteFinalizeRes.StatusCode);
        Assert.Equal(WorkflowErrorCodes.IncompleteScores, await ReadProblemCode(incompleteFinalizeRes));

        // Finish Cold = 8.00
        incompleteDraft = await Body<EvaluationDraftDto>(await evaluator.PutAsJsonAsync(
            $"/api/v1/evaluations/{incompleteDraft.Id}/draft",
            new SaveEvaluationDraftRequest(incompleteDraft.ConcurrencyToken, "Solid cold report",
                [new(s.Criteria[0], 8.0m, null), new(s.Criteria[1], 16.0m, null)])));
        await Body<EvaluationDraftDto>(await evaluator.PostAsJsonAsync(
            $"/api/v1/evaluations/{incompleteDraft.Id}/finalize", new { incompleteDraft.ConcurrencyToken }));

        // TEST-P0-06: Student MUST NOT see Cold score before PUBLISHED; Staff sees 8.00
        var studentColdProgress = await Body<ColdProgressDto>(await student.GetAsync($"/api/v1/projects/{s.ProjectId}/cold/progress"));
        Assert.True(studentColdProgress.Ready);
        Assert.Null(Assert.Single(studentColdProgress.Grading).Score);

        var staffColdProgress = await Body<ColdProgressDto>(await staff.GetAsync($"/api/v1/projects/{s.ProjectId}/cold/progress"));
        Assert.Equal(8.00m, Assert.Single(staffColdProgress.Grading).Score);

        // TEST-P0-06: Student calling result endpoints pre-publish gets 403 RESULT_NOT_PUBLISHED
        var preStudentResult = await student.GetAsync($"/api/v1/projects/{s.ProjectId}/students/{s.Scope.Users.Student}/result");
        Assert.Equal(HttpStatusCode.Forbidden, preStudentResult.StatusCode);
        Assert.Equal("RESULT_NOT_PUBLISHED", await ReadProblemCode(preStudentResult));

        // TEST-P0-07: Batch publish before Defense finalized returns 422 INCOMPLETE_SCORES
        var preBatchPreview = await Body<ProjectResultPreviewDto>(await staff.GetAsync($"/api/v1/projects/{s.ProjectId}/result/preview"));
        var blockedBatchRes = await staff.PostAsJsonAsync($"/api/v1/projects/{s.ProjectId}/results/publish-batch",
            new BatchPublishResultRequest(preBatchPreview.ConfirmationToken));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, blockedBatchRes.StatusCode);
        Assert.Equal(WorkflowErrorCodes.IncompleteScores, await ReadProblemCode(blockedBatchRes));

        // 5. POL-06 Non-Blocking Defense: ZERO DefenseSession in DB (defenseSessionId == null), Defense grading & finalize succeed!
        await using (var verifyNoSession = database.CreateContext())
        {
            Assert.False(await verifyNoSession.Set<DefenseSession>().AnyAsync(ds => ds.ProjectId == s.ProjectId));
        }

        var defenseAssign = await Body<EvaluationAssignmentDto>(await staff.PostAsJsonAsync(
            $"/api/v1/projects/{s.ProjectId}/evaluation-assignments",
            new AssignEvaluatorRequest(s.Scope.Users.Lecturer, s.PeriodId, "COMMITTEE", defenseComp.Scope, null, null, defenseComp.Id)));
        await ScoreAndFinalize(evaluator, s, defenseAssign.Id, 7.00m, "Clear defense demo");

        // 6. Atomic Batch Publish: ProjectResult + StudentResult in 1 transaction
        var readyPreview = await Body<ProjectResultPreviewDto>(await staff.GetAsync($"/api/v1/projects/{s.ProjectId}/result/preview"));
        Assert.True(readyPreview.CanPublish);

        var batch = await Body<BatchPublishResultDto>(await staff.PostAsJsonAsync(
            $"/api/v1/projects/{s.ProjectId}/results/publish-batch",
            new BatchPublishResultRequest(readyPreview.ConfirmationToken)));

        Assert.Equal("PASSED", batch.ProjectResult.Outcome);
        var publishedStudent = Assert.Single(batch.StudentResults);
        // 30% * 8.00 + 50% * 7.00 + 20% * 8.25 = 2.40 + 3.50 + 1.65 = 7.55
        Assert.Equal(7.55m, publishedStudent.TotalScore);
        Assert.Equal("PASSED", publishedStudent.Outcome);
        Assert.Equal("PASSED", publishedStudent.OutcomeDetail);
        Assert.NotNull(publishedStudent.Breakdown?.PublishedFeedback);
        Assert.Contains(publishedStudent.Breakdown!.PublishedFeedback!, f => f.Comments.Contains("Clear defense demo"));
        Assert.Contains(publishedStudent.Breakdown!.PublishedFeedback!, f => f.Comments.Contains("Good progress at G3"));

        // Student can now view published result + comments
        var studentRead = await Body<StudentResultDto>(await student.GetAsync(
            $"/api/v1/projects/{s.ProjectId}/students/{s.Scope.Users.Student}/result"));
        Assert.Equal(7.55m, studentRead.TotalScore);
        Assert.Equal("PASSED", studentRead.OutcomeDetail);
        Assert.Equal("{}", studentRead.SnapshotJson);
    }

    [Fact]
    public async Task Failing_component_floor_below_4_defense_absent_and_archive_open_appeal_guard()
    {
        using var app = new EvaluationFactory(database);
        var (s, majorId, primaryId, gateIds, _) = await SeedPol01Project(app, database);

        using var staff = app.CreateAuthenticatedClient(s.Scope.Users.Staff, roles: ["DEPARTMENT_STAFF"]);
        using var primary = app.CreateAuthenticatedClient(primaryId);
        using var evaluator = app.CreateAuthenticatedClient(s.Scope.Users.Lecturer);

        var schemeDraft = await Body<EvaluationSchemeDto>(await staff.PostAsJsonAsync("/api/v1/evaluation-schemes",
            new SaveEvaluationSchemeRequest(
                s.ProjectId, s.PeriodId, "POL-01 Floor Check Scheme", 5.00m,
                [
                    new("Cold", "COMMON", null, s.RubricId, 37.5m, 30m, 1, Stage: "COLD", TargetScope: "PROJECT"),
                    new("Defense", "COMMON", null, s.RubricId, 62.5m, 50m, 1, Stage: "DEFENSE", TargetScope: "PROJECT"),
                    new("GVHD", "INDIVIDUAL", majorId, s.RubricId, 0m, 20m, 1, Stage: "ON_GOING", TargetScope: "STUDENT")
                ],
                Policy: new(false, "APPROVED", "QD-POL01-FLOOR", "POL_SINGLE_30_50_20",
                    AIPMS.Application.Features.Evaluations.Services.VersionedResultCalculator.PolV4Version,
                    FailingComponentThreshold: 4.00m))));
        var scheme = await Body<EvaluationSchemeDto>(await staff.PostAsJsonAsync(
            $"/api/v1/evaluation-schemes/{schemeDraft.Id}/publish", new SchemeTokenRequest(schemeDraft.ConcurrencyToken)));

        // Finalize GVHD 20% = 8.00 across G1-G3
        for (int i = 0; i < 3; i++)
        {
            var d = await Body<GateIndividualProcessScoreItemDto>(await primary.PutAsJsonAsync(
                $"/api/v1/projects/{s.ProjectId}/gates/{gateIds[i]}/individual-process-scores/{s.Scope.Users.Student}",
                new UpsertIndividualProcessScoreRequest(8.00m)));
            await Body<GateIndividualProcessScoreItemDto>(await primary.PostAsJsonAsync(
                $"/api/v1/projects/{s.ProjectId}/gates/{gateIds[i]}/individual-process-scores/{s.Scope.Users.Student}/finalize",
                new FinalizeIndividualProcessScoreRequest(d.ConcurrencyToken)));
        }

        // Cold = 3.50 (< 4.00 floor!), Defense = 8.00 => Student total = 3.5*0.3 + 8.0*0.5 + 8.0*0.2 = 6.65 >= 5.00, MUST fail component floor!
        var coldComp = scheme.Components.Single(c => c.Stage == "COLD");
        var defenseComp = scheme.Components.Single(c => c.Stage == "DEFENSE");
        var coldAssign = await Body<EvaluationAssignmentDto>(await staff.PostAsJsonAsync(
            $"/api/v1/projects/{s.ProjectId}/evaluation-assignments",
            new AssignEvaluatorRequest(s.Scope.Users.Lecturer, s.PeriodId, "COMMITTEE", coldComp.Scope, null, null, coldComp.Id)));
        await ScoreAndFinalize(evaluator, s, coldAssign.Id, 3.50m, "Weak cold report");

        var defenseAssign = await Body<EvaluationAssignmentDto>(await staff.PostAsJsonAsync(
            $"/api/v1/projects/{s.ProjectId}/evaluation-assignments",
            new AssignEvaluatorRequest(s.Scope.Users.Lecturer, s.PeriodId, "COMMITTEE", defenseComp.Scope, null, null, defenseComp.Id)));
        await ScoreAndFinalize(evaluator, s, defenseAssign.Id, 8.00m, "Strong live defense");

        var floorPreview = await Body<ProjectResultPreviewDto>(await staff.GetAsync(
            $"/api/v1/projects/{s.ProjectId}/students/{s.Scope.Users.Student}/result/preview"));
        Assert.Equal(6.65m, floorPreview.TotalScore);
        Assert.Equal("FAILED", floorPreview.Outcome);
        Assert.Equal("FAILED_COMPONENT_FLOOR", floorPreview.OutcomeDetail);

        // Now record explicit DEFENSE_ABSENT -> Defense becomes 0.00 -> Total = 3.5*0.3 + 0*0.5 + 8*0.2 = 2.65, OutcomeDetail = DEFENSE_ABSENT
        var absence = await Body<StudentDefenseAbsenceDto>(await staff.PostAsJsonAsync(
            $"/api/v1/projects/{s.ProjectId}/students/{s.Scope.Users.Student}/defense-absence",
            new RecordDefenseAbsenceRequest("Student did not attend final defense", "BB-2026-01")));
        Assert.Equal("BB-2026-01", absence.MinutesReference);

        var absentPreview = await Body<ProjectResultPreviewDto>(await staff.GetAsync(
            $"/api/v1/projects/{s.ProjectId}/students/{s.Scope.Users.Student}/result/preview"));
        Assert.Equal(2.65m, absentPreview.TotalScore);
        Assert.Equal("FAILED", absentPreview.Outcome);
        Assert.Equal("DEFENSE_ABSENT", absentPreview.OutcomeDetail);

        // Publish batch -> Project becomes COMPLETED
        var projPreview = await Body<ProjectResultPreviewDto>(await staff.GetAsync($"/api/v1/projects/{s.ProjectId}/result/preview"));
        await Body<BatchPublishResultDto>(await staff.PostAsJsonAsync(
            $"/api/v1/projects/{s.ProjectId}/results/publish-batch",
            new BatchPublishResultRequest(projPreview.ConfirmationToken)));

        // Seed an open ResultAppealRequest -> ArchiveProject MUST fail with 409 ARCHIVE_NOT_ALLOWED
        var projectDto = await Body<AIPMS.Application.Features.Projects.DTOs.ProjectDto>(
            await staff.GetAsync($"/api/v1/projects/{s.ProjectId}"));
        Assert.Equal("COMPLETED", projectDto.Status);
        var projectToken = projectDto.ConcurrencyToken;
        long appealId;
        await using (var db = database.CreateContext())
        {
            var appealPolicy = new ResultAppealPolicy
            {
                ProjectId = s.ProjectId,
                Version = 1,
                ApprovalReference = "QD-APPEAL-2026",
                RecordedBy = s.Scope.Users.Staff,
                RecordedAt = Now
            };
            db.Set<ResultAppealPolicy>().Add(appealPolicy);
            await db.SaveChangesAsync();

            var appeal = new ResultAppealRequest
            {
                ProjectId = s.ProjectId,
                PolicyId = appealPolicy.Id,
                Source = "STAFF_CORRECTION",
                TargetKey = s.Scope.Users.Student,
                StudentId = s.Scope.Users.Student,
                ResultVersion = 1,
                ComponentId = coldComp.Id,
                Scope = coldComp.Scope,
                ResponsibleDepartmentId = s.Scope.Users.DepartmentId,
                Status = "PENDING",
                RequestedBy = s.Scope.Users.Staff,
                RequestedAt = Now,
                Reason = "Re-check cold score",
                BaseStamp = "stamp",
                FrozenInputs = "{}",
                ConcurrencyToken = Guid.NewGuid()
            };
            db.Set<ResultAppealRequest>().Add(appeal);
            await db.SaveChangesAsync();
            appealId = appeal.Id;
        }

        var archiveBlocked = await staff.PostAsJsonAsync($"/api/v1/projects/{s.ProjectId}/archive",
            new { concurrencyToken = projectToken, reason = "Closing semester" });
        Assert.Equal(HttpStatusCode.Conflict, archiveBlocked.StatusCode);
        Assert.Equal(WorkflowErrorCodes.ArchiveNotAllowed, await ReadProblemCode(archiveBlocked));

        // Close appeal -> ArchiveProject succeeds
        await using (var db = database.CreateContext())
        {
            var appeal = await db.Set<ResultAppealRequest>().SingleAsync(a => a.Id == appealId);
            appeal.Status = "REJECTED";
            await db.SaveChangesAsync();
        }
        var archiveOk = await staff.PostAsJsonAsync($"/api/v1/projects/{s.ProjectId}/archive",
            new { concurrencyToken = projectToken, reason = "Appeals resolved" });
        Assert.Equal(HttpStatusCode.OK, archiveOk.StatusCode);
    }
}
