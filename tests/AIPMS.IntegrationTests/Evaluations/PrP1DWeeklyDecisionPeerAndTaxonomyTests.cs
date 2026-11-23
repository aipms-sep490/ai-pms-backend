using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Meetings.DTOs;
using AIPMS.Application.Features.ProjectLabels;
using AIPMS.Application.Features.ProgressReports.DTOs;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.IntegrationTests.Evaluations;

public sealed class PrP1DWeeklyDecisionPeerAndTaxonomyTests(EvaluationDraftDatabaseFixture database)
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
    public async Task Weekly_report_locked_decision_supersede_task_taxonomy_and_project_labels_work()
    {
        var s = await database.Seed(false);
        long itMajorId, mktMajorId, memberId, meetingId, decisionId, taskId, reviewedReportId;
        string meetingToken;

        await using (var db = database.CreateContext())
        {
            var project = await db.Projects.SingleAsync(p => p.Id == s.ProjectId);
            project.Status = "ACTIVE";
            project.TrackCode = "IT_MKT";
            itMajorId = await db.ProjectMajors.Where(pm => pm.ProjectId == s.ProjectId).Select(pm => pm.MajorId).FirstAsync();

            var mktDeptId = (await db.Users.SingleAsync(u => u.Id == s.Scope.Users.OtherLecturer)).DepartmentId!.Value;
            var mktMajor = new M.Major { DepartmentId = mktDeptId, Code = "MKT_" + Guid.NewGuid().ToString("N")[..4], Name = "Marketing", IsActive = true };
            db.Majors.Add(mktMajor);
            await db.SaveChangesAsync();
            mktMajorId = mktMajor.Id;
            db.ProjectMajors.Add(new M.ProjectMajor { ProjectId = s.ProjectId, MajorId = mktMajorId });

            var leaderMember = await db.TeamMembers.SingleAsync(m => m.TeamId == project.TeamId && m.UserId == s.Scope.Users.Student);
            leaderMember.IsLeader = true;

            var studentRoleId = await db.Roles.Where(r => r.Code == "STUDENT").Select(r => r.Id).SingleAsync();
            var memberUser = new M.User
            {
                Email = $"tax_mem_{Guid.NewGuid():N}@e2e.invalid",
                FullName = "Normal Member",
                PasswordHash = "unused",
                Status = "ACTIVE",
                DepartmentId = s.Scope.Users.DepartmentId,
                MajorId = itMajorId,
                AcademicProfileStatus = "VERIFIED",
                UserRoleUsers = [new() { RoleId = studentRoleId }]
            };
            db.Users.Add(memberUser);
            await db.SaveChangesAsync();
            memberId = memberUser.Id;
            db.TeamMembers.Add(new M.TeamMember
            {
                TeamId = project.TeamId,
                AcademicSemesterId = s.Scope.SemesterId,
                UserId = memberId,
                IsLeader = false,
                JoinedAt = Now.AddDays(-10)
            });

            var meeting = new M.Meeting
            {
                ProjectId = s.ProjectId,
                Title = "Architecture Sync",
                StartAt = Now.AddDays(-2),
                EndAt = Now.AddDays(-2).AddHours(1),
                Status = "COMPLETED",
                CreatedBy = s.Scope.Users.Student,
                CreatedAt = Now.AddDays(-3),
                UpdatedAt = Now.AddDays(-2),
                ConcurrencyToken = Guid.NewGuid()
            };
            db.Meetings.Add(meeting);
            await db.SaveChangesAsync();
            meetingId = meeting.Id;
            meetingToken = meeting.ConcurrencyToken.ToString("N");

            var dec = new MeetingDecision
            {
                MeetingId = meeting.Id,
                Content = "Use REST v1 for payment integration",
                DecidedBy = s.Scope.Users.Student,
                DecidedAt = Now.AddDays(-2),
                CreatedAt = Now.AddDays(-2)
            };
            db.MeetingDecisions.Add(dec);

            var ms = new M.Milestone
            {
                ProjectId = s.ProjectId,
                Title = "MS D",
                Status = "IN_PROGRESS",
                SortOrder = 1,
                CreatedBy = s.Scope.Users.Student,
                CreatedAt = Now,
                UpdatedAt = Now,
                ConcurrencyToken = Guid.NewGuid()
            };
            db.Milestones.Add(ms);
            await db.SaveChangesAsync();
            decisionId = dec.Id;

            var task = new M.Task
            {
                MilestoneId = ms.Id,
                Title = "Build API Endpoint",
                Status = "IN_PROGRESS",
                Priority = "HIGH",
                CreatedBy = s.Scope.Users.Student,
                CreatedAt = Now,
                UpdatedAt = Now,
                ConcurrencyToken = Guid.NewGuid()
            };
            db.Tasks.Add(task);

            var report = new M.ProgressReport
            {
                ProjectId = s.ProjectId,
                ReportType = "WEEKLY",
                PeriodStart = DateOnly.FromDateTime(Now.AddDays(-7)),
                PeriodEnd = DateOnly.FromDateTime(Now),
                Summary = "Reviewed weekly report",
                Status = "REVIEWED",
                SubmittedBy = s.Scope.Users.Student,
                CreatedAt = Now.AddDays(-1),
                UpdatedAt = Now.AddDays(-1),
                ConcurrencyToken = Guid.NewGuid()
            };
            db.ProgressReports.Add(report);
            await db.SaveChangesAsync();
            taskId = task.Id;
            reviewedReportId = report.Id;
        }

        using var app = new EvaluationFactory(database);
        using var leader = app.CreateAuthenticatedClient(s.Scope.Users.Student, roles: ["STUDENT"]);
        using var member = app.CreateAuthenticatedClient(memberId, roles: ["STUDENT"]);

        // 1. Negative: Updating REVIEWED progress report => 409 REPORT_LOCKED
        var editReviewed = await leader.PutAsJsonAsync($"/api/v1/progress-reports/{reviewedReportId}",
            new UpdateProgressReportRequest("Try overwrite", null, null, null));
        Assert.Equal(HttpStatusCode.Conflict, editReviewed.StatusCode);
        Assert.Equal(WorkflowErrorCodes.ReportLocked, await ReadProblemCode(editReviewed));

        // 2. Positive: Supersede Meeting Decision (append-only chain preserving original content)
        var superseded = await Body<MeetingDecisionDto>(await leader.PostAsJsonAsync(
            $"/api/v1/meetings/{meetingId}/decisions/{decisionId}/supersede",
            new CreateMeetingDecisionRequest("Switch to REST v2 webhook architecture", meetingToken)));
        Assert.NotEqual(decisionId, superseded.Id);
        await using (var db = database.CreateContext())
        {
            var oldDec = await db.MeetingDecisions.SingleAsync(d => d.Id == decisionId);
            Assert.Equal("Use REST v1 for payment integration", oldDec.Content);
            Assert.Equal(superseded.Id, oldDec.SupersededByDecisionId);
        }

        // 3. Project Labels auto-seed (includes IT_MKT integration & pilot-data) + CRUD ACL
        var labels = await Body<List<ProjectLabelDto>>(await leader.GetAsync($"/api/v1/projects/{s.ProjectId}/labels"));
        Assert.Contains(labels, l => l.Slug == "bug");
        Assert.Contains(labels, l => l.Slug == "integration");

        // Negative: Normal member cannot POST project label definitions => 403
        var memberCreateLabel = await member.PostAsJsonAsync($"/api/v1/projects/{s.ProjectId}/labels",
            new SaveProjectLabelRequest("custom-mem", "Member Label", "#112233", "Not allowed"));
        Assert.Equal(HttpStatusCode.Forbidden, memberCreateLabel.StatusCode);

        // Positive: Leader creates custom colored label
        var createdLabel = await Body<ProjectLabelDto>(await leader.PostAsJsonAsync($"/api/v1/projects/{s.ProjectId}/labels",
            new SaveProjectLabelRequest("backend-api", "Backend API", "#1D4ED8", "API contracts")));
        Assert.Equal("#1D4ED8", createdLabel.ColorHex);

        // 4. Task Taxonomy: Negative mismatched category for IT major => 422 INVALID_CATEGORY_FOR_MAJOR
        var badCat = await leader.PatchAsJsonAsync($"/api/v1/tasks/{taskId}/taxonomy",
            new UpdateTaskTaxonomyRequest("Budget & Media", "CODING", "NONE", ["bug"], PrimaryMajorId: itMajorId));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, badCat.StatusCode);
        Assert.Equal(WorkflowErrorCodes.InvalidCategoryForMajor, await ReadProblemCode(badCat));

        // Negative: > 10 labels on a task => 422
        var tooManyLabels = await leader.PatchAsJsonAsync($"/api/v1/tasks/{taskId}/taxonomy",
            new UpdateTaskTaxonomyRequest("Backend", "CODING", "NONE",
                ["l1", "l2", "l3", "l4", "l5", "l6", "l7", "l8", "l9", "l10", "l11"], PrimaryMajorId: itMajorId));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, tooManyLabels.StatusCode);

        // Positive: Valid IT category + Sub-status + Handoff WAITING_FOR_MKT + Labels
        var okTax = await Body<TaskTaxonomyStateDto>(await leader.PatchAsJsonAsync($"/api/v1/tasks/{taskId}/taxonomy",
            new UpdateTaskTaxonomyRequest("Backend", "PR_OPENED", "WAITING_FOR_MKT", ["backend-api", "integration"], PrimaryMajorId: itMajorId)));
        Assert.Equal("Backend", okTax.TaskCategory);
        Assert.Equal("PR_OPENED", okTax.DisciplineSubStatus);
        Assert.Equal("WAITING_FOR_MKT", okTax.HandoffFlag);
        Assert.NotNull(okTax.HandoffFlaggedAt);
        Assert.Contains("integration", okTax.Labels);
    }
}
