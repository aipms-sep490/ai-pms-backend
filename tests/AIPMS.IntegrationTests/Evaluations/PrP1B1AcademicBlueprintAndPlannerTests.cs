using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.AcademicBlueprints;
using AIPMS.Application.Features.Semesters;
using AIPMS.Application.Features.Semesters.DTOs;
using AIPMS.Domain.Teams;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.IntegrationTests.Evaluations;

public sealed class PrP1B1AcademicBlueprintAndPlannerTests(EvaluationDraftDatabaseFixture database)
    : IClassFixture<EvaluationDraftDatabaseFixture>
{
    private static DateTime Now => EvaluationDraftDatabaseFixture.Now;
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private static async Task<T> Body<T>(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {text}");
        return JsonSerializer.Deserialize<T>(text, JsonOpts)!;
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
    public async Task Blueprint_dual_approval_readiness_gate_dry_run_idempotent_apply_and_deadline_change_work()
    {
        var s = await database.Seed(false);
        long leadDeptId, partDeptId, partStaffId, regPeriodId, execPeriodId;

        await using (var db = database.CreateContext())
        {
            leadDeptId = s.Scope.Users.DepartmentId;
            partDeptId = (await db.Users.SingleAsync(u => u.Id == s.Scope.Users.OtherLecturer)).DepartmentId!.Value;
            var staffRoleId = await db.Roles.Where(r => r.Code == "DEPARTMENT_STAFF").Select(r => r.Id).SingleAsync();
            var partStaff = new M.User
            {
                Email = $"bp_part_{Guid.NewGuid():N}@e2e.invalid",
                FullName = "MKT Dept Head",
                PasswordHash = "unused",
                Status = "ACTIVE",
                DepartmentId = partDeptId,
                UserRoleUsers = [new() { RoleId = staffRoleId }]
            };
            db.Users.Add(partStaff);

            var project = await db.Projects.SingleAsync(p => p.Id == s.ProjectId);
            project.Status = "ACTIVE";
            project.TrackCode = AcademicTrackCodes.ItMkt;
            var member = await db.TeamMembers.SingleOrDefaultAsync(m => m.TeamId == project.TeamId && m.UserId == s.Scope.Users.Student);
            if (member is null)
                db.TeamMembers.Add(new M.TeamMember
                {
                    TeamId = project.TeamId,
                    AcademicSemesterId = s.Scope.SemesterId,
                    UserId = s.Scope.Users.Student,
                    IsLeader = true,
                    JoinedAt = Now.AddDays(-20)
                });
            else
                member.IsLeader = true;

            var regPeriod = new M.ProjectPeriod
            {
                AcademicSemesterId = s.Scope.SemesterId,
                Code = "REG_" + Guid.NewGuid().ToString("N")[..6],
                Name = "Registration Window",
                PeriodType = PeriodTypes.Registration,
                Status = "UPCOMING",
                StartAt = Now.AddDays(-10),
                EndAt = Now.AddDays(10)
            };
            var execPeriod = new M.ProjectPeriod
            {
                AcademicSemesterId = s.Scope.SemesterId,
                Code = "EXE_" + Guid.NewGuid().ToString("N")[..6],
                Name = "Execution Window",
                PeriodType = PeriodTypes.Execution,
                Status = "ACTIVE",
                StartAt = Now,
                EndAt = Now.AddDays(120)
            };
            db.ProjectPeriods.AddRange(regPeriod, execPeriod);
            await db.SaveChangesAsync();
            partStaffId = partStaff.Id;
            regPeriodId = regPeriod.Id;
            execPeriodId = execPeriod.Id;

            // Add customized milestone + task matching a template origin to verify Dry-Run Diff preserves user customizations
            var customMs = new M.Milestone
            {
                ProjectId = s.ProjectId,
                Title = "Custom Student Milestone",
                Status = "PLANNED",
                SortOrder = 1,
                TemplateOriginId = "MS-1",
                IsUserCustomized = true,
                CreatedBy = s.Scope.Users.Student,
                CreatedAt = Now,
                UpdatedAt = Now,
                ConcurrencyToken = Guid.NewGuid()
            };
            db.Milestones.Add(customMs);
            await db.SaveChangesAsync();
            db.Tasks.Add(new M.Task
            {
                MilestoneId = customMs.Id,
                Title = "Custom Student Task Kept",
                Status = "TODO",
                Priority = "MEDIUM",
                TemplateOriginId = "TASK-G1-CUSTOM",
                IsUserCustomized = true,
                IsCoreGateTask = false,
                CreatedBy = s.Scope.Users.Student,
                CreatedAt = Now,
                UpdatedAt = Now,
                ConcurrencyToken = Guid.NewGuid()
            });
            await db.SaveChangesAsync();
        }

        using var app = new EvaluationFactory(database);
        using var leadStaff = app.CreateAuthenticatedClient(s.Scope.Users.Staff, roles: ["DEPARTMENT_STAFF"]);
        using var partStaffClient = app.CreateAuthenticatedClient(partStaffId, roles: ["DEPARTMENT_STAFF"]);
        using var admin = app.CreateAuthenticatedClient(s.Scope.Users.Staff, roles: ["ADMIN"]);
        using var student = app.CreateAuthenticatedClient(s.Scope.Users.Student, roles: ["STUDENT"]);

        var baseConfigNoInt = new AcademicBlueprintConfig(
            GateRules:
            [
                new("G1", 1, "Review 1", "Scope & Architecture", 10, 3, 5, 20m, false),
                new("G2", 2, "Review 2", "MVP & Pilot", 25, 3, 5, 35m, false),
                new("G3", 3, "Review 3", "Pre-Defense", 45, 3, 5, 45m, true)
            ],
            SprintRules: [new("SP-1", "Sprint 1", 0, 10)],
            MilestoneRules:
            [
                new("MS-1", "Template MS 1", "Desc", 0, 15, 1,
                [
                    new("TASK-G1-CORE", "Core G1 Evidence Package", "Mandatory", "HIGH", true, "G1", 10),
                    new("TASK-G1-CUSTOM", "Overwritten If Not Customized", "Desc", "LOW", false, "G1", 12)
                ])
            ],
            IntegrationDeliverables: []);

        // 1. Create DRAFT IT_MKT blueprint (without INT-* initially)
        var created = await Body<AcademicBlueprintDto>(await leadStaff.PostAsJsonAsync("/api/v1/academic-blueprints",
            new CreateAcademicBlueprintRequest(s.Scope.SemesterId, AcademicTrackCodes.ItMkt, "BP-ITMKT", "Blueprint ITxMKT", leadDeptId, partDeptId, baseConfigNoInt)));
        Assert.Equal("DRAFT", created.Status);

        // 2. Negative: Activate REGISTRATION period while blueprint is still DRAFT => 409 BLUEPRINT_NOT_PUBLISHED
        var openRegBlocked = await admin.PatchAsJsonAsync($"/api/v1/academic/project-periods/{regPeriodId}/status", new { status = "ACTIVE" });
        Assert.Equal(HttpStatusCode.Conflict, openRegBlocked.StatusCode);
        Assert.Equal(WorkflowErrorCodes.BlueprintNotPublished, await ReadProblemCode(openRegBlocked));

        // 3. Negative: Publish IT_MKT without INT-* deliverable => 422 INTEGRATION_DELIVERABLE_REQUIRED
        var pubNoInt = await leadStaff.PostAsync($"/api/v1/academic-blueprints/{created.Id}/publish", null);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, pubNoInt.StatusCode);
        Assert.Equal(WorkflowErrorCodes.IntegrationDeliverableRequired, await ReadProblemCode(pubNoInt));

        // 4. Add INT-01 deliverable covering G1-G3 & FINAL, submit for review
        var validConfig = baseConfigNoInt with
        {
            IntegrationDeliverables = [new("INT-01", "Tracking & Analytics Event Bridge", ["G1", "G2", "G3", "FINAL"])]
        };
        var updated = await Body<AcademicBlueprintDto>(await leadStaff.PutAsJsonAsync($"/api/v1/academic-blueprints/{created.Id}",
            new UpdateAcademicBlueprintRequest("Blueprint ITxMKT Final", partDeptId, validConfig, created.ConcurrencyToken)));
        await Body<AcademicBlueprintDto>(await leadStaff.PostAsync($"/api/v1/academic-blueprints/{created.Id}/submit-review", null));

        // 5. Negative: Publish with only Lead approval (missing Participating dept approval) => 422
        await Body<AcademicBlueprintDto>(await leadStaff.PostAsJsonAsync($"/api/v1/academic-blueprints/{created.Id}/approvals",
            new RecordBlueprintApprovalRequest(leadDeptId, "APPROVED", "IT approved")));
        var pubMissingPart = await leadStaff.PostAsync($"/api/v1/academic-blueprints/{created.Id}/publish", null);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, pubMissingPart.StatusCode);

        // 6. Participating dept approves -> Publish succeeds!
        await Body<AcademicBlueprintDto>(await partStaffClient.PostAsJsonAsync($"/api/v1/academic-blueprints/{created.Id}/approvals",
            new RecordBlueprintApprovalRequest(partDeptId, "APPROVED", "MKT approved")));
        var published = await Body<AcademicBlueprintDto>(await leadStaff.PostAsync($"/api/v1/academic-blueprints/{created.Id}/publish", null));
        Assert.Equal("PUBLISHED", published.Status);

        // 7. Negative: Editing PUBLISHED blueprint => 409 IMMUTABLE_VERSION
        var editPublished = await leadStaff.PutAsJsonAsync($"/api/v1/academic-blueprints/{created.Id}",
            new UpdateAcademicBlueprintRequest("Illegal edit", partDeptId, validConfig, published.ConcurrencyToken));
        Assert.Equal(HttpStatusCode.Conflict, editPublished.StatusCode);
        Assert.Equal(WorkflowErrorCodes.ImmutableVersion, await ReadProblemCode(editPublished));

        // 8. Positive: Now REGISTRATION period opens cleanly!
        var openRegOk = await admin.PatchAsJsonAsync($"/api/v1/academic/project-periods/{regPeriodId}/status", new { status = "ACTIVE" });
        Assert.True(openRegOk.IsSuccessStatusCode, await openRegOk.Content.ReadAsStringAsync());

        // 9. Dry-Run Diff: non-mutating and explicitly preserves customized milestone & task
        var dryRun = await Body<BlueprintDryRunDiffDto>(await leadStaff.PostAsync($"/api/v1/projects/{s.ProjectId}/blueprint-plan/dry-run", null));
        Assert.Equal(3, dryRun.Gates.Count);
        Assert.Contains(dryRun.PreservedCustomizedItems, x => x.Contains("MS-1"));
        Assert.Contains(dryRun.PreservedCustomizedItems, x => x.Contains("TASK-G1-CUSTOM"));
        await using (var db = database.CreateContext())
            Assert.Equal(0, await db.ProjectGateInstances.CountAsync(g => g.ProjectId == s.ProjectId)); // Zero DB writes on Dry-Run!

        // 10. Idempotent Apply (twice): creates 3 gates, 1 sprint, 1 core task; preserves customized task; second call creates 0 duplicates
        var apply1 = await Body<ProjectBlueprintSnapshotDto>(await leadStaff.PostAsync($"/api/v1/projects/{s.ProjectId}/blueprint-plan/apply", null));
        Assert.False(apply1.WasAlreadyApplied);
        Assert.Equal(3, apply1.GatesCreated);
        Assert.Equal(1, apply1.SprintsCreated);
        Assert.Equal(1, apply1.TasksCreated);
        var apply2 = await Body<ProjectBlueprintSnapshotDto>(await leadStaff.PostAsync($"/api/v1/projects/{s.ProjectId}/blueprint-plan/apply", null));
        Assert.True(apply2.WasAlreadyApplied);
        Assert.Equal(0, apply2.GatesCreated);

        // 11. Negative: Student cannot delete Core Gate Task => 409 RESTRICTED_DELETE
        long coreTaskId;
        await using (var db = database.CreateContext())
        {
            var customTask = await db.Tasks.SingleAsync(t => t.TemplateOriginId == "TASK-G1-CUSTOM");
            Assert.Equal("Custom Student Task Kept", customTask.Title);
            coreTaskId = (await db.Tasks.SingleAsync(t => t.TemplateOriginId == "TASK-G1-CORE")).Id;
        }
        var delCore = await student.DeleteAsync($"/api/v1/tasks/{coreTaskId}");
        Assert.Equal(HttpStatusCode.Conflict, delCore.StatusCode);
        Assert.Equal(WorkflowErrorCodes.RestrictedDelete, await ReadProblemCode(delCore));

        // 12. Gate DeadlineChangeRequest: reversed order => 422; valid request -> student cannot approve (403) -> Lead Staff approves -> Gate updated
        var badDcr = await student.PostAsJsonAsync($"/api/v1/projects/{s.ProjectId}/review-gates/G1/deadline-change-requests",
            new CreateGateDeadlineChangeRequest(Now.AddDays(30), Now.AddDays(20), Now.AddDays(25), "Invalid reversed"));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, badDcr.StatusCode);

        var dcr = await Body<GateDeadlineChangeRequestDto>(await student.PostAsJsonAsync(
            $"/api/v1/projects/{s.ProjectId}/review-gates/G1/deadline-change-requests",
            new CreateGateDeadlineChangeRequest(Now.AddDays(18), Now.AddDays(21), Now.AddDays(25), "Extended field pilot data collection")));
        Assert.Equal("PENDING", dcr.Status);

        var studentApprove = await student.PostAsJsonAsync($"/api/v1/projects/{s.ProjectId}/deadline-change-requests/{dcr.Id}/review",
            new ReviewGateDeadlineChangeRequest("APPROVED", "Self approve", dcr.ConcurrencyToken));
        Assert.Equal(HttpStatusCode.Forbidden, studentApprove.StatusCode);

        var approvedDcr = await Body<GateDeadlineChangeRequestDto>(await leadStaff.PostAsJsonAsync(
            $"/api/v1/projects/{s.ProjectId}/deadline-change-requests/{dcr.Id}/review",
            new ReviewGateDeadlineChangeRequest("APPROVED", "Approved by Lead Dept", dcr.ConcurrencyToken)));
        Assert.Equal("APPROVED", approvedDcr.Status);

        await using (var db = database.CreateContext())
        {
            var g1 = await db.ProjectGateInstances.SingleAsync(g => g.ProjectId == s.ProjectId && g.Code == "G1");
            Assert.Equal(dcr.RequestedRevisionDueAt, g1.RevisionDueAt);
        }
    }
}
