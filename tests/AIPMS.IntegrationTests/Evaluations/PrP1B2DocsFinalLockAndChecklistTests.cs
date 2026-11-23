using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.ProjectChecklists;
using AIPMS.Application.Features.ReviewGates;
using AIPMS.Domain.Teams;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.IntegrationTests.Evaluations;

public sealed class PrP1B2DocsFinalLockAndChecklistTests(EvaluationDraftDatabaseFixture database)
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
    public async Task Master_checklist_tailoring_normalized_scoring_role_split_each_student_waiver_and_2step_final_lock_work()
    {
        var s = await database.Seed(false);
        long itMajorId, mktMajorId, mktStudentId, mentorId, primarySupId, reviewerId, g3Id, mktReqId;

        await using (var db = database.CreateContext())
        {
            var project = await db.Projects.SingleAsync(p => p.Id == s.ProjectId);
            project.Status = "ACTIVE";
            project.TrackCode = AcademicTrackCodes.ItMkt;

            itMajorId = await db.ProjectMajors.Where(pm => pm.ProjectId == s.ProjectId).Select(pm => pm.MajorId).FirstAsync();
            var mktDeptId = (await db.Users.SingleAsync(u => u.Id == s.Scope.Users.OtherLecturer)).DepartmentId!.Value;
            var mktMajor = new M.Major { DepartmentId = mktDeptId, Code = "MKT_" + Guid.NewGuid().ToString("N")[..4], Name = "Marketing", IsActive = true };
            db.Majors.Add(mktMajor);
            await db.SaveChangesAsync();
            mktMajorId = mktMajor.Id;
            db.ProjectMajors.Add(new M.ProjectMajor { ProjectId = s.ProjectId, MajorId = mktMajorId });

            var studentRoleId = await db.Roles.Where(r => r.Code == "STUDENT").Select(r => r.Id).SingleAsync();
            var mktStudent = new M.User
            {
                Email = $"mkt_stu_{Guid.NewGuid():N}@e2e.invalid",
                FullName = "MKT Team Member",
                PasswordHash = "unused",
                Status = "ACTIVE",
                DepartmentId = mktDeptId,
                MajorId = mktMajorId,
                AcademicProfileStatus = "VERIFIED",
                UserRoleUsers = [new() { RoleId = studentRoleId }]
            };
            db.Users.Add(mktStudent);
            await db.SaveChangesAsync();
            mktStudentId = mktStudent.Id;

            // Ensure leader + MKT member on team
            var leaderMember = await db.TeamMembers.SingleAsync(m => m.TeamId == project.TeamId && m.UserId == s.Scope.Users.Student);
            leaderMember.IsLeader = true;
            db.TeamMembers.Add(new M.TeamMember
            {
                TeamId = project.TeamId,
                AcademicSemesterId = s.Scope.SemesterId,
                UserId = mktStudentId,
                IsLeader = false,
                JoinedAt = Now.AddDays(-15)
            });

            // Update frozen registration snapshot to include both IT leader and MKT member with complete IT_MKT scope
            var leadDeptId = (await db.Majors.SingleAsync(m => m.Id == itMajorId)).DepartmentId;
            var snap = await db.Set<ProjectRegistrationSnapshot>().SingleAsync(r => r.ProjectId == s.ProjectId);
            var reg = JsonSerializer.Deserialize<AIPMS.Application.Features.Projects.DTOs.RegistrationEvidence>(snap.SnapshotJson)!;
            snap.SnapshotJson = JsonSerializer.Serialize(reg with
            {
                Scope = reg.Scope with
                {
                    ProjectMode = "INTERDISCIPLINARY",
                    PrimaryMajorId = null,
                    LeadDepartmentId = leadDeptId,
                    TrackCode = AcademicTrackCodes.ItMkt,
                    HasIntegrationDeliverable = true,
                    Requirements =
                    [
                        new(itMajorId, 1, 4, "IT Engineering"),
                        new(mktMajorId, 1, 4, "Marketing Pilot")
                    ]
                },
                Members = [.. reg.Members, new(mktStudentId, "MKT Team Member", mktMajorId, false)],
                DepartmentIds = [leadDeptId, mktDeptId],
                MajorDepartmentIds = new Dictionary<long, long> { [itMajorId] = leadDeptId, [mktMajorId] = mktDeptId }
            });

            // Discipline Mentor for MKT major (OtherLecturer)
            var mentorProfile = new M.SupervisorProfile { UserId = s.Scope.Users.OtherLecturer };
            db.SupervisorProfiles.Add(mentorProfile);
            await db.SaveChangesAsync();
            var mentorReq = new M.SupervisorRequest
            {
                ProjectId = s.ProjectId,
                SupervisorProfileId = mentorProfile.Id,
                RequestedBy = s.Scope.Users.Student,
                Status = "ACCEPTED",
                RequestedAt = Now.AddDays(-20),
                RespondedAt = Now.AddDays(-19)
            };
            db.SupervisorRequests.Add(mentorReq);
            await db.SaveChangesAsync();
            db.SupervisorAssignments.Add(new M.SupervisorAssignment
            {
                ProjectId = s.ProjectId,
                SupervisorProfileId = mentorProfile.Id,
                SupervisorRequestId = mentorReq.Id,
                IsPrimary = false,
                AssignmentType = "DISCIPLINE_MENTOR",
                MajorId = mktMajorId,
                AssignedAt = Now.AddDays(-19)
            });

            primarySupId = await db.SupervisorAssignments.Where(a => a.ProjectId == s.ProjectId && a.IsPrimary)
                .Select(a => a.SupervisorProfile.UserId).SingleAsync();
            mentorId = s.Scope.Users.OtherLecturer;
            reviewerId = s.Scope.Users.Lecturer;

            // Gate G3 (blocksDefense = true) with MAJOR_SPECIFIC MKT req + EACH_STUDENT self-eval req
            var tpl = new ReviewGateTemplate
            {
                Code = "TPL-B2-" + Guid.NewGuid().ToString("N")[..6],
                Name = "B2 Gate Template",
                Mode = "INTERDISCIPLINARY",
                Major = "IT_MKT",
                Status = "PUBLISHED",
                Version = 1,
                CreatedBy = s.Scope.Users.Staff,
                CreatedAt = Now,
                UpdatedAt = Now
            };
            db.ReviewGateTemplates.Add(tpl);
            await db.SaveChangesAsync();

            var g3 = new ProjectGateInstance
            {
                ProjectId = s.ProjectId,
                TemplateId = tpl.Id,
                TemplateVersion = 1,
                Code = "G3",
                Sequence = 3,
                Title = "Review 3",
                Objective = "Pre-Defense Gate",
                Status = "PLANNED",
                ReviewEligibility = "READY",
                Required = true,
                BlockNextGate = false,
                BlocksDefense = true,
                WindowStart = Now.AddDays(-5),
                WindowEnd = Now.AddDays(5),
                CreatedAt = Now
            };
            db.ProjectGateInstances.Add(g3);
            await db.SaveChangesAsync();
            g3Id = g3.Id;

            var mktReq = new ProjectGateRequirement
            {
                GateInstanceId = g3.Id,
                Code = "MKT-PILOT",
                Title = "MKT Pilot Report",
                Type = "DOCUMENT",
                Scope = "MAJOR_SPECIFIC",
                MajorId = mktMajorId,
                Required = true,
                VerificationStatus = "PENDING",
                TargetCardinality = "MAJOR",
                DeliveryStage = "G3"
            };
            var eachStuReq = new ProjectGateRequirement
            {
                GateInstanceId = g3.Id,
                Code = "SELF-EVAL",
                Title = "Individual Self Evaluation",
                Type = "DOCUMENT",
                Scope = "INDIVIDUAL",
                Required = true,
                VerificationStatus = "PENDING",
                TargetCardinality = "EACH_STUDENT",
                DeliveryStage = "G3"
            };
            db.ProjectGateRequirements.AddRange(mktReq, eachStuReq);
            db.GateReviewerAssignments.Add(new GateReviewerAssignment
            {
                GateInstanceId = g3.Id,
                ReviewerId = reviewerId,
                Scope = "COMMON",
                Status = "PENDING_REVIEW"
            });
            await db.SaveChangesAsync();
            mktReqId = mktReq.Id;
        }

        using var app = new EvaluationFactory(database);
        using var leadStaff = app.CreateAuthenticatedClient(s.Scope.Users.Staff, roles: ["DEPARTMENT_STAFF"]);
        using var leader = app.CreateAuthenticatedClient(s.Scope.Users.Student, roles: ["STUDENT"]);
        using var mktMember = app.CreateAuthenticatedClient(mktStudentId, roles: ["STUDENT"]);
        using var primarySup = app.CreateAuthenticatedClient(primarySupId, roles: ["LECTURER"]);
        using var mktMentor = app.CreateAuthenticatedClient(mentorId, roles: ["LECTURER"]);
        using var reviewer = app.CreateAuthenticatedClient(reviewerId, roles: ["LECTURER"]);

        // 1. Expand EACH_STUDENT across active roster (creates 2 per-student slots)
        var expanded = await Body<ExpandStudentRequirementsResultDto>(
            await leadStaff.PostAsync($"/api/v1/projects/{s.ProjectId}/review-gates/G3/expand-student-requirements", null));
        Assert.Equal(2, expanded.CreatedRequirementCount);

        // 2. Scoped MAJOR_SPECIFIC draft upload: MKT member uploads MKT requirement successfully!
        string gateToken;
        await using (var db = database.CreateContext())
            gateToken = (await db.ProjectGateInstances.SingleAsync(g => g.Id == g3Id)).ConcurrencyToken.ToString("N");
        var mktUploadOk = await mktMember.PostAsJsonAsync($"/api/v1/projects/{s.ProjectId}/review-gates/G3/evidence",
            new SubmitReviewGateEvidenceRequest(mktReqId.ToString(), "mkt-pilot.pdf", "MKT draft", 0, gateToken));
        Assert.True(mktUploadOk.IsSuccessStatusCode, await mktUploadOk.Content.ReadAsStringAsync());

        // 3. Mid-semester withdrawal: Lead Staff withdraws MKT member => unsubmitted EACH_STUDENT slot turns WAIVED_WITHDRAWN
        var withdrawRes = await Body<WithdrawRosterMemberResultDto>(await leadStaff.PostAsJsonAsync(
            $"/api/v1/projects/{s.ProjectId}/roster/withdraw-member",
            new WithdrawRosterMemberRequest(mktStudentId, "Medical leave approved by Academic Affairs")));
        Assert.True(withdrawRes.WaivedGateRequirements >= 1);

        // 4. Master Review Checklist Layer 1: Create + Publish Master Template
        var masterTpl = await Body<MasterChecklistTemplateDto>(await leadStaff.PostAsJsonAsync("/api/v1/checklist-templates",
            new CreateMasterChecklistTemplateRequest(s.Scope.SemesterId, AcademicTrackCodes.ItMkt, "CHK-R3", "Master Review 3 Checklist", s.Scope.Users.DepartmentId,
            [
                new("IT-01", "Software Core", "IT", itMajorId, "NONE", "Working CI/CD & UAT tests?", "Mandatory", 1.0m),
                new("MKT-01", "Marketing Pilot", "MKT", mktMajorId, "NONE", "Pilot results with Attribution Limits?", "Mandatory", 1.0m),
                new("AI-01", "AI Extension", "IT", itMajorId, "AI", "Model evaluation benchmark?", "Conditional", 0.5m)
            ])));
        await Body<MasterChecklistTemplateDto>(await leadStaff.PostAsync($"/api/v1/checklist-templates/{masterTpl.Id}/publish", null));

        // 5. Instantiate Project Checklist for G3
        var projChk = await Body<ProjectGateChecklistDto>(
            await leadStaff.PostAsync($"/api/v1/projects/{s.ProjectId}/review-gates/G3/checklist?templateId={masterTpl.Id}", null));
        Assert.Equal("DRAFT", projChk.Status);

        // 6. Negative: Marking Mandatory item N/A => 422; altering core Weight => 422
        var illegalNa = await leadStaff.PutAsJsonAsync($"/api/v1/projects/{s.ProjectId}/checklists/{projChk.Id}/tailor",
            new TailorProjectChecklistRequest(projChk.ConcurrencyToken, [new("IT-01", false, "Skip core", null, null, null, null)]));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, illegalNa.StatusCode);

        var illegalWeight = await leadStaff.PutAsJsonAsync($"/api/v1/projects/{s.ProjectId}/checklists/{projChk.Id}/tailor",
            new TailorProjectChecklistRequest(projChk.ConcurrencyToken, [new("IT-01", true, null, "Focus", null, null, null, Weight: 2.0m)]));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, illegalWeight.StatusCode);

        // 7. Positive: Tailor Conditional AI-01 as N/A + add tailored checkpoints/evidence -> preview Diff -> Publish
        var tailored = await Body<ProjectGateChecklistDto>(await leadStaff.PutAsJsonAsync($"/api/v1/projects/{s.ProjectId}/checklists/{projChk.Id}/tailor",
            new TailorProjectChecklistRequest(projChk.ConcurrencyToken,
            [
                new("IT-01", true, null, "Check Swagger + integration tests", "CI log screenshot", "Verify auth edge cases", "Proposal §3"),
                new("AI-01", false, "Project does not use ML/AI modules", null, null, null, "Proposal §1")
            ])));
        var diff = await Body<ChecklistDiffPreviewDto>(await leadStaff.GetAsync($"/api/v1/projects/{s.ProjectId}/checklists/{projChk.Id}/diff"));
        Assert.Equal(1, diff.NaItems);
        Assert.Equal(2, diff.ApplicableItems);

        var publishedChk = await Body<ProjectGateChecklistDto>(await leadStaff.PostAsync($"/api/v1/projects/{s.ProjectId}/checklists/{projChk.Id}/publish", null));
        Assert.Equal("PUBLISHED", publishedChk.Status);

        // 8. Negative: Editing PUBLISHED checklist => 409 IMMUTABLE_VERSION
        var editPubChk = await leadStaff.PutAsJsonAsync($"/api/v1/projects/{s.ProjectId}/checklists/{projChk.Id}/tailor",
            new TailorProjectChecklistRequest(publishedChk.ConcurrencyToken, [new("IT-01", true, null, "Late edit", null, null, null)]));
        Assert.Equal(HttpStatusCode.Conflict, editPubChk.StatusCode);
        Assert.Equal(WorkflowErrorCodes.ImmutableVersion, await ReadProblemCode(editPubChk));

        // 9. Role separation & D17:
        // - MKT Mentor advising IT item => 403
        var mentorCrossMajor = await mktMentor.PostAsJsonAsync($"/api/v1/projects/{s.ProjectId}/checklists/{projChk.Id}/advisory",
            new SubmitAdvisoryEvaluationRequest([new("IT-01", "ON_TRACK", "Cross-major attempt")]));
        Assert.Equal(HttpStatusCode.Forbidden, mentorCrossMajor.StatusCode);

        // - MKT Mentor trying to write independent reviewer column => 403
        var mentorWriteReviewer = await mktMentor.PostAsJsonAsync($"/api/v1/projects/{s.ProjectId}/checklists/{projChk.Id}/reviewer-score",
            new SubmitReviewerEvaluationRequest([new("MKT-01", 1.0m, "Mentor overwriting reviewer")]));
        Assert.Equal(HttpStatusCode.Forbidden, mentorWriteReviewer.StatusCode);

        // - Primary Supervisor trying to score independent reviewer column => 409 EVALUATOR_ROLE_CONFLICT (D17!)
        var supWriteReviewer = await primarySup.PostAsJsonAsync($"/api/v1/projects/{s.ProjectId}/checklists/{projChk.Id}/reviewer-score",
            new SubmitReviewerEvaluationRequest([new("IT-01", 1.0m, "COI violation")]));
        Assert.Equal(HttpStatusCode.Conflict, supWriteReviewer.StatusCode);
        Assert.Equal(WorkflowErrorCodes.EvaluatorRoleConflict, await ReadProblemCode(supWriteReviewer));

        // 10. Normalized 0-10 scoring excluding N/A & Review 3 PENDING_RECOMMENDATIONS -> AGREE:
        // Primary Supervisor recommends defense (Reviewer still PENDING => MUST stay PENDING_RECOMMENDATIONS!)
        var advOnly = await Body<ProjectGateChecklistDto>(await primarySup.PostAsJsonAsync($"/api/v1/projects/{s.ProjectId}/checklists/{projChk.Id}/advisory",
            new SubmitAdvisoryEvaluationRequest([new("IT-01", "READY", "Looks solid")], "RECOMMEND_DEFENSE")));
        Assert.Equal("PENDING_RECOMMENDATIONS", advOnly.Review3ConsensusStatus);

        // Independent Reviewer scores IT-01=1.0 (w=1.0), MKT-01=0.5 (w=1.0); AI-01 is N/A (excluded from denominator!)
        // Expected normalized score: ((1.0*1.0 + 0.5*1.0) / (1.0 + 1.0)) * 10 = 7.50 => "Good"
        var revScored = await Body<ProjectGateChecklistDto>(await reviewer.PostAsJsonAsync($"/api/v1/projects/{s.ProjectId}/checklists/{projChk.Id}/reviewer-score",
            new SubmitReviewerEvaluationRequest([new("IT-01", 1.0m, "All tests pass"), new("MKT-01", 0.5m, "Needs stronger limits")], "RECOMMEND_DEFENSE")));
        Assert.Equal(7.50m, revScored.ScoreSummary!.NormalizedScoreOutOf10);
        Assert.Equal("Good", revScored.ScoreSummary.RatingBand);
        Assert.Equal("AGREE", revScored.Review3ConsensusStatus);

        // 11. 2-Step Final Lock:
        // - Blocked with 409 GATE_3_NOT_PASSED while G3 is not PASSED
        var lockBeforeG3 = await leader.PostAsync($"/api/v1/projects/{s.ProjectId}/final-submission/request-lock", null);
        Assert.Equal(HttpStatusCode.Conflict, lockBeforeG3.StatusCode);
        Assert.Equal(WorkflowErrorCodes.Gate3NotPassed, await ReadProblemCode(lockBeforeG3));

        // Mark G3 PASSED
        await using (var db = database.CreateContext())
        {
            var g3 = await db.ProjectGateInstances.SingleAsync(g => g.Id == g3Id);
            g3.Status = "PASSED";
            await db.SaveChangesAsync();
        }

        // Step 1: Leader requests lock => PENDING_SUPERVISOR_LOCK
        var reqLock = await Body<PreDefenseLockStatusDto>(await leader.PostAsync($"/api/v1/projects/{s.ProjectId}/final-submission/request-lock", null));
        Assert.Equal("PENDING_SUPERVISOR_LOCK", reqLock.LockWorkflowStatus);

        // Negative: Leader tries to bypass supervisor and lock directly => 403
        var leaderBypass = await leader.PostAsJsonAsync($"/api/v1/projects/{s.ProjectId}/final-submission/supervisor-lock",
            new SupervisorFinalLockDecisionRequest("LOCK", "Trying to self-lock"));
        Assert.Equal(HttpStatusCode.Forbidden, leaderBypass.StatusCode);

        // Step 2: Primary Supervisor signs LOCK => freezes Final + Cold SHA-256 manifest atomically, project becomes FINAL_SUBMISSION without any DefenseSession!
        var locked = await Body<PreDefenseLockStatusDto>(await primarySup.PostAsJsonAsync($"/api/v1/projects/{s.ProjectId}/final-submission/supervisor-lock",
            new SupervisorFinalLockDecisionRequest("LOCK", "Verified final deliverables and 20% scores")));
        Assert.Equal("LOCKED", locked.LockWorkflowStatus);
        Assert.Equal("FINAL_SUBMISSION", locked.ProjectStatus);
        Assert.Equal(primarySupId, locked.LockedBySupervisorId);
        Assert.False(string.IsNullOrWhiteSpace(locked.ColdManifestSha256));
    }
}
