using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AIPMS.Application.Features.Evaluations.DTOs;
using AIPMS.Application.Features.Evaluations.Models;
using AIPMS.Application.Features.Projects.DTOs;
using AIPMS.Application.Features.Results.DTOs;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.IntegrationTests.Evaluations;

public sealed partial class PolicyEvaluationEndpointTests
{
    [Fact]
    public async Task V5_E_three_major_demo_golden_scores_use_real_save_finalize_preview_and_db_readback()
    {
        var (s, firstMajor) = await Seed();
        var students = new List<long> { s.Scope.Users.Student }; var majors = new List<long> { firstMajor };
        var rubrics = new List<long> { s.RubricId }; var criteria = new List<long[]> { s.Criteria };
        await using (var db = database.CreateContext())
        {
            // Synthetic locked-package inputs only. Scheme, assignments, scores and previews below use real APIs.
            var department = s.Scope.Users.DepartmentId;
            (await db.Majors.FindAsync(firstMajor))!.Name = "IT";
            foreach (var name in new[] { "Marketing", "Design" })
            {
                var major = new M.Major { DepartmentId = department, Code = Guid.NewGuid().ToString("N"), Name = name, IsActive = true };
                var student = new M.User { DepartmentId = department, Major = major, Email = Guid.NewGuid().ToString("N") + "@v5.invalid", FullName = name + " demo student",
                    PasswordHash = "unused", Status = "ACTIVE", AcademicProfileStatus = "VERIFIED", UserRoleUsers = [new() { RoleId = await db.Roles.Where(r => r.Code == "STUDENT").Select(r => r.Id).SingleAsync() }] };
                var rubric = new M.Rubric { Code = Guid.NewGuid().ToString("N"), Name = name + " demo", DepartmentId = department, AcademicSemesterId = s.Scope.SemesterId,
                    IsActive = true, CreatedBy = s.Scope.Users.Staff, RubricCriteria = [new() { Criterion = new() { Code = Guid.NewGuid().ToString("N"), Name = "Demo quality", IsActive = true }, WeightPercent = 100, MaxScore = 10, IsRequired = true }] };
                db.AddRange(student, rubric); db.ProjectMajors.Add(new() { ProjectId = s.ProjectId, Major = major }); await db.SaveChangesAsync();
                db.Add(new RubricVersion { RubricId = rubric.Id, RootRubricId = rubric.Id, VersionNumber = 1, Status = "PUBLISHED", ConcurrencyToken = Guid.NewGuid() });
                db.TeamMembers.Add(new() { TeamId = (await db.Projects.FindAsync(s.ProjectId))!.TeamId, AcademicSemesterId = s.Scope.SemesterId, UserId = student.Id });
                majors.Add(major.Id); students.Add(student.Id); rubrics.Add(rubric.Id); criteria.Add([rubric.RubricCriteria.Single().Id]);
            }
            var snapshot = await db.Set<ProjectRegistrationSnapshot>().SingleAsync(p => p.ProjectId == s.ProjectId);
            var evidence = JsonSerializer.Deserialize<RegistrationEvidence>(snapshot.SnapshotJson)!;
            snapshot.SnapshotJson = JsonSerializer.Serialize(evidence with { Scope = evidence.Scope with { ProjectMode = "INTERDISCIPLINARY", PrimaryMajorId = null,
                Requirements = majors.Select(m => new AIPMS.Application.Features.Teams.DTOs.MajorRequirementDto(m, 1, 5, "Demo artifacts")).ToArray() },
                Members = students.Select((id, i) => new RegisteredMemberDto(id, "Demo " + i, majors[i], i == 0)).ToArray(),
                MajorDepartmentIds = majors.ToDictionary(m => m, _ => department) });
            await db.SaveChangesAsync();
        }
        var evidenceLog = new ApiAcceptanceEvidence("E-demo-golden");
        using var app = new EvaluationFactory(database) { AcceptanceEvidence = evidenceLog }; using var staff = app.CreateAuthenticatedClient(s.Scope.Users.Staff); using var evaluator = app.CreateAuthenticatedClient(s.Scope.Users.Lecturer);
        var components = new List<SchemeComponentInput> { new("Common", "COMMON", null, s.RubricId, 50, 50, 1) };
        for (var i = 0; i < 3; i++) { components.Add(new("Major " + i, "MAJOR_SPECIFIC", majors[i], rubrics[i], i == 0 ? 16.6666m : 16.6667m, 30, 1));
            components.Add(new("Individual " + i, "INDIVIDUAL", majors[i], rubrics[i], 0, 20, 1)); }
        var scheme = await Body<EvaluationSchemeDto>(await staff.PostAsJsonAsync("/api/v1/evaluation-schemes", new SaveEvaluationSchemeRequest(s.ProjectId, s.PeriodId, "V5 demo golden", 5, components,
            Policy: new(Formula: "DEMO_50_30_20"))));
        scheme = await Body<EvaluationSchemeDto>(await staff.PostAsJsonAsync($"/api/v1/evaluation-schemes/{scheme.Id}/publish", new SchemeTokenRequest(scheme.ConcurrencyToken)));
        var evaluationIds = new List<long>();
        async Task Score(SchemeComponentDto component, decimal score, long? student = null)
        {
            var a = await Body<EvaluationAssignmentDto>(await staff.PostAsJsonAsync($"/api/v1/projects/{s.ProjectId}/evaluation-assignments",
                new AssignEvaluatorRequest(s.Scope.Users.Lecturer, s.PeriodId, "LECTURER", component.Scope, component.MajorId, student, component.Id)));
            Assert.Equal(1, (await Body<AIPMS.Application.Features.Dashboards.DTOs.ActorDashboardDto>(await evaluator.GetAsync("/api/v1/dashboards/me"))).PendingEvaluations);
            var e = await Body<EvaluationDraftDto>(await evaluator.PostAsync($"/api/v1/evaluation-assignments/{a.Id}/evaluation", null));
            var index = component.Scope == "COMMON" ? 0 : majors.IndexOf(component.MajorId!.Value);
            var scores = criteria[index].Select((id, n) => new EvaluationScoreInput(id, n == 1 ? score * 2 : score, null)).ToArray();
            e = await Body<EvaluationDraftDto>(await evaluator.PutAsJsonAsync($"/api/v1/evaluations/{e.Id}/draft", new SaveEvaluationDraftRequest(e.ConcurrencyToken, "Synthetic demo", scores)));
            await Body<EvaluationDraftDto>(await evaluator.PostAsJsonAsync($"/api/v1/evaluations/{e.Id}/finalize", new { e.ConcurrencyToken })); evaluationIds.Add(e.Id);
            Assert.Equal(0, (await Body<AIPMS.Application.Features.Dashboards.DTOs.ActorDashboardDto>(await evaluator.GetAsync("/api/v1/dashboards/me"))).PendingEvaluations);
        }
        await Score(scheme.Components.Single(c => c.Scope == "COMMON"), 8);
        decimal[] majorScores = [9, 7, 6], individualScores = [7, 9, 8], expected = [8.10m, 7.90m, 7.40m];
        for (var i = 0; i < 3; i++) { await Score(scheme.Components.Single(c => c.Scope == "MAJOR_SPECIFIC" && c.MajorId == majors[i]), majorScores[i]);
            await Score(scheme.Components.Single(c => c.Scope == "INDIVIDUAL" && c.MajorId == majors[i]), individualScores[i], students[i]); }
        for (var i = 0; i < 3; i++)
        {
            var url = $"/api/v1/projects/{s.ProjectId}/students/{students[i]}/result";
            var preview = await Body<ProjectResultPreviewDto>(await staff.GetAsync(url + "/preview")); Assert.Equal(expected[i], preview.TotalScore);
            Assert.False(preview.CanPublish); Assert.Contains("DEMO_SCHEME_NOT_OFFICIAL", preview.Blockers);
            Assert.Equal(HttpStatusCode.Conflict, (await staff.PostAsJsonAsync(url, new PublishProjectResultRequest(preview.ConfirmationToken))).StatusCode);
        }
        var project = await Body<ProjectResultPreviewDto>(await staff.GetAsync($"/api/v1/projects/{s.ProjectId}/result/preview")); Assert.Equal(7.67m, project.TotalScore);
        await using var verify = database.CreateContext(); Assert.Equal(7, await verify.Set<EvaluationFinalization>().CountAsync(f => evaluationIds.Contains(f.EvaluationId)));
        Assert.Equal(7, await verify.AuditLogs.CountAsync(a => a.Action == "EVALUATION_FINALIZED" && evaluationIds.Select(id => id.ToString()).Contains(a.EntityId)));
        Assert.False(await verify.Set<StudentResult>().AnyAsync(r => r.ProjectId == s.ProjectId));
        await evidenceLog.SaveAsync(new { studentScores = expected, projectScore = project.TotalScore, finalizations = 7,
            auditIds = await verify.AuditLogs.Where(a => a.Action == "EVALUATION_FINALIZED" && evaluationIds.Select(id => id.ToString()).Contains(a.EntityId)).Select(a => a.Id).ToArrayAsync(),
            finalizationAudits = 7, studentResults = 0, inputs = "Synthetic locked package; API scheme/assignment/save/finalize/preview" });
    }
}
