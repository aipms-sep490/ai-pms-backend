using System.Net;
using System.Text.Json;
using AIPMS.Application.Features.ActionAggregators;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.IntegrationTests.Evaluations;

public sealed class PrP1EWorkspacesActionCenterAndIndustryTests(EvaluationDraftDatabaseFixture database)
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

    [Fact]
    public async Task Action_aggregators_surface_unified_lecturer_triage_exclude_soft_deleted_tasks_and_isolate_industry()
    {
        var s = await database.Seed(false);
        long supervisorUserId, evalAssignmentId, activeTaskId, deletedTaskId, blockedTaskId, expertUserId;

        await using (var db = database.CreateContext())
        {
            var project = await db.Projects.SingleAsync(p => p.Id == s.ProjectId);
            project.Status = "ACTIVE";

            supervisorUserId = await db.SupervisorAssignments
                .Where(a => a.ProjectId == s.ProjectId && a.EndedAt == null)
                .Select(a => a.SupervisorProfile.UserId)
                .FirstAsync();

            var milestone = new M.Milestone
            {
                ProjectId = s.ProjectId,
                Title = "Sprint 1",
                Status = "IN_PROGRESS",
                DueDate = DateOnly.FromDateTime(Now.AddDays(7)),
                CreatedBy = s.Scope.Users.Student
            };
            db.Milestones.Add(milestone);
            await db.SaveChangesAsync();

            var activeTask = new M.Task
            {
                MilestoneId = milestone.Id,
                Title = "Active Student Task",
                Status = "IN_PROGRESS",
                Priority = "MEDIUM",
                CreatedBy = s.Scope.Users.Student,
                DueAt = Now.AddDays(2)
            };
            var deletedTask = new M.Task
            {
                MilestoneId = milestone.Id,
                Title = "Soft Deleted Task",
                Status = "IN_PROGRESS",
                Priority = "HIGH",
                CreatedBy = s.Scope.Users.Student,
                DeletedAt = Now.AddMinutes(-5)
            };
            var blockedTask = new M.Task
            {
                MilestoneId = milestone.Id,
                Title = "Stuck API Integration",
                Status = "BLOCKED",
                Priority = "HIGH",
                CreatedBy = s.Scope.Users.Student,
                BlockerReason = "Waiting for external merchant gateway credential approval",
                BlockedSinceAt = Now.AddHours(-60)
            };
            db.Tasks.AddRange(activeTask, deletedTask, blockedTask);
            await db.SaveChangesAsync();
            activeTaskId = activeTask.Id;
            deletedTaskId = deletedTask.Id;
            blockedTaskId = blockedTask.Id;

            db.TaskAssignees.AddRange(
                new M.TaskAssignee { TaskId = activeTaskId, UserId = s.Scope.Users.Student, AssignedBy = s.Scope.Users.Student },
                new M.TaskAssignee { TaskId = deletedTaskId, UserId = s.Scope.Users.Student, AssignedBy = s.Scope.Users.Student },
                new M.TaskAssignee { TaskId = blockedTaskId, UserId = s.Scope.Users.Student, AssignedBy = s.Scope.Users.Student });

            var evalAssign = new EvaluationAssignment
            {
                ProjectId = s.ProjectId,
                EvaluatorId = supervisorUserId,
                RubricId = s.RubricId,
                ProjectPeriodId = s.PeriodId,
                DepartmentId = s.Scope.Users.DepartmentId,
                Stage = "DEFENSE",
                Scope = "UNKNOWN",
                Status = "ACTIVE",
                AssignedBy = s.Scope.Users.Staff,
                AssignedAt = Now.AddDays(-1),
                ValidFrom = Now.AddDays(-1),
                ValidUntil = DateTime.UtcNow.AddDays(30),
                ConcurrencyToken = Guid.NewGuid()
            };
            db.Set<EvaluationAssignment>().Add(evalAssign);

            var pendingLock = new FinalSubmissionDraft
            {
                ProjectId = s.ProjectId,
                ProjectPeriodId = s.PeriodId,
                CreatedBy = s.Scope.Users.Student,
                UpdatedBy = s.Scope.Users.Student,
                CreatedAt = Now.AddDays(-1),
                UpdatedAt = Now.AddHours(-2),
                ConcurrencyToken = Guid.NewGuid(),
                LockWorkflowStatus = "PENDING_SUPERVISOR_LOCK",
                LockRequestedBy = s.Scope.Users.Student,
                LockRequestedAt = Now.AddHours(-2)
            };
            db.Set<FinalSubmissionDraft>().Add(pendingLock);

            var expertUser = new M.User
            {
                Email = $"expert_{Guid.NewGuid():N}@industry.invalid",
                FullName = "External Mentor",
                PasswordHash = "unused",
                Status = "ACTIVE"
            };
            db.Users.Add(expertUser);
            await db.SaveChangesAsync();
            evalAssignmentId = evalAssign.Id;
            expertUserId = expertUser.Id;
        }

        using var factory = new EvaluationFactory(database);
        using var studentClient = factory.CreateAuthenticatedClient(s.Scope.Users.Student, roles: ["STUDENT"]);
        using var supervisorClient = factory.CreateAuthenticatedClient(supervisorUserId, roles: ["LECTURER"]);
        using var expertClient = factory.CreateAuthenticatedClient(expertUserId, roles: ["INDUSTRY_EXPERT"]);

        // 1. Student Next Actions excludes soft-deleted task while keeping active task
        var studentActions = await Body<StudentNextActionsDto>(await studentClient.GetAsync("/api/v1/students/me/next-actions"));
        Assert.Contains(studentActions.Items, x => x.Kind == "TASK" && x.Id == activeTaskId);
        Assert.DoesNotContain(studentActions.Items, x => x.Kind == "TASK" && x.Id == deletedTaskId);

        // 2. Lecturer Unified Action Center surfaces Evaluation Assignment, Blocked > 48h Task, and Pending Final Lock
        var triage = await Body<SupervisorTriageQueueDto>(await supervisorClient.GetAsync("/api/v1/supervisors/me/triage-queue"));
        Assert.Contains(triage.Items, x => x.Kind == "EVALUATION_ASSIGNMENT" && x.Id == evalAssignmentId && x.DeepLink == $"/evaluator/assignments/{evalAssignmentId}");
        Assert.Contains(triage.Items, x => x.Kind == "TASK_BLOCKED_48H" && x.Id == blockedTaskId);
        Assert.Contains(triage.Items, x => x.Kind == "FINAL_LOCK_APPROVAL" && x.ProjectId == s.ProjectId);

        // 3. Negative role isolation: Lecturer cannot call Student next-actions; Industry Expert gets 403 on internal workspaces
        Assert.Equal(HttpStatusCode.Forbidden, (await supervisorClient.GetAsync("/api/v1/students/me/next-actions")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await expertClient.GetAsync("/api/v1/supervisors/me/triage-queue")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await expertClient.GetAsync("/api/v1/students/me/next-actions")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await expertClient.GetAsync($"/api/v1/departments/{s.Scope.Users.DepartmentId}/urgent-actions")).StatusCode);
    }
}
