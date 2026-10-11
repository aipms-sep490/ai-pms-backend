using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AIPMS.Application.Features.Deliverables.DTOs;
using AIPMS.Application.Features.Evaluations.DTOs;
using AIPMS.Application.Features.FinalSubmissions.DTOs;
using AIPMS.Application.Features.FinalSubmissions.Models;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.IntegrationTests.FinalSubmissions;

public sealed partial class FinalSubmissionEndpointTests
{
    [Theory]
    [InlineData("MAJOR_SPECIFIC")]
    [InlineData("INDIVIDUAL")]
    public async Task Explicit_target_survives_upload_lock_readback_and_revocation(string targetScope)
    {
        using var app = new Factory(database);
        var s = await database.Seed();
        await SeedEvaluationRoster(s, s.PeriodId);
        using var leader = app.CreateAuthenticatedClient(s.Users.Student);
        using var member = app.CreateAuthenticatedClient(s.MemberId);
        using var staff = app.CreateAuthenticatedClient(s.Users.Staff);
        using var evaluator = app.CreateAuthenticatedClient(s.Users.NewLecturer);
        using var outsider = app.CreateAuthenticatedClient(s.Users.OtherLecturer);
        long major, rubricId, periodId;
        await using (var db = database.CreateContext())
        {
            major = await db.ProjectMajors.Where(m => m.ProjectId == s.ProjectId).Select(m => m.MajorId).SingleAsync();
            var rubric = new M.Rubric { DepartmentId = s.Users.DepartmentId, AcademicSemesterId = s.SemesterId,
                Code = Guid.NewGuid().ToString("N"), Name = "Evidence rubric", IsActive = true, CreatedBy = s.Users.Staff,
                RubricCriteria = [new() { Criterion = new() { Code = Guid.NewGuid().ToString("N"), Name = "Quality", IsActive = true },
                    WeightPercent = 100, MaxScore = 10, IsRequired = true }] };
            var period = new M.ProjectPeriod { AcademicSemesterId = s.SemesterId, Code = "EVAL", Name = "Evaluation",
                PeriodType = "EVALUATION", Status = "ACTIVE", StartAt = Now.AddHours(-1), EndAt = Now.AddDays(1), Rubric = rubric };
            db.Add(period);
            await db.SaveChangesAsync();
            db.Add(new RubricVersion { RubricId = rubric.Id, RootRubricId = rubric.Id, VersionNumber = 1,
                Status = "PUBLISHED", ConcurrencyToken = Guid.NewGuid() });
            await db.SaveChangesAsync();
            rubricId = rubric.Id; periodId = period.Id;
        }
        var input = new SaveDeliverableRequest(null, "Targeted report", null, "REPORT", Now.AddDays(1), targetScope,
            major, targetScope == "INDIVIDUAL" ? s.Users.Student : null);
        var createUrl = $"/api/v1/projects/{s.ProjectId}/deliverables";
        Assert.Equal(HttpStatusCode.Conflict, (await leader.PostAsJsonAsync(createUrl, input with { TargetMajorId = long.MaxValue })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await leader.PostAsJsonAsync(createUrl, input with { EvidenceScope = "COMMON" })).StatusCode);
        var item = await Body<DeliverableDto>(await leader.PostAsJsonAsync(createUrl, input));
        if (targetScope == "INDIVIDUAL")
        {
            using var form = new MultipartFormDataContent();
            var wrongFile = new ByteArrayContent("Wrong submitter"u8.ToArray());
            wrongFile.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/plain");
            form.Add(wrongFile, "file", "wrong.txt");
            form.Add(new StringContent("0"), "expectedLatestVersion");
            Assert.Equal(HttpStatusCode.Forbidden, (await member.PostAsync($"/api/v1/deliverables/{item.Id}/versions", form)).StatusCode);
        }
        var version = await Upload(leader, item.Id, 0, "Exact locked evidence");
        Assert.Equal(HttpStatusCode.Conflict, (await leader.PutAsJsonAsync($"/api/v1/deliverables/{item.Id}",
            input with { EvidenceScope = "COMMON", TargetMajorId = null, TargetStudentId = null })).StatusCode);
        var draft = await Body<FinalSubmissionDraftDto>(await leader.PostAsJsonAsync(DraftRoute(s.ProjectId),
            new CreateFinalSubmissionDraftRequest(s.PeriodId, null, [version.Id])));
        var requirements = await Body<FinalRequirementsDto>(await staff.PutAsJsonAsync(Route(s.ProjectId) + "/requirements",
            new ConfigureFinalRequirementsRequest([item.Id], null)));
        var package = await Body<FinalSubmissionDto>(await leader.PostAsJsonAsync(Route(s.ProjectId),
            new SubmitFinalSubmissionRequest(draft.ConcurrencyToken, requirements.ConcurrencyToken!)));
        var schemeDraft = await Body<EvaluationSchemeDto>(await staff.PostAsJsonAsync("/api/v1/evaluation-schemes",
            new SaveEvaluationSchemeRequest(s.ProjectId, periodId, "Targeted evidence", 5,
                [new("Common", "COMMON", null, rubricId, 50, 50, 1),
                 new("Major", "MAJOR_SPECIFIC", major, rubricId, 50, 30, 1),
                 new("Individual", "INDIVIDUAL", major, rubricId, 0, 20, 1)])));
        var scheme = await Body<EvaluationSchemeDto>(await staff.PostAsJsonAsync($"/api/v1/evaluation-schemes/{schemeDraft.Id}/publish",
            new SchemeTokenRequest(schemeDraft.ConcurrencyToken)));
        var assignment = await Body<EvaluationAssignmentDto>(await staff.PostAsJsonAsync($"/api/v1/projects/{s.ProjectId}/evaluation-assignments",
            new AssignEvaluatorRequest(s.Users.NewLecturer, periodId, "LECTURER", targetScope, major,
                input.TargetStudentId, scheme.Components.Single(c => c.Scope == targetScope).Id)));
        var evidenceUrl = $"/api/v1/evaluation-assignments/{assignment.Id}/evidence";
        var evidence = await Body<EvaluationAssignmentEvidenceDto>(await evaluator.GetAsync(evidenceUrl));
        Assert.Equal(package.Id, evidence.FinalSubmissionId);
        var file = Assert.Single(evidence.Items);
        Assert.Equal(major, file.MajorId);
        Assert.Equal(input.TargetStudentId, file.StudentId);
        Assert.Equal("Exact locked evidence", await (await evaluator.GetAsync(file.DownloadUrl)).Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Forbidden, (await evaluator.GetAsync(Route(s.ProjectId))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await evaluator.GetAsync(Route(s.ProjectId) + $"/files/{file.FileId}/download")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await outsider.GetAsync(file.DownloadUrl)).StatusCode);
        await using (var db = database.CreateContext())
        {
            var snapshot = await db.Set<FinalSubmissionItem>().SingleAsync(i => i.SubmissionId == package.Id);
            var frozen = Assert.Single(JsonSerializer.Deserialize<FinalSnapshotFile[]>(snapshot.FilesJson)!);
            Assert.Equal(targetScope, frozen.EvidenceScope);
            Assert.Equal(major, frozen.TargetMajorId);
            Assert.Equal(input.TargetStudentId, frozen.TargetStudentId);
            var live = (await db.Files.FindAsync(file.FileId))!;
            live.StoragePath = "replacement-object";
            live.OriginalFileName = "replacement.txt";
            await db.SaveChangesAsync();
            Assert.True(await db.AuditLogs.AnyAsync(a => a.Action == "FINAL_SUBMISSION_LOCKED" && a.EntityId == package.Id.ToString()));
        }
        Assert.Equal("Exact locked evidence", await (await evaluator.GetAsync(file.DownloadUrl)).Content.ReadAsStringAsync());
        await Body<EvaluationAssignmentDto>(await staff.PostAsJsonAsync($"/api/v1/evaluation-assignments/{assignment.Id}/revoke",
            new RevokeEvaluatorRequest(assignment.ConcurrencyToken, "End evidence access")));
        Assert.Equal(HttpStatusCode.Forbidden, (await evaluator.GetAsync(file.DownloadUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync(file.DownloadUrl)).StatusCode);
    }
}
