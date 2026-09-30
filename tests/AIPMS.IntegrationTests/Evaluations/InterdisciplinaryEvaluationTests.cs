using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AIPMS.Application.Common.Models;
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
    public async Task Two_departments_multiple_evaluators_and_individual_targets_use_frozen_weights_and_visibility()
    {
        var (s, firstMajor) = await Seed();
        long secondMajor, secondStudent, secondRubric, secondCriterion;
        await using (var db = database.CreateContext())
        {
            var department = (await db.Users.FindAsync(s.Scope.Users.OtherLecturer))!.DepartmentId!.Value;
            var major = new M.Major { DepartmentId = department, Code = Guid.NewGuid().ToString("N"), Name = "Business", IsActive = true };
            var student = new M.User { Email = Guid.NewGuid().ToString("N") + "@example.test", FullName = "Business Student",
                PasswordHash = "unused", Status = "ACTIVE", DepartmentId = department, Major = major, AcademicProfileStatus = "VERIFIED",
                UserRoleUsers = [new() { RoleId = await db.Roles.Where(r => r.Code == "STUDENT").Select(r => r.Id).SingleAsync() }] };
            var rubric = new M.Rubric { Code = Guid.NewGuid().ToString("N"), Name = "Business assessment", DepartmentId = department,
                AcademicSemesterId = s.Scope.SemesterId, CreatedBy = s.Scope.Users.OutsideStaff, IsActive = true,
                RubricCriteria = [new() { Criterion = new() { Code = Guid.NewGuid().ToString("N"), Name = "Business outcome", IsActive = true },
                    WeightPercent = 100, MaxScore = 10, IsRequired = true }] };
            db.Users.Add(student); db.Rubrics.Add(rubric); db.ProjectMajors.Add(new() { ProjectId = s.ProjectId, Major = major });
            await db.SaveChangesAsync();
            secondMajor = major.Id; secondStudent = student.Id; secondRubric = rubric.Id; secondCriterion = rubric.RubricCriteria.Single().Id;
            db.Add(new RubricVersion { RubricId = rubric.Id, RootRubricId = rubric.Id, VersionNumber = 1, Status = "PUBLISHED", ConcurrencyToken = Guid.NewGuid() });
            db.TeamMembers.Add(new() { TeamId = (await db.Projects.FindAsync(s.ProjectId))!.TeamId, UserId = student.Id, AcademicSemesterId = s.Scope.SemesterId });
            var snapshot = await db.Set<ProjectRegistrationSnapshot>().SingleAsync(x => x.ProjectId == s.ProjectId);
            var evidence = JsonSerializer.Deserialize<RegistrationEvidence>(snapshot.SnapshotJson)!;
            snapshot.SnapshotJson = JsonSerializer.Serialize(evidence with {
                Scope = evidence.Scope with { ProjectMode = "INTERDISCIPLINARY", PrimaryMajorId = null,
                    Requirements = [.. evidence.Scope.Requirements, new(secondMajor, 1, 5, "Business deliverables")] },
                Members = [.. evidence.Members, new(student.Id, student.FullName, major.Id, false)],
                DepartmentIds = [s.Scope.Users.DepartmentId, department],
                MajorDepartmentIds = new Dictionary<long,long> { [firstMajor] = s.Scope.Users.DepartmentId, [secondMajor] = department }
            });
            await db.SaveChangesAsync();
        }
        using var app = new EvaluationFactory(database);
        using var admin = app.CreateAuthenticatedClient(s.Scope.Users.Admin);
        using var staff = app.CreateAuthenticatedClient(s.Scope.Users.Staff);
        using var otherStaff = app.CreateAuthenticatedClient(s.Scope.Users.OutsideStaff);
        using var studentA = app.CreateAuthenticatedClient(s.Scope.Users.Student);
        using var studentB = app.CreateAuthenticatedClient(secondStudent);
        var input = new SaveEvaluationSchemeRequest(s.ProjectId, s.PeriodId, "Two departments", 5,
            [new("Common", "COMMON", null, s.RubricId, 40, 40, 2),
             new("Software", "MAJOR_SPECIFIC", firstMajor, s.RubricId, 30, 40, 1),
             new("Business", "MAJOR_SPECIFIC", secondMajor, secondRubric, 30, 40, 1),
             new("Student software", "INDIVIDUAL", firstMajor, s.RubricId, 0, 20, 1),
             new("Student business", "INDIVIDUAL", secondMajor, secondRubric, 0, 20, 1)]);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.PostAsJsonAsync("/api/v1/evaluation-schemes", input)).StatusCode);
        var draft = await Body<EvaluationSchemeDto>(await admin.PostAsJsonAsync("/api/v1/evaluation-schemes", input));
        var scheme = await Body<EvaluationSchemeDto>(await admin.PostAsJsonAsync($"/api/v1/evaluation-schemes/{draft.Id}/publish", new SchemeTokenRequest(draft.ConcurrencyToken)));
        var common = scheme.Components.Single(c => c.Scope == "COMMON");
        var candidates = await Body<PagedResult<EligibleEvaluatorDto>>(await staff.GetAsync($"/api/v1/projects/{s.ProjectId}/eligible-evaluators?periodId={s.PeriodId}&componentId={common.Id}&scope=COMMON"));
        Assert.Contains(candidates.Items, x => x.UserId == s.Scope.Users.Lecturer);
        var projectUrl = $"/api/v1/projects/{s.ProjectId}/result";
        var studentUrlA = $"/api/v1/projects/{s.ProjectId}/students/{s.Scope.Users.Student}/result";
        var studentUrlB = $"/api/v1/projects/{s.ProjectId}/students/{secondStudent}/result";
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync(projectUrl + "/preview")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await studentA.GetAsync(studentUrlA)).StatusCode);
        async Task Score(SchemeComponentDto component, long evaluator, long? target, decimal score)
        {
            using var lecturer = app.CreateAuthenticatedClient(evaluator);
            var request = new AssignEvaluatorRequest(evaluator, s.PeriodId, "LECTURER", component.Scope, component.MajorId, target, component.Id);
            var assignment = await Body<EvaluationAssignmentDto>(await admin.PostAsJsonAsync($"/api/v1/projects/{s.ProjectId}/evaluation-assignments", request));
            var evaluation = await Body<EvaluationDraftDto>(await lecturer.PostAsync($"/api/v1/evaluation-assignments/{assignment.Id}/evaluation", null));
            EvaluationScoreInput[] scores = component.RubricId == secondRubric ? [new(secondCriterion, score, null)]
                : [new(s.Criteria[0], score, null), new(s.Criteria[1], score * 2, null)];
            evaluation = await Body<EvaluationDraftDto>(await lecturer.PutAsJsonAsync($"/api/v1/evaluations/{evaluation.Id}/draft", new SaveEvaluationDraftRequest(evaluation.ConcurrencyToken, null, scores)));
            await Body<EvaluationDraftDto>(await lecturer.PostAsJsonAsync($"/api/v1/evaluations/{evaluation.Id}/finalize", new { evaluation.ConcurrencyToken }));
        }
        await Score(common, s.Scope.Users.Lecturer, null, 8);
        await Score(common, s.Scope.Users.NewLecturer, null, 10);
        await Score(scheme.Components.Single(c => c.Name == "Software"), s.Scope.Users.Lecturer, null, 6);
        await Score(scheme.Components.Single(c => c.Name == "Business"), s.Scope.Users.OtherLecturer, null, 4);
        await Score(scheme.Components.Single(c => c.Name == "Student software"), s.Scope.Users.Lecturer, s.Scope.Users.Student, 10);
        Assert.False((await Body<ProjectResultPreviewDto>(await admin.GetAsync(projectUrl + "/preview"))).CanPublish);
        await Score(scheme.Components.Single(c => c.Name == "Student business"), s.Scope.Users.OtherLecturer, secondStudent, 2);
        var project = await Body<ProjectResultPreviewDto>(await admin.GetAsync(projectUrl + "/preview"));
        Assert.Equal(6.6m, project.TotalScore);
        var first = await Body<ProjectResultPreviewDto>(await staff.GetAsync(studentUrlA + "/preview"));
        var second = await Body<ProjectResultPreviewDto>(await otherStaff.GetAsync(studentUrlB + "/preview"));
        Assert.Equal(8m, first.TotalScore); Assert.Equal(5.6m, second.TotalScore);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync(studentUrlB + "/preview")).StatusCode);
        await using (var db = database.CreateContext())
        {
            (await db.Users.FindAsync(s.Scope.Users.Student))!.MajorId = secondMajor;
            (await db.Rubrics.FindAsync(s.RubricId))!.Name = "Live name changed";
            await db.SaveChangesAsync();
        }
        Assert.Equal(first.ConfirmationToken, (await Body<ProjectResultPreviewDto>(await staff.GetAsync(studentUrlA + "/preview"))).ConfirmationToken);
        await Body<StudentResultDto>(await staff.PostAsJsonAsync(studentUrlA, new PublishProjectResultRequest(first.ConfirmationToken)));
        await Body<StudentResultDto>(await otherStaff.PostAsJsonAsync(studentUrlB, new PublishProjectResultRequest(second.ConfirmationToken)));
        Assert.Equal(8m, (await Body<StudentResultDto>(await studentA.GetAsync(studentUrlA))).TotalScore);
        Assert.Equal(HttpStatusCode.Forbidden, (await studentA.GetAsync(studentUrlB)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await studentB.GetAsync(studentUrlA)).StatusCode);
        var saved = await Body<ProjectResultDto>(await admin.PostAsJsonAsync(projectUrl, new PublishProjectResultRequest(project.ConfirmationToken)));
        Assert.Equal(4, saved.Contributions.Count);
        await RerunMigration(); await RerunMigration();
        Assert.Equal(5.6m, (await Body<StudentResultDto>(await studentB.GetAsync(studentUrlB))).TotalScore);
    }
}
