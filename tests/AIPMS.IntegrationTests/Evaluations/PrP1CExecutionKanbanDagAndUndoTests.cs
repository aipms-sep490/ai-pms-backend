using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Tasks.Commands;
using AIPMS.Application.Features.Tasks.DTOs;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.IntegrationTests.Evaluations;

public sealed class PrP1CExecutionKanbanDagAndUndoTests(EvaluationDraftDatabaseFixture database)
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
    public async Task One_step_task_create_blocker_15char_done_evidence_gate_dag_422_and_soft_delete_undo_5s_work()
    {
        var s = await database.Seed(false);
        long milestoneId, sprintId, gateReqId, memberId;

        await using (var db = database.CreateContext())
        {
            var project = await db.Projects.SingleAsync(p => p.Id == s.ProjectId);
            project.Status = "ACTIVE";
            var itMajorId = await db.ProjectMajors.Where(pm => pm.ProjectId == s.ProjectId).Select(pm => pm.MajorId).FirstAsync();

            var leaderMember = await db.TeamMembers.SingleAsync(m => m.TeamId == project.TeamId && m.UserId == s.Scope.Users.Student);
            leaderMember.IsLeader = true;

            var studentRoleId = await db.Roles.Where(r => r.Code == "STUDENT").Select(r => r.Id).SingleAsync();
            var normalMember = new M.User
            {
                Email = $"exec_mem_{Guid.NewGuid():N}@e2e.invalid",
                FullName = "IT Team Member",
                PasswordHash = "unused",
                Status = "ACTIVE",
                DepartmentId = s.Scope.Users.DepartmentId,
                MajorId = itMajorId,
                AcademicProfileStatus = "VERIFIED",
                UserRoleUsers = [new() { RoleId = studentRoleId }]
            };
            db.Users.Add(normalMember);
            await db.SaveChangesAsync();
            memberId = normalMember.Id;

            db.TeamMembers.Add(new M.TeamMember
            {
                TeamId = project.TeamId,
                AcademicSemesterId = s.Scope.SemesterId,
                UserId = memberId,
                IsLeader = false,
                JoinedAt = Now.AddDays(-10)
            });

            var ms = new M.Milestone
            {
                ProjectId = s.ProjectId,
                Title = "Execution MS 1",
                Status = "IN_PROGRESS",
                SortOrder = 1,
                CreatedBy = s.Scope.Users.Student,
                CreatedAt = Now,
                UpdatedAt = Now,
                ConcurrencyToken = Guid.NewGuid()
            };
            var sprint = new Sprint
            {
                ProjectId = s.ProjectId,
                Name = "Sprint 1",
                Status = "PLANNED",
                StartAt = Now,
                EndAt = Now.AddDays(14),
                CreatedBy = s.Scope.Users.Student,
                CreatedAt = Now,
                UpdatedAt = Now
            };
            var tpl = new ReviewGateTemplate
            {
                Code = "TPL-C-" + Guid.NewGuid().ToString("N")[..6],
                Name = "Gate C",
                Mode = "SINGLE_MAJOR",
                Major = "SE",
                Status = "PUBLISHED",
                Version = 1,
                CreatedBy = s.Scope.Users.Staff,
                CreatedAt = Now,
                UpdatedAt = Now
            };
            db.Milestones.Add(ms);
            db.Set<Sprint>().Add(sprint);
            db.ReviewGateTemplates.Add(tpl);
            await db.SaveChangesAsync();
            milestoneId = ms.Id;
            sprintId = sprint.Id;

            var gate = new ProjectGateInstance
            {
                ProjectId = s.ProjectId,
                TemplateId = tpl.Id,
                TemplateVersion = 1,
                Code = "G1",
                Sequence = 1,
                Title = "Gate 1",
                Objective = "Obj",
                Status = "PLANNED",
                ReviewEligibility = "READY",
                Required = true,
                BlockNextGate = true,
                CreatedAt = Now
            };
            db.ProjectGateInstances.Add(gate);
            await db.SaveChangesAsync();

            var req = new ProjectGateRequirement
            {
                GateInstanceId = gate.Id,
                Code = "SRS-01",
                Title = "Software Requirements Spec",
                Type = "DOCUMENT",
                Scope = "COMMON",
                Required = true,
                VerificationStatus = "PENDING"
            };
            db.ProjectGateRequirements.Add(req);
            await db.SaveChangesAsync();
            gateReqId = req.Id;
        }

        using var app = new EvaluationFactory(database);
        using var leader = app.CreateAuthenticatedClient(s.Scope.Users.Student, roles: ["STUDENT"]);
        using var member = app.CreateAuthenticatedClient(memberId, roles: ["STUDENT"]);

        // 1. Negative: Creating task directly in DONE / IN_REVIEW => 422 INVALID_INITIAL_STATUS
        var badInit = await leader.PostAsJsonAsync("/api/v1/tasks",
            new CreateTaskCommand(milestoneId, null, "Illegal Done Task", "Desc", "HIGH", null, null, [], InitialStatus: "DONE"));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, badInit.StatusCode);
        Assert.Equal(WorkflowErrorCodes.InvalidInitialStatus, await ReadProblemCode(badInit));

        // 2. Negative: Member cannot create Gate-linked core task => 403
        var memberCore = await member.PostAsJsonAsync("/api/v1/tasks",
            new CreateTaskCommand(milestoneId, null, "Core Gate Task", "Desc", "HIGH", null, null, [], GateRequirementId: gateReqId));
        Assert.Equal(HttpStatusCode.Forbidden, memberCore.StatusCode);

        // 3. Positive: Member creates normal task 1-step in IN_PROGRESS with labels & criteria
        var memberTask = await Body<TaskDto>(await member.PostAsJsonAsync("/api/v1/tasks",
            new CreateTaskCommand(milestoneId, null, "Member Feature Task", "Build login UI", "MEDIUM", null, null, [memberId],
                InitialStatus: "IN_PROGRESS", SprintId: sprintId, StoryPoints: 5, Labels: ["enhancement"], InitialCriteria: ["Form validation", "Unit tests"])));
        Assert.Equal("IN_PROGRESS", memberTask.Status);
        Assert.Equal(sprintId, memberTask.SprintId);
        Assert.Equal(5, memberTask.StoryPoints);
        Assert.Contains("enhancement", memberTask.Labels!);

        // 4. Positive: Leader creates Gate-linked core task in IN_PROGRESS
        var gateTask = await Body<TaskDto>(await leader.PostAsJsonAsync("/api/v1/tasks",
            new CreateTaskCommand(milestoneId, null, "Deliver SRS Spec", "Core gate requirement", "HIGH", null, null, [],
                InitialStatus: "IN_PROGRESS", GateRequirementId: gateReqId)));
        Assert.True(gateTask.IsCoreGateTask);
        Assert.Equal(gateReqId, gateTask.GateRequirementId);

        // 5. Negative: BLOCKED reason < 15 chars => 422
        var shortBlock = await leader.PutAsJsonAsync($"/api/v1/tasks/{gateTask.Id}/status",
            new { newStatus = "BLOCKED", reason = "Too short", concurrencyToken = gateTask.ConcurrencyToken });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, shortBlock.StatusCode);

        // 6. Positive: BLOCKED reason >= 15 chars records BlockedSinceAt & BlockerReason, then resumes IN_PROGRESS
        var blocked = await Body<TaskDto>(await leader.PutAsJsonAsync($"/api/v1/tasks/{gateTask.Id}/status",
            new { newStatus = "BLOCKED", reason = "Waiting on database schema approval from mentor", concurrencyToken = gateTask.ConcurrencyToken }));
        Assert.Equal("BLOCKED", blocked.Status);
        Assert.NotNull(blocked.BlockedSinceAt);
        var resumed = await Body<TaskDto>(await leader.PutAsJsonAsync($"/api/v1/tasks/{gateTask.Id}/status",
            new { newStatus = "IN_PROGRESS", reason = "Schema approved", concurrencyToken = blocked.ConcurrencyToken }));
        Assert.Null(resumed.BlockedSinceAt);

        // 7. Negative: Gate-linked task -> DONE without READY evidence => 422 EVIDENCE_REQUIRED
        var doneNoEv = await leader.PutAsJsonAsync($"/api/v1/tasks/{gateTask.Id}/status",
            new { newStatus = "DONE", reason = "Finished", concurrencyToken = resumed.ConcurrencyToken });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, doneNoEv.StatusCode);
        Assert.Equal(WorkflowErrorCodes.EvidenceRequired, await ReadProblemCode(doneNoEv));

        // Attach READY GateEvidence -> DONE now succeeds!
        await using (var db = database.CreateContext())
        {
            db.GateEvidence.Add(new GateEvidence
            {
                ProjectId = s.ProjectId,
                RequirementId = gateReqId,
                Version = 1,
                ArtifactName = "srs.pdf",
                ContentType = "application/pdf",
                SizeBytes = 1024,
                Sha256 = new string('c', 64),
                StorageKey = "k1",
                SubmittedBy = s.Scope.Users.Student,
                SubmittedAt = Now,
                Status = "SUBMITTED",
                ScanStatus = FileScanStatuses.Ready
            });
            await db.SaveChangesAsync();
        }
        var doneOk = await Body<TaskDto>(await leader.PutAsJsonAsync($"/api/v1/tasks/{gateTask.Id}/status",
            new { newStatus = "DONE", reason = "Evidence uploaded", concurrencyToken = resumed.ConcurrencyToken }));
        Assert.Equal("DONE", doneOk.Status);

        // 8. Negative: Circular dependency => 422 CIRCULAR_DEPENDENCY
        var tA = await Body<TaskDto>(await leader.PostAsJsonAsync("/api/v1/tasks",
            new CreateTaskCommand(milestoneId, null, "DAG Task A", null, "MEDIUM", null, null, [])));
        var tB = await Body<TaskDto>(await leader.PostAsJsonAsync("/api/v1/tasks",
            new CreateTaskCommand(milestoneId, null, "DAG Task B", null, "MEDIUM", null, null, [])));
        await Body<TaskDto>(await leader.PostAsJsonAsync("/api/v1/tasks/dependency",
            new AddTaskDependencyCommand(tB.Id, tA.Id, "FINISH_TO_START", tB.ConcurrencyToken)));
        var cycleRes = await leader.PostAsJsonAsync("/api/v1/tasks/dependency",
            new AddTaskDependencyCommand(tA.Id, tB.Id, "FINISH_TO_START", tA.ConcurrencyToken));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, cycleRes.StatusCode);
        Assert.Equal(WorkflowErrorCodes.CircularDependency, await ReadProblemCode(cycleRes));

        // 9. Soft-delete Undo within 5s (same actor restores; different actor gets 403; expired gets 409)
        var tempTask = await Body<TaskDto>(await member.PostAsJsonAsync("/api/v1/tasks",
            new CreateTaskCommand(milestoneId, null, "Temp Deletable Task", null, "LOW", null, null, [])));
        var delRes = await member.DeleteAsync($"/api/v1/tasks/{tempTask.Id}?concurrencyToken={tempTask.ConcurrencyToken}");
        Assert.Equal(HttpStatusCode.NoContent, delRes.StatusCode);

        // Hidden from normal GetById
        var getDeleted = await member.GetAsync($"/api/v1/tasks/{tempTask.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getDeleted.StatusCode);

        // Different actor cannot restore => 403
        var wrongActorRestore = await leader.PostAsync($"/api/v1/tasks/{tempTask.Id}/restore", null);
        Assert.Equal(HttpStatusCode.Forbidden, wrongActorRestore.StatusCode);

        // Original actor restores within 5s => 200 OK!
        var restored = await Body<TaskDto>(await member.PostAsync($"/api/v1/tasks/{tempTask.Id}/restore", null));
        Assert.Equal(tempTask.Id, restored.Id);

        // Expire undo window -> 409 UNDO_WINDOW_EXPIRED
        await member.DeleteAsync($"/api/v1/tasks/{restored.Id}?concurrencyToken={restored.ConcurrencyToken}");
        await using (var db = database.CreateContext())
        {
            var raw = await db.Tasks.IgnoreQueryFilters().SingleAsync(t => t.Id == restored.Id);
            raw.RestoreUntilAt = Now.AddSeconds(-10);
            await db.SaveChangesAsync();
        }
        var expiredRestore = await member.PostAsync($"/api/v1/tasks/{restored.Id}/restore", null);
        Assert.Equal(HttpStatusCode.Conflict, expiredRestore.StatusCode);
    }
}
