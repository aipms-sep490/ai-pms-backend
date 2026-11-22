using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.ProjectHub;
using AIPMS.Application.Features.ReviewGates;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.IntegrationTests.Evaluations;

public sealed class PrP2CAllInOneHubTests(EvaluationDraftDatabaseFixture database)
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
    public async Task All_in_one_hub_spec_vault_chat_acl_anchored_comments_heatmap_and_health_pulse_work()
    {
        var s = await database.Seed(false);
        long deliverableId, mktMemberId, otherStaffId, milestoneId, meetingId, evidenceId, rubricCriterionId, emptyProjectId;
        string gateToken;

        await using (var db = database.CreateContext())
        {
            var project = await db.Projects.SingleAsync(p => p.Id == s.ProjectId);
            project.Status = "ACTIVE";
            project.TrackCode = "IT_MKT";

            var itMajorId = await db.ProjectMajors.Where(pm => pm.ProjectId == s.ProjectId).Select(pm => pm.MajorId).FirstAsync();
            var itMajor = await db.Majors.SingleAsync(m => m.Id == itMajorId);
            itMajor.Code = "SE_IT";

            // Create a Marketing major in the SAME department so OtherStaff (different dept) remains strictly out-of-scope
            var mktMajor = new M.Major
            {
                DepartmentId = s.Scope.Users.DepartmentId,
                Code = "MKT_" + Guid.NewGuid().ToString("N")[..4],
                Name = "Digital Marketing",
                IsActive = true
            };
            db.Majors.Add(mktMajor);
            await db.SaveChangesAsync();
            db.ProjectMajors.Add(new M.ProjectMajor { ProjectId = s.ProjectId, MajorId = mktMajor.Id });

            var studentRoleId = await db.Roles.Where(r => r.Code == "STUDENT").Select(r => r.Id).SingleAsync();
            var mktUser = new M.User
            {
                Email = $"hub_mkt_{Guid.NewGuid():N}@e2e.invalid",
                FullName = "Silent Marketing Member",
                PasswordHash = "unused",
                Status = "ACTIVE",
                DepartmentId = s.Scope.Users.DepartmentId,
                MajorId = mktMajor.Id,
                AcademicProfileStatus = "VERIFIED",
                UserRoleUsers = [new() { RoleId = studentRoleId }]
            };
            db.Users.Add(mktUser);
            await db.SaveChangesAsync();
            mktMemberId = mktUser.Id;
            db.TeamMembers.Add(new M.TeamMember
            {
                TeamId = project.TeamId,
                AcademicSemesterId = s.Scope.SemesterId,
                UserId = mktMemberId,
                IsLeader = false,
                JoinedAt = Now.AddDays(-20)
            });

            var deliverable = new M.Deliverable
            {
                ProjectId = s.ProjectId,
                Title = "System Architecture Spec",
                Status = "OPEN",
                CreatedBy = s.Scope.Users.Student
            };
            db.Deliverables.Add(deliverable);

            var milestone = new M.Milestone
            {
                ProjectId = s.ProjectId,
                Title = "Sprint 1",
                Status = "IN_PROGRESS",
                DueDate = DateOnly.FromDateTime(Now.AddDays(10)),
                CreatedBy = s.Scope.Users.Student
            };
            db.Milestones.Add(milestone);

            var meeting = new M.Meeting
            {
                ProjectId = s.ProjectId,
                Title = "Weekly Sync",
                StartAt = Now.AddDays(-1),
                EndAt = Now.AddDays(-1).AddHours(1),
                Status = "COMPLETED",
                CreatedBy = s.Scope.Users.Student
            };
            db.Meetings.Add(meeting);

            var tpl = new ReviewGateTemplate
            {
                ProjectPeriodId = s.PeriodId,
                Code = "HUB_TPL_" + Guid.NewGuid().ToString("N")[..5],
                Name = "Hub Gate Template",
                Status = "PUBLISHED",
                CreatedBy = s.Scope.Users.Staff,
                CreatedAt = Now,
                UpdatedAt = Now
            };
            db.Set<ReviewGateTemplate>().Add(tpl);
            await db.SaveChangesAsync();

            var gate = new ProjectGateInstance
            {
                ProjectId = s.ProjectId,
                TemplateId = tpl.Id,
                TemplateVersion = 1,
                Code = "G1",
                Sequence = 1,
                Title = "Review 1",
                Objective = "Baseline check",
                Status = "IN_PROGRESS",
                ReviewEligibility = "READY",
                Required = true,
                ConcurrencyToken = Guid.NewGuid(),
                CreatedAt = Now,
                Requirements =
                [
                    new ProjectGateRequirement
                    {
                        Code = "DOC-SRS",
                        Title = "SRS Document",
                        Type = "DOCUMENT",
                        Scope = "COMMON",
                        Required = true,
                        VerificationStatus = "PENDING"
                    }
                ]
            };
            db.ProjectGateInstances.Add(gate);
            await db.SaveChangesAsync();

            var req = gate.Requirements.Single();
            var evidence = new GateEvidence
            {
                ProjectId = s.ProjectId,
                RequirementId = req.Id,
                Version = 1,
                ArtifactName = "srs-v1.pdf",
                ContentType = "application/pdf",
                SizeBytes = 1024,
                Sha256 = new string('a', 64),
                StorageKey = "hub/srs-v1.pdf",
                SubmittedBy = s.Scope.Users.Student,
                SubmittedAt = Now.AddHours(-2),
                Status = "SUBMITTED",
                ScanStatus = "READY",
                ConcurrencyToken = Guid.NewGuid()
            };
            db.Set<GateEvidence>().Add(evidence);

            // Empty project to prove Health Pulse returns INSUFFICIENT_DATA instead of guessing GREEN
            var emptyProj = new M.Project
            {
                Code = "HUB_EMPTY_" + Guid.NewGuid().ToString("N")[..5],
                Title = "Fresh Empty Project",
                CreatedBy = s.Scope.Users.Student,
                Status = "ACTIVE",
                TrackCode = "IT_ONLY",
                Team = new()
                {
                    Code = "T_EMPTY_" + Guid.NewGuid().ToString("N")[..5],
                    Name = "Empty Team",
                    CreatedBy = s.Scope.Users.Student,
                    AcademicSemesterId = s.Scope.SemesterId,
                    Status = "LOCKED"
                },
                ProjectMajors = [new() { MajorId = itMajorId }]
            };
            db.Projects.Add(emptyProj);

            var staffRoleId = await db.Roles.Where(r => r.Code == "DEPARTMENT_STAFF").Select(r => r.Id).SingleAsync();
            var otherDeptId = (await db.Users.SingleAsync(u => u.Id == s.Scope.Users.OtherLecturer)).DepartmentId!.Value;
            var otherStaffUser = new M.User
            {
                Email = $"hub_ostaff_{Guid.NewGuid():N}@e2e.invalid",
                FullName = "Out Of Scope Staff",
                PasswordHash = "unused",
                Status = "ACTIVE",
                DepartmentId = otherDeptId,
                UserRoleUsers = [new() { RoleId = staffRoleId }]
            };
            db.Users.Add(otherStaffUser);

            rubricCriterionId = await db.RubricCriteria.Where(rc => rc.RubricId == s.RubricId).Select(rc => rc.Id).FirstAsync();
            await db.SaveChangesAsync();

            otherStaffId = otherStaffUser.Id;

            deliverableId = deliverable.Id;
            milestoneId = milestone.Id;
            meetingId = meeting.Id;
            evidenceId = evidence.Id;
            emptyProjectId = emptyProj.Id;
            gateToken = gate.ConcurrencyToken.ToString("N");
        }

        using var app = new EvaluationFactory(database);
        using var leader = app.CreateAuthenticatedClient(s.Scope.Users.Student, roles: ["STUDENT"]);
        using var mktMember = app.CreateAuthenticatedClient(mktMemberId, roles: ["STUDENT"]);
        using var staff = app.CreateAuthenticatedClient(s.Scope.Users.Staff, roles: ["DEPARTMENT_STAFF"]);
        using var otherStaff = app.CreateAuthenticatedClient(otherStaffId, roles: ["DEPARTMENT_STAFF"]);

        // 1. Spec Vault: v1 ok, v2 changelog < 10 chars -> 422, v2 valid changelog -> 201, compare -> 200, pinned resources CRUD
        var v1 = await Body<SpecVersionDto>(await leader.PostAsJsonAsync(
            $"/api/v1/projects/{s.ProjectId}/hub/deliverables/{deliverableId}/versions",
            new CreateSpecVersionRequest("Initial architecture", null)));
        Assert.Equal(1, v1.VersionNumber);

        var badV2 = await leader.PostAsJsonAsync(
            $"/api/v1/projects/{s.ProjectId}/hub/deliverables/{deliverableId}/versions",
            new CreateSpecVersionRequest("Patch", "too short"));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, badV2.StatusCode);

        var v2 = await Body<SpecVersionDto>(await leader.PostAsJsonAsync(
            $"/api/v1/projects/{s.ProjectId}/hub/deliverables/{deliverableId}/versions",
            new CreateSpecVersionRequest("Revised after G1", "Updated ERD schema and REST API contract details")));
        Assert.Equal(2, v2.VersionNumber);

        var diff = await Body<SpecVersionCompareDto>(await leader.GetAsync(
            $"/api/v1/projects/{s.ProjectId}/hub/deliverables/{deliverableId}/compare?fromVersion=1&toVersion=2"));
        Assert.Equal(1, diff.FromVersion.VersionNumber);
        Assert.Equal(2, diff.ToVersion.VersionNumber);

        var pinned = await Body<PinnedResourceDto>(await leader.PostAsJsonAsync(
            $"/api/v1/projects/{s.ProjectId}/hub/pinned-resources",
            new CreatePinnedResourceRequest("FIGMA", "UI Prototype V4", "https://figma.com/file/demo")));
        Assert.Single(await Body<PinnedResourceDto[]>(await leader.GetAsync($"/api/v1/projects/{s.ProjectId}/hub/pinned-resources")));
        Assert.Equal(HttpStatusCode.NoContent, (await leader.DeleteAsync($"/api/v1/projects/{s.ProjectId}/hub/pinned-resources/{pinned.Id}")).StatusCode);

        // 2. Context Chat ACL & 1-Click Chat-to-Task / Chat-to-Decision Idempotency
        var mktForbiddenIt = await mktMember.GetAsync($"/api/v1/projects/{s.ProjectId}/hub/channels/IT");
        Assert.Equal(HttpStatusCode.Forbidden, mktForbiddenIt.StatusCode);
        Assert.Equal(WorkflowErrorCodes.ScopeAccessDenied, await ReadProblemCode(mktForbiddenIt));

        var itMsg = await Body<HubChannelMessageDto>(await leader.PostAsJsonAsync(
            $"/api/v1/projects/{s.ProjectId}/hub/channels/IT/messages",
            new PostChannelMessageRequest("Implement OAuth2 PKCE flow and log architecture decision")));

        var task1 = await Body<ChatConvertedTaskDto>(await leader.PostAsJsonAsync(
            $"/api/v1/projects/{s.ProjectId}/hub/chat/messages/{itMsg.Id}/to-task",
            new ConvertMessageToTaskRequest(milestoneId, "Implement OAuth2 PKCE flow")));
        Assert.False(task1.AlreadyExisted);

        // Replay must be idempotent (no duplicate task)
        var taskReplay = await Body<ChatConvertedTaskDto>(await leader.PostAsJsonAsync(
            $"/api/v1/projects/{s.ProjectId}/hub/chat/messages/{itMsg.Id}/to-task",
            new ConvertMessageToTaskRequest(milestoneId, "Implement OAuth2 PKCE flow")));
        Assert.True(taskReplay.AlreadyExisted);
        Assert.Equal(task1.TaskId, taskReplay.TaskId);

        var dec1 = await Body<ChatConvertedDecisionDto>(await leader.PostAsJsonAsync(
            $"/api/v1/projects/{s.ProjectId}/hub/chat/messages/{itMsg.Id}/to-decision",
            new ConvertMessageToDecisionRequest(meetingId, "Adopted OAuth2 PKCE")));
        Assert.False(dec1.AlreadyExisted);
        var decReplay = await Body<ChatConvertedDecisionDto>(await leader.PostAsJsonAsync(
            $"/api/v1/projects/{s.ProjectId}/hub/chat/messages/{itMsg.Id}/to-decision",
            new ConvertMessageToDecisionRequest(meetingId, "Adopted OAuth2 PKCE")));
        Assert.True(decReplay.AlreadyExisted);
        Assert.Equal(dec1.DecisionId, decReplay.DecisionId);

        // 3. Anchored Comments: CRITICAL OPEN blocks Gate SubmitForReview with 422 UNRESOLVED_CRITICAL_COMMENTS; RESOLVED unblocks
        var comment = await Body<AnchoredCommentDto>(await staff.PostAsJsonAsync(
            $"/api/v1/projects/{s.ProjectId}/hub/gates/G1/anchored-comments",
            new CreateAnchoredCommentRequest(evidenceId, "Page 4 · Section 2.1", "CRITICAL", "Missing sequence diagram for payment rollback")));
        Assert.Equal("OPEN", comment.Status);

        var blockedSubmitReq = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/projects/{s.ProjectId}/review-gates/G1/submit-for-review")
        {
            Content = JsonContent.Create(new SubmitReviewGateForReviewRequest("Resubmitting G1", gateToken, "idem-hub-1"))
        };
        blockedSubmitReq.Headers.Add("Idempotency-Key", "idem-hub-1");
        var blockedSubmit = await leader.SendAsync(blockedSubmitReq);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, blockedSubmit.StatusCode);
        Assert.Equal(WorkflowErrorCodes.UnresolvedCriticalComments, await ReadProblemCode(blockedSubmit));

        var resolved = await Body<AnchoredCommentDto>(await leader.PostAsync(
            $"/api/v1/projects/{s.ProjectId}/hub/gates/G1/anchored-comments/{comment.Id}/resolve", null));
        Assert.Equal("RESOLVED", resolved.Status);

        var okSubmitReq = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/projects/{s.ProjectId}/review-gates/G1/submit-for-review")
        {
            Content = JsonContent.Create(new SubmitReviewGateForReviewRequest("Resubmitting after resolve", gateToken, "idem-hub-2"))
        };
        okSubmitReq.Headers.Add("Idempotency-Key", "idem-hub-2");
        var okSubmit = await leader.SendAsync(okSubmitReq);
        Assert.Equal(HttpStatusCode.OK, okSubmit.StatusCode);

        // 4. Internal Contribution Heatmap + Silent Member (> 10d) + Evidence-to-Rubric Trace (NO Git sync!)
        var heatmap = await Body<InternalContributionHeatmapDto>(await leader.GetAsync(
            $"/api/v1/projects/{s.ProjectId}/hub/contribution-heatmap?weeks=4"));
        Assert.Contains(heatmap.SilentMemberIds, id => id == mktMemberId);
        Assert.DoesNotContain(heatmap.SilentMemberIds, id => id == s.Scope.Users.Student);

        var link = await Body<EvidenceRubricLinkDto>(await leader.PostAsJsonAsync(
            $"/api/v1/projects/{s.ProjectId}/hub/evidence-rubric-links",
            new LinkEvidenceToRubricRequest(evidenceId, rubricCriterionId)));
        Assert.Equal(evidenceId, link.EvidenceId);
        Assert.Single(await Body<EvidenceRubricLinkDto[]>(await leader.GetAsync($"/api/v1/projects/{s.ProjectId}/hub/evidence-rubric-links")));

        // 5. Rule-based Health Pulse & Scoped Audit Timeline
        var emptyPulse = await Body<ProjectHealthPulseDto>(await staff.GetAsync($"/api/v1/projects/{emptyProjectId}/hub/health-pulse"));
        Assert.Equal("INSUFFICIENT_DATA", emptyPulse.Status);

        var activePulse = await Body<ProjectHealthPulseDto>(await staff.GetAsync($"/api/v1/projects/{s.ProjectId}/hub/health-pulse"));
        Assert.NotEqual("INSUFFICIENT_DATA", activePulse.Status);
        Assert.Equal(100m, activePulse.Signals.EvidenceReadyPercent);

        var timeline = await Body<ProjectAuditTimelineEntryDto[]>(await staff.GetAsync($"/api/v1/projects/{s.ProjectId}/hub/audit-timeline"));
        Assert.NotEmpty(timeline);

        // Negative scope checks: out-of-scope staff and student receive 403 on health-pulse and audit-timeline
        Assert.Equal(HttpStatusCode.Forbidden, (await otherStaff.GetAsync($"/api/v1/projects/{s.ProjectId}/hub/health-pulse")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await otherStaff.GetAsync($"/api/v1/projects/{s.ProjectId}/hub/audit-timeline")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await leader.GetAsync($"/api/v1/projects/{s.ProjectId}/hub/health-pulse")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await leader.GetAsync($"/api/v1/projects/{s.ProjectId}/hub/audit-timeline")).StatusCode);
    }
}
