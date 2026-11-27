using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.ProjectChecklists;
using AIPMS.Domain.Teams;
using AIPMS.Infrastructure.Persistence.Models;
using AIPMS.IntegrationTests.Evaluations;
using Microsoft.EntityFrameworkCore;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.IntegrationTests.Security;

public sealed class ProjectChecklistAuthorizationTests(EvaluationDraftDatabaseFixture database)
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
    public async Task Checklist_And_Diff_Enforce_FailClosed_AuthZ_N6_Masking_And_Revoked_Reviewer()
    {
        var s = await database.Seed(false);
        long itMajorId, unrelatedStudentId, unrelatedLecturerId, otherDeptStaffId, revokedReviewerId, activeReviewerId, primarySupId;

        await using (var db = database.CreateContext())
        {
            var project = await db.Projects.SingleAsync(p => p.Id == s.ProjectId);
            project.Status = "ACTIVE";
            project.TrackCode = AcademicTrackCodes.ItOnly;
            itMajorId = await db.ProjectMajors.Where(pm => pm.ProjectId == s.ProjectId).Select(pm => pm.MajorId).FirstAsync();

            var stuRoleId = await db.Roles.Where(r => r.Code == AppRoles.Student).Select(r => r.Id).SingleAsync();
            var lecRoleId = await db.Roles.Where(r => r.Code == AppRoles.Lecturer).Select(r => r.Id).SingleAsync();
            var staffRoleId = await db.Roles.Where(r => r.Code == AppRoles.DepartmentStaff).Select(r => r.Id).SingleAsync();
            var otherDeptId = (await db.Users.SingleAsync(u => u.Id == s.Scope.Users.OtherLecturer)).DepartmentId!.Value;

            var unrelatedStu = new M.User
            {
                Email = $"chk_unrel_stu_{Guid.NewGuid():N}@e2e.invalid",
                FullName = "Unrelated Student",
                PasswordHash = "unused",
                Status = "ACTIVE",
                DepartmentId = s.Scope.Users.DepartmentId,
                MajorId = itMajorId,
                UserRoleUsers = [new() { RoleId = stuRoleId }]
            };
            var unrelatedLec = new M.User
            {
                Email = $"chk_unrel_lec_{Guid.NewGuid():N}@e2e.invalid",
                FullName = "Unrelated Lecturer",
                PasswordHash = "unused",
                Status = "ACTIVE",
                DepartmentId = s.Scope.Users.DepartmentId,
                UserRoleUsers = [new() { RoleId = lecRoleId }]
            };
            var otherStaff = new M.User
            {
                Email = $"chk_other_staff_{Guid.NewGuid():N}@e2e.invalid",
                FullName = "Other Dept Staff",
                PasswordHash = "unused",
                Status = "ACTIVE",
                DepartmentId = otherDeptId,
                UserRoleUsers = [new() { RoleId = staffRoleId }]
            };
            var revokedRev = new M.User
            {
                Email = $"chk_rev_rev_{Guid.NewGuid():N}@e2e.invalid",
                FullName = "Revoked Gate Reviewer",
                PasswordHash = "unused",
                Status = "ACTIVE",
                DepartmentId = s.Scope.Users.DepartmentId,
                UserRoleUsers = [new() { RoleId = lecRoleId }]
            };
            db.Users.AddRange(unrelatedStu, unrelatedLec, otherStaff, revokedRev);
            await db.SaveChangesAsync();

            unrelatedStudentId = unrelatedStu.Id;
            unrelatedLecturerId = unrelatedLec.Id;
            otherDeptStaffId = otherStaff.Id;
            revokedReviewerId = revokedRev.Id;
            activeReviewerId = s.Scope.Users.Lecturer;
            primarySupId = await db.SupervisorAssignments.Where(a => a.ProjectId == s.ProjectId && a.IsPrimary)
                .Select(a => a.SupervisorProfile.UserId).SingleAsync();

            var tpl = new ReviewGateTemplate
            {
                Code = "TPL-CHKSEC-" + Guid.NewGuid().ToString("N")[..6],
                Name = "Checklist Security Gate Template",
                Mode = "SINGLE_MAJOR",
                Major = "IT_ONLY",
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
                Title = "Review 1",
                Objective = "Check auth",
                Status = "IN_PROGRESS",
                ReviewEligibility = "READY",
                Required = true,
                WindowStart = Now.AddDays(-5),
                WindowEnd = Now.AddDays(5),
                CreatedAt = Now
            };
            db.ProjectGateInstances.Add(g1);
            await db.SaveChangesAsync();

            db.GateReviewerAssignments.AddRange(
                new GateReviewerAssignment
                {
                    GateInstanceId = g1.Id,
                    ReviewerId = activeReviewerId,
                    Scope = "COMMON",
                    Status = "PENDING_REVIEW",
                    ValidFrom = Now.AddDays(-1),
                    ValidUntil = Now.AddDays(5)
                },
                new GateReviewerAssignment
                {
                    GateInstanceId = g1.Id,
                    ReviewerId = revokedReviewerId,
                    Scope = "COMMON",
                    Status = "REVOKED",
                    ValidFrom = Now.AddDays(-5),
                    ValidUntil = Now.AddDays(5),
                    RevokedAt = Now.AddMinutes(-5),
                    RevokedBy = s.Scope.Users.Staff,
                    RevocationReason = "Reassigned reviewer"
                });
            await db.SaveChangesAsync();
        }

        using var app = new EvaluationFactory(database);
        using var leadStaff = app.CreateAuthenticatedClient(s.Scope.Users.Staff, roles: [AppRoles.DepartmentStaff]);
        using var teamStudent = app.CreateAuthenticatedClient(s.Scope.Users.Student, roles: [AppRoles.Student]);
        using var primarySup = app.CreateAuthenticatedClient(primarySupId, roles: [AppRoles.Lecturer]);
        using var activeReviewer = app.CreateAuthenticatedClient(activeReviewerId, roles: [AppRoles.Lecturer]);
        using var revokedReviewer = app.CreateAuthenticatedClient(revokedReviewerId, roles: [AppRoles.Lecturer]);
        using var unrelatedStudent = app.CreateAuthenticatedClient(unrelatedStudentId, roles: [AppRoles.Student]);
        using var unrelatedLecturer = app.CreateAuthenticatedClient(unrelatedLecturerId, roles: [AppRoles.Lecturer]);
        using var otherStaffClient = app.CreateAuthenticatedClient(otherDeptStaffId, roles: [AppRoles.DepartmentStaff]);

        // Create + Publish Master Template and instantiate DRAFT project checklist
        var masterTpl = await Body<MasterChecklistTemplateDto>(await leadStaff.PostAsJsonAsync("/api/v1/checklist-templates",
            new CreateMasterChecklistTemplateRequest(s.Scope.SemesterId, AcademicTrackCodes.ItOnly, "CHK-SEC1", "Security Checklist", s.Scope.Users.DepartmentId,
            [new("IT-01", "Core", "IT", itMajorId, "NONE", "Working build?", "Mandatory", 1.0m)])));
        await Body<MasterChecklistTemplateDto>(await leadStaff.PostAsync($"/api/v1/checklist-templates/{masterTpl.Id}/publish", null));
        var draftChk = await Body<ProjectGateChecklistDto>(
            await leadStaff.PostAsync($"/api/v1/projects/{s.ProjectId}/review-gates/G1/checklist?templateId={masterTpl.Id}", null));

        // 1. Negative (DRAFT): Team student cannot read DRAFT checklist -> 403
        var stuDraft = await teamStudent.GetAsync($"/api/v1/projects/{s.ProjectId}/review-gates/G1/checklist");
        Assert.Equal(HttpStatusCode.Forbidden, stuDraft.StatusCode);

        // 2. Negative (IDOR on DRAFT & Diff): Unrelated student, unrelated lecturer, other-dept staff -> 403 SCOPE_ACCESS_DENIED
        foreach (var rogue in new[] { unrelatedStudent, unrelatedLecturer, otherStaffClient })
        {
            var r1 = await rogue.GetAsync($"/api/v1/projects/{s.ProjectId}/review-gates/G1/checklist");
            Assert.Equal(HttpStatusCode.Forbidden, r1.StatusCode);
            Assert.Equal(WorkflowErrorCodes.ScopeAccessDenied, await ReadProblemCode(r1));

            var rDiff = await rogue.GetAsync($"/api/v1/projects/{s.ProjectId}/checklists/{draftChk.Id}/diff");
            Assert.Equal(HttpStatusCode.Forbidden, rDiff.StatusCode);
            Assert.Equal(WorkflowErrorCodes.ScopeAccessDenied, await ReadProblemCode(rDiff));
        }

        // Team student cannot access diff preview either -> 403 SCOPE_ACCESS_DENIED
        var stuDiff = await teamStudent.GetAsync($"/api/v1/projects/{s.ProjectId}/checklists/{draftChk.Id}/diff");
        Assert.Equal(HttpStatusCode.Forbidden, stuDiff.StatusCode);
        Assert.Equal(WorkflowErrorCodes.ScopeAccessDenied, await ReadProblemCode(stuDiff));

        // Positive: Lead staff CAN access diff preview -> 200
        var staffDiff = await Body<ChecklistDiffPreviewDto>(await leadStaff.GetAsync($"/api/v1/projects/{s.ProjectId}/checklists/{draftChk.Id}/diff"));
        Assert.Equal(draftChk.Id, staffDiff.ChecklistId);

        // Publish + record advisory & reviewer scores
        await Body<ProjectGateChecklistDto>(await leadStaff.PostAsync($"/api/v1/projects/{s.ProjectId}/checklists/{draftChk.Id}/publish", null));
        await Body<ProjectGateChecklistDto>(await primarySup.PostAsJsonAsync($"/api/v1/projects/{s.ProjectId}/checklists/{draftChk.Id}/advisory",
            new SubmitAdvisoryEvaluationRequest([new("IT-01", "READY", "Secret advisor note")], "RECOMMEND_DEFENSE")));
        await Body<ProjectGateChecklistDto>(await activeReviewer.PostAsJsonAsync($"/api/v1/projects/{s.ProjectId}/checklists/{draftChk.Id}/reviewer-score",
            new SubmitReviewerEvaluationRequest([new("IT-01", 1.0m, "Secret reviewer note")], "RECOMMEND_DEFENSE")));

        // 3. Negative (IDOR on PUBLISHED): Unrelated student, unrelated lecturer, other-dept staff STILL get 403 SCOPE_ACCESS_DENIED
        foreach (var rogue in new[] { unrelatedStudent, unrelatedLecturer, otherStaffClient })
        {
            var rPub = await rogue.GetAsync($"/api/v1/projects/{s.ProjectId}/review-gates/G1/checklist");
            Assert.Equal(HttpStatusCode.Forbidden, rPub.StatusCode);
            Assert.Equal(WorkflowErrorCodes.ScopeAccessDenied, await ReadProblemCode(rPub));
        }

        // 4. Negative (Revoked Gate Reviewer): returns 403 ASSIGNMENT_REVOKED
        var revRes = await revokedReviewer.GetAsync($"/api/v1/projects/{s.ProjectId}/review-gates/G1/checklist");
        Assert.Equal(HttpStatusCode.Forbidden, revRes.StatusCode);
        Assert.Equal(WorkflowErrorCodes.AssignmentRevoked, await ReadProblemCode(revRes));

        // 5. Positive (N6 Masking for Team Student on PUBLISHED): 200 OK with scores/notes/recommendations completely masked
        var stuView = await Body<ProjectGateChecklistDto>(await teamStudent.GetAsync($"/api/v1/projects/{s.ProjectId}/review-gates/G1/checklist"));
        Assert.Null(stuView.ScoreSummary);
        Assert.Equal("PENDING", stuView.AdvisorRecommendation);
        Assert.Equal("PENDING", stuView.ReviewerRecommendation);
        Assert.Equal("PENDING_RECOMMENDATIONS", stuView.Review3ConsensusStatus);
        Assert.All(stuView.Items, item =>
        {
            Assert.Null(item.ReviewerScore);
            Assert.Null(item.ReviewerNotes);
            Assert.Null(item.AdvisoryNotes);
        });

        // 6. Positive: Lead Staff, Primary Supervisor, and Active Reviewer read full unmasked scores -> 200 OK
        foreach (var allowed in new[] { leadStaff, primarySup, activeReviewer })
        {
            var fullView = await Body<ProjectGateChecklistDto>(await allowed.GetAsync($"/api/v1/projects/{s.ProjectId}/review-gates/G1/checklist"));
            Assert.Equal(10.00m, fullView.ScoreSummary!.NormalizedScoreOutOf10);
            Assert.Equal("RECOMMEND_DEFENSE", fullView.ReviewerRecommendation);
            Assert.Contains(fullView.Items, i => i.ReviewerNotes == "Secret reviewer note");
        }
    }
}
