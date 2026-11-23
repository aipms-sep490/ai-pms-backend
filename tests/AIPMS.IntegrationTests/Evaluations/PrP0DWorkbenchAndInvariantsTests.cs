using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Evaluations.DTOs;
using AIPMS.Application.Features.Evaluations.Models;
using AIPMS.Application.Features.FinalSubmissions.DTOs;
using AIPMS.Application.Features.Sprints;
using AIPMS.Application.Features.Teams.Commands;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.IntegrationTests.Evaluations;

public sealed class PrP0DWorkbenchAndInvariantsTests(EvaluationDraftDatabaseFixture database)
    : IClassFixture<EvaluationDraftDatabaseFixture>
{
    private static DateTime Now => EvaluationDraftDatabaseFixture.Now;

    private static async Task<T> Body<T>(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {text}");
        return JsonSerializer.Deserialize<T>(text, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }

    private static async Task<string?> ReadProblemCode(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(text);
        if (doc.RootElement.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.String) return c.GetString();
        if (doc.RootElement.TryGetProperty("errorCode", out var ec) && ec.ValueKind == JsonValueKind.String) return ec.GetString();
        return null;
    }

    [Fact]
    public async Task Scoring_workbench_and_atomic_batch_finalize_enforce_permissions_and_completeness()
    {
        var s = await database.Seed();
        long majorId;
        long assign1Id, assign2Id;

        await using (var db = database.CreateContext())
        {
            var student = await db.Users.SingleAsync(u => u.Id == s.Scope.Users.Student);
            student.StudentCode = "SE260001";
            majorId = student.MajorId!.Value;

            var tpl = new ReviewGateTemplate
            {
                Code = "TPL-WB-" + Guid.NewGuid().ToString("N")[..6],
                Name = "Workbench Template",
                Mode = "SINGLE_MAJOR",
                Major = "IT",
                Status = "PUBLISHED",
                Version = 1,
                CreatedBy = s.Scope.Users.Staff,
                CreatedAt = Now,
                UpdatedAt = Now
            };
            db.ReviewGateTemplates.Add(tpl);
            await db.SaveChangesAsync();

            var g1 = new ProjectGateInstance
            {
                ProjectId = s.ProjectId,
                TemplateId = tpl.Id,
                TemplateVersion = 1,
                Code = "G1",
                Sequence = 1,
                Title = "Gate 1",
                Objective = "Obj",
                Status = "PASSED",
                ReviewEligibility = "READY",
                Required = true,
                BlockNextGate = true,
                CreatedAt = Now
            };
            db.ProjectGateInstances.Add(g1);
            await db.SaveChangesAsync();

            var supAssignId = await db.SupervisorAssignments
                .Where(a => a.ProjectId == s.ProjectId && a.IsPrimary && a.EndedAt == null)
                .Select(a => a.Id)
                .FirstAsync();

            db.Set<GateIndividualProcessScore>().Add(new GateIndividualProcessScore
            {
                ProjectId = s.ProjectId,
                GateInstanceId = g1.Id,
                StudentId = student.Id,
                SupervisorAssignmentId = supAssignId,
                Score = 8.50m,
                GateWeightPercent = 100.00m,
                Status = "FINALIZED",
                FinalizedAt = Now,
                CreatedAt = Now,
                UpdatedAt = Now
            });

            // Confirmed committee with NO DefenseSession (POL-06 non-blocking defense!)
            db.Set<DefenseCommittee>().Add(new DefenseCommittee
            {
                ProjectId = s.ProjectId,
                Name = "Workbench Confirmed Committee",
                Status = "CONFIRMED",
                CreatedBy = s.Scope.Users.Staff,
                CreatedAt = Now,
                Members =
                [
                    new() { UserId = s.Scope.Users.NewLecturer, MajorId = majorId },
                    new() { UserId = s.Scope.Users.Lecturer, MajorId = majorId }
                ]
            });
            await db.SaveChangesAsync();
        }

        using var app = new EvaluationFactory(database);
        using var evaluator = app.CreateAuthenticatedClient(s.Scope.Users.NewLecturer, roles: ["LECTURER"]);
        using var studentClient = app.CreateAuthenticatedClient(s.Scope.Users.Student, roles: ["STUDENT"]);
        using var staff = app.CreateAuthenticatedClient(s.Scope.Users.Staff, roles: ["DEPARTMENT_STAFF"]);

        var scheme = await Body<EvaluationSchemeDto>(await staff.PostAsJsonAsync("/api/v1/evaluation-schemes",
            new SaveEvaluationSchemeRequest(
                s.ProjectId, s.PeriodId, "Workbench Scheme", 5.00m,
                [
                    new SchemeComponentInput("Project Common", "COMMON", null, s.RubricId, 100m, 50m, 1, null, "ON_GOING"),
                    new SchemeComponentInput("Student Individual", "INDIVIDUAL", majorId, s.RubricId, 0m, 50m, 1, null, "ON_GOING")
                ],
                null, null)));
        scheme = await Body<EvaluationSchemeDto>(await staff.PostAsJsonAsync($"/api/v1/evaluation-schemes/{scheme.Id}/publish",
            new SchemeTokenRequest(scheme.ConcurrencyToken)));
        var compCommon = scheme.Components.Single(c => c.Scope == "COMMON");
        var compIndiv = scheme.Components.Single(c => c.Scope == "INDIVIDUAL");

        var a1 = await Body<EvaluationAssignmentDto>(await staff.PostAsJsonAsync($"/api/v1/projects/{s.ProjectId}/evaluation-assignments",
            new AssignEvaluatorRequest(s.Scope.Users.NewLecturer, s.PeriodId, "LECTURER", "COMMON", null, null, compCommon.Id)));
        var a2 = await Body<EvaluationAssignmentDto>(await staff.PostAsJsonAsync($"/api/v1/projects/{s.ProjectId}/evaluation-assignments",
            new AssignEvaluatorRequest(s.Scope.Users.NewLecturer, s.PeriodId, "LECTURER", "INDIVIDUAL", majorId, s.Scope.Users.Student, compIndiv.Id)));
        assign1Id = a1.Id;
        assign2Id = a2.Id;

        // Negative: Student blocked from workbench (403)
        var studentRes = await studentClient.GetAsync($"/api/v1/projects/{s.ProjectId}/scoring-workbench");
        Assert.Equal(HttpStatusCode.Forbidden, studentRes.StatusCode);

        // Positive: Evaluator reads workbench -> sees MSSV, 20% GVHD read-only (8.50), and assignments
        var wb = await Body<ProjectScoringWorkbenchDto>(await evaluator.GetAsync($"/api/v1/projects/{s.ProjectId}/scoring-workbench"));
        Assert.Equal(4.00m, wb.FailingComponentThreshold);
        var member = Assert.Single(wb.Roster, r => r.StudentId == s.Scope.Users.Student);
        Assert.Equal("SE260001", member.StudentCode);
        Assert.Equal(8.50m, member.IndividualProcessScore);
        Assert.True(member.IndividualProcessComplete);
        Assert.Contains(wb.ProjectLevelAssignments, a => a.AssignmentId == assign1Id);
        Assert.Contains(member.Assignments, a => a.AssignmentId == assign2Id);

        // Create both drafts; score draft1 completely, leave draft2 incomplete -> Batch Finalize MUST return 422 INCOMPLETE_SCORES atomically
        var d1 = await Body<EvaluationDraftDto>(await evaluator.PostAsync($"/api/v1/evaluation-assignments/{assign1Id}/evaluation", null));
        var d2 = await Body<EvaluationDraftDto>(await evaluator.PostAsync($"/api/v1/evaluation-assignments/{assign2Id}/evaluation", null));

        var fullScores = d1.Scores.Select(c => new EvaluationScoreInput(c.RubricCriterionId, 8.25m, "Good")).ToArray();
        d1 = await Body<EvaluationDraftDto>(await evaluator.PutAsJsonAsync($"/api/v1/evaluations/{d1.Id}/draft",
            new SaveEvaluationDraftRequest(d1.ConcurrencyToken, "Ready", fullScores)));

        var partialBatchRes = await evaluator.PostAsJsonAsync($"/api/v1/projects/{s.ProjectId}/evaluations/batch-finalize",
            new BatchFinalizeEvaluationsRequest([new(d1.Id, d1.ConcurrencyToken), new(d2.Id, d2.ConcurrencyToken)]));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, partialBatchRes.StatusCode);
        Assert.Equal(WorkflowErrorCodes.IncompleteScores, await ReadProblemCode(partialBatchRes));

        // Verify d1 was NOT finalized (atomic rollback!)
        var d1Check = await Body<EvaluationDraftDto>(await evaluator.GetAsync($"/api/v1/evaluations/{d1.Id}"));
        Assert.Equal("DRAFT", d1Check.Status);

        // Complete d2 and batch finalize -> 200 OK, BOTH finalized
        d2 = await Body<EvaluationDraftDto>(await evaluator.PutAsJsonAsync($"/api/v1/evaluations/{d2.Id}/draft",
            new SaveEvaluationDraftRequest(d2.ConcurrencyToken, "Ready 2", fullScores)));
        var batchOk = await Body<BatchFinalizeEvaluationsResponse>(await evaluator.PostAsJsonAsync(
            $"/api/v1/projects/{s.ProjectId}/evaluations/batch-finalize",
            new BatchFinalizeEvaluationsRequest([new(d1.Id, d1.ConcurrencyToken), new(d2.Id, d2.ConcurrencyToken)])));
        Assert.Equal(2, batchOk.FinalizedCount);
        Assert.All(batchOk.Evaluations, e => Assert.Equal("FINALIZED", e.Status));

        // Revoke all assignments for NewLecturer -> workbench returns 403 ASSIGNMENT_REVOKED
        await using (var db = database.CreateContext())
        {
            var rows = await db.Set<EvaluationAssignment>()
                .Where(a => a.ProjectId == s.ProjectId && a.EvaluatorId == s.Scope.Users.NewLecturer)
                .ToListAsync();
            foreach (var r in rows)
            {
                r.Status = "REVOKED";
                r.RevokedAt = Now;
                r.RevocationReason = "Revoked for test";
            }
            await db.SaveChangesAsync();
        }
        var revokedRes = await evaluator.GetAsync($"/api/v1/projects/{s.ProjectId}/scoring-workbench");
        Assert.Equal(HttpStatusCode.Forbidden, revokedRes.StatusCode);
        Assert.Equal(WorkflowErrorCodes.AssignmentRevoked, await ReadProblemCode(revokedRes));
    }

    [Fact]
    public async Task Hard_invariants_D18_N1_StaleConcurrencyToken_and_Gate3NotPassed_return_canonical_409_codes()
    {
        var s = await database.Seed(false);
        long sprintId;

        // 1. D18: Project in DRAFT => creating a sprint returns 409 PROJECT_NOT_ACTIVE
        await using (var db = database.CreateContext())
        {
            var proj = await db.Projects.SingleAsync(p => p.Id == s.ProjectId);
            proj.Status = "DRAFT";
            await db.SaveChangesAsync();
        }
        using var app = new EvaluationFactory(database);
        using var leader = app.CreateAuthenticatedClient(s.Scope.Users.Student, roles: ["STUDENT"]);

        var d18Res = await leader.PostAsJsonAsync($"/api/v1/projects/{s.ProjectId}/sprints",
            new SaveSprintRequest("Sprint 1", new DateTimeOffset(Now.AddDays(1), TimeSpan.Zero), new DateTimeOffset(Now.AddDays(14), TimeSpan.Zero), null));
        Assert.Equal(HttpStatusCode.Conflict, d18Res.StatusCode);
        Assert.Equal(WorkflowErrorCodes.ProjectNotActive, await ReadProblemCode(d18Res));

        // 2. Activate project + create Sprint => updating with stale token returns 409 STALE_CONCURRENCY_TOKEN
        await using (var db = database.CreateContext())
        {
            var proj = await db.Projects.SingleAsync(p => p.Id == s.ProjectId);
            proj.Status = "ACTIVE";
            await db.SaveChangesAsync();
        }
        var createdSprint = await Body<SprintDto>(await leader.PostAsJsonAsync($"/api/v1/projects/{s.ProjectId}/sprints",
            new SaveSprintRequest("Sprint 1", new DateTimeOffset(Now.AddDays(1), TimeSpan.Zero), new DateTimeOffset(Now.AddDays(14), TimeSpan.Zero), null)));
        sprintId = createdSprint.Id;

        var staleSprintRes = await leader.PutAsJsonAsync($"/api/v1/projects/{s.ProjectId}/sprints/{sprintId}",
            new SaveSprintRequest("Sprint 1 Updated", new DateTimeOffset(Now.AddDays(1), TimeSpan.Zero), new DateTimeOffset(Now.AddDays(14), TimeSpan.Zero), Guid.NewGuid().ToString("N")));
        Assert.Equal(HttpStatusCode.Conflict, staleSprintRes.StatusCode);
        Assert.Equal(WorkflowErrorCodes.StaleConcurrencyToken, await ReadProblemCode(staleSprintRes));

        // 3. GATE_3_NOT_PASSED: Seed an unpassed Gate 3 => final submission checklist & submit report GATE_3_NOT_PASSED
        await using (var db = database.CreateContext())
        {
            var tpl = new ReviewGateTemplate
            {
                Code = "TPL-G3-" + Guid.NewGuid().ToString("N")[..6],
                Name = "G3 Template",
                Mode = "SINGLE_MAJOR",
                Major = "IT",
                Status = "PUBLISHED",
                Version = 1,
                CreatedBy = s.Scope.Users.Staff,
                CreatedAt = Now,
                UpdatedAt = Now
            };
            db.ReviewGateTemplates.Add(tpl);
            await db.SaveChangesAsync();

            db.ProjectGateInstances.Add(new ProjectGateInstance
            {
                ProjectId = s.ProjectId,
                TemplateId = tpl.Id,
                TemplateVersion = 1,
                Code = "G3",
                Sequence = 3,
                Title = "Gate 3",
                Objective = "Pre-defense readiness",
                Status = "OPEN",
                ReviewEligibility = "READY",
                Required = true,
                BlockNextGate = true,
                BlocksDefense = true,
                CreatedAt = Now
            });
            await db.SaveChangesAsync();
        }

        var checklist = await Body<FinalSubmissionChecklistDto>(await leader.GetAsync($"/api/v1/projects/{s.ProjectId}/final-submission/checklist"));
        Assert.False(checklist.CanSubmit);
        Assert.Contains(WorkflowErrorCodes.Gate3NotPassed, checklist.Blockers);

        var submitGate3Blocked = await leader.PostAsJsonAsync($"/api/v1/projects/{s.ProjectId}/final-submission",
            new SubmitFinalSubmissionRequest(Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N")));
        Assert.Equal(HttpStatusCode.Conflict, submitGate3Blocked.StatusCode);
        Assert.Equal(WorkflowErrorCodes.Gate3NotPassed, await ReadProblemCode(submitGate3Blocked));

        // 4. N1 (409 REGISTRATION_WINDOW_CLOSED): Expire registration period at exact endAt == Now (UTC)
        long expiredStudentId;
        await using (var db = database.CreateContext())
        {
            var regPeriods = await db.ProjectPeriods
                .Where(p => p.AcademicSemesterId == s.Scope.SemesterId && p.PeriodType == "REGISTRATION")
                .ToListAsync();
            if (regPeriods.Count == 0)
            {
                db.ProjectPeriods.Add(new M.ProjectPeriod
                {
                    AcademicSemesterId = s.Scope.SemesterId,
                    Code = "REG-EXP-" + Guid.NewGuid().ToString("N")[..4],
                    Name = "Expired Registration",
                    PeriodType = "REGISTRATION",
                    Status = "ACTIVE",
                    StartAt = Now.AddDays(-7),
                    EndAt = Now,
                    MinTeamSize = 3,
                    MaxTeamSize = 5,
                    MinDistinctMajors = 1,
                    CreatedAt = Now.AddDays(-7),
                    UpdatedAt = Now.AddDays(-7)
                });
            }
            else
            {
                foreach (var rp in regPeriods)
                {
                    rp.StartAt = Now.AddDays(-7);
                    rp.EndAt = Now;
                    rp.Status = "ACTIVE";
                }
            }

            var studentMajor = await db.Users.Where(u => u.Id == s.Scope.Users.Student)
                .Select(u => new { u.DepartmentId, u.MajorId }).SingleAsync();
            var roleId = await db.Roles.Where(r => r.Code == "STUDENT").Select(r => r.Id).SingleAsync();
            var freeStudent = new M.User
            {
                Email = $"n1_{Guid.NewGuid():N}@e2e.invalid",
                FullName = "N1 Boundary Student",
                PasswordHash = "unused",
                Status = "ACTIVE",
                AcademicProfileStatus = "VERIFIED",
                DepartmentId = studentMajor.DepartmentId,
                MajorId = studentMajor.MajorId,
                UserRoleUsers = [new() { RoleId = roleId }]
            };
            db.Users.Add(freeStudent);
            await db.SaveChangesAsync();
            expiredStudentId = freeStudent.Id;
        }

        using var freeClient = app.CreateAuthenticatedClient(expiredStudentId, roles: ["STUDENT"]);
        var n1Res = await freeClient.PostAsJsonAsync("/api/v1/teams",
            new CreateTeamCommand(s.Scope.SemesterId, "N1-" + Guid.NewGuid().ToString("N")[..4], "Late Team", "Past deadline"));
        Assert.Equal(HttpStatusCode.Conflict, n1Res.StatusCode);
        Assert.Equal(WorkflowErrorCodes.RegistrationWindowClosed, await ReadProblemCode(n1Res));
    }
}
