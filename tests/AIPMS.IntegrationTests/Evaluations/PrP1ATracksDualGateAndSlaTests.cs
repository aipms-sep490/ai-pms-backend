using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.ReviewGates;
using AIPMS.Domain.Teams;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.IntegrationTests.Evaluations;

public sealed class PrP1ATracksDualGateAndSlaTests(EvaluationDraftDatabaseFixture database)
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
    public async Task Dual_dept_per_round_gate_consensus_consolidated_revision_lead_override_and_sla_dedup_work()
    {
        var s = await database.Seed(false);
        long itMajorId, mktMajorId, mktDeptId, mktStaffId, g1Id;

        await using (var db = database.CreateContext())
        {
            itMajorId = await db.ProjectMajors.Where(pm => pm.ProjectId == s.ProjectId).Select(pm => pm.MajorId).FirstAsync();
            var leadDeptId = (await db.Majors.SingleAsync(m => m.Id == itMajorId)).DepartmentId;
            mktDeptId = (await db.Users.SingleAsync(u => u.Id == s.Scope.Users.OtherLecturer)).DepartmentId!.Value;

            var mktMajor = new M.Major { DepartmentId = mktDeptId, Code = "MKT_" + Guid.NewGuid().ToString("N")[..4], Name = "Marketing", IsActive = true };
            db.Majors.Add(mktMajor);
            await db.SaveChangesAsync();
            mktMajorId = mktMajor.Id;

            var staffRoleId = await db.Roles.Where(r => r.Code == "DEPARTMENT_STAFF").Select(r => r.Id).SingleAsync();
            var mktStaff = new M.User
            {
                Email = $"mkt_staff_{Guid.NewGuid():N}@e2e.invalid",
                FullName = "MKT Dept Staff",
                PasswordHash = "unused",
                Status = "ACTIVE",
                DepartmentId = mktDeptId,
                UserRoleUsers = [new() { RoleId = staffRoleId }]
            };
            db.Users.Add(mktStaff);
            db.ProjectMajors.Add(new M.ProjectMajor { ProjectId = s.ProjectId, MajorId = mktMajorId });
            await db.SaveChangesAsync();
            mktStaffId = mktStaff.Id;

            // Update frozen snapshot to IT_MKT interdisciplinary with both majors & departments
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
                        new(mktMajorId, 1, 4, "Marketing Strategy & Pilot")
                    ]
                },
                DepartmentIds = [leadDeptId, mktDeptId],
                MajorDepartmentIds = new Dictionary<long, long> { [itMajorId] = leadDeptId, [mktMajorId] = mktDeptId }
            });

            var tpl = new ReviewGateTemplate
            {
                Code = "TPL-DUAL-" + Guid.NewGuid().ToString("N")[..6],
                Name = "Dual Dept Template",
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

            var g1 = new ProjectGateInstance
            {
                ProjectId = s.ProjectId, TemplateId = tpl.Id, TemplateVersion = 1,
                Code = "G1", Sequence = 1, Title = "Gate 1", Objective = "G1",
                Status = "SUBMITTED", ReviewEligibility = "READY", Required = true, BlockNextGate = true, CreatedAt = Now
            };
            var g2 = new ProjectGateInstance
            {
                ProjectId = s.ProjectId, TemplateId = tpl.Id, TemplateVersion = 1,
                Code = "G2", Sequence = 2, Title = "Gate 2", Objective = "G2",
                Status = "PLANNED", ReviewEligibility = "BLOCKED", Required = true, BlockNextGate = true, CreatedAt = Now
            };
            db.ProjectGateInstances.AddRange(g1, g2);
            await db.SaveChangesAsync();
            g1Id = g1.Id;

            // Backdate submitted package 75h to test 48h reminder + 72h escalation dedup
            db.Set<ReviewGatePackage>().Add(new ReviewGatePackage
            {
                ProjectId = s.ProjectId,
                GateInstanceId = g1.Id,
                RoundNumber = 1,
                IdempotencyKey = "dual-r1",
                RequestHash = new string('b', 64),
                Notes = "Round 1 submitted",
                SubmittedBy = s.Scope.Users.Student,
                SubmittedAt = Now.AddHours(-75)
            });
            await db.SaveChangesAsync();
        }

        using var app = new EvaluationFactory(database);
        using var leadStaff = app.CreateAuthenticatedClient(s.Scope.Users.Staff, roles: ["DEPARTMENT_STAFF"]);
        using var mktStaffClient = app.CreateAuthenticatedClient(mktStaffId, roles: ["DEPARTMENT_STAFF"]);

        // 1. SLA Sweep 48h/72h Dedup: First run sends reminders & escalations; second run sends 0 duplicates
        var sweep1 = await Body<SlaSweepResultDto>(await leadStaff.PostAsync("/api/v1/departments/sla/sweep", null));
        Assert.True(sweep1.Reminders48hSent >= 1);
        Assert.True(sweep1.Escalations72hSent >= 1);
        var sweep2 = await Body<SlaSweepResultDto>(await leadStaff.PostAsync("/api/v1/departments/sla/sweep", null));
        Assert.Equal(0, sweep2.Reminders48hSent);
        Assert.Equal(0, sweep2.Escalations72hSent);

        // 2. Stale round rejected with 409 STALE_REVIEW_ROUND
        var staleRoundRes = await leadStaff.PostAsJsonAsync($"/api/v1/projects/{s.ProjectId}/review-gates/G1/round-scope-decisions",
            new RecordRoundScopeDecisionRequest(itMajorId.ToString(), 99, "PASSED", "Wrong round"));
        Assert.Equal(HttpStatusCode.Conflict, staleRoundRes.StatusCode);
        Assert.Equal(WorkflowErrorCodes.StaleReviewRound, await ReadProblemCode(staleRoundRes));

        // 3. Consolidated Revision: Both IT and MKT return REVISION_REQUIRED => consolidated into 1 unified feedback
        var revIt = await Body<GateRoundConsensusDto>(await leadStaff.PostAsJsonAsync(
            $"/api/v1/projects/{s.ProjectId}/review-gates/G1/round-scope-decisions",
            new RecordRoundScopeDecisionRequest(itMajorId.ToString(), 1, "REVISION_REQUIRED", "Bổ sung sơ đồ ERD và INT-01 API")));
        Assert.Equal("REVISION_REQUIRED", revIt.GateStatus);

        var revMkt = await Body<GateRoundConsensusDto>(await mktStaffClient.PostAsJsonAsync(
            $"/api/v1/projects/{s.ProjectId}/review-gates/G1/round-scope-decisions",
            new RecordRoundScopeDecisionRequest(mktMajorId.ToString(), 1, "REVISION_REQUIRED", "Nêu rõ Attribution Limits cho Pilot")));
        Assert.Equal("REVISION_REQUIRED", revMkt.GateStatus);
        Assert.Contains("ERD", revMkt.ConsolidatedRevisionFeedback);
        Assert.Contains("Attribution Limits", revMkt.ConsolidatedRevisionFeedback);

        // 4. Negative: ParticipatingScope (MKT) cannot override LeadScope (IT) => 403
        var illegalOverride = await mktStaffClient.PostAsJsonAsync(
            $"/api/v1/projects/{s.ProjectId}/review-gates/G1/round-scope-decisions",
            new RecordRoundScopeDecisionRequest(itMajorId.ToString(), 1, "OVERRIDDEN_PASS", "MKT trying to override IT lead scope illegally", IsLeadOverride: true));
        Assert.Equal(HttpStatusCode.Forbidden, illegalOverride.StatusCode);

        // 4b. Negative (TK-W2-06): Gate 1–3 PASSED requires both assigned reviewers to pass first -> 409 GATE_REVIEW_PANEL_REQUIRED
        var panelMissing = await leadStaff.PostAsJsonAsync(
            $"/api/v1/projects/{s.ProjectId}/review-gates/G1/round-scope-decisions",
            new RecordRoundScopeDecisionRequest(itMajorId.ToString(), 1, "PASSED", "Attempting pass before reviewer panel"));
        Assert.Equal(HttpStatusCode.Conflict, panelMissing.StatusCode);
        Assert.Equal(WorkflowErrorCodes.GateReviewPanelRequired, await ReadProblemCode(panelMissing));

        // Seed 2 distinct passed gate reviewers for G1 so department scope sign-offs satisfy TK-W2-06
        await using (var db = database.CreateContext())
        {
            db.GateReviewerAssignments.AddRange(
                new GateReviewerAssignment
                {
                    GateInstanceId = g1Id,
                    ReviewerId = s.Scope.Users.NewLecturer,
                    Scope = "COMMON",
                    Status = "REVIEWED",
                    SessionRole = "CHAIR",
                    LatestDecision = "PASSED",
                    CreatedAt = Now
                },
                new GateReviewerAssignment
                {
                    GateInstanceId = g1Id,
                    ReviewerId = s.Scope.Users.OtherLecturer,
                    Scope = "COMMON",
                    Status = "REVIEWED",
                    SessionRole = "MEMBER",
                    LatestDecision = "PASSED",
                    CreatedAt = Now
                });
            await db.SaveChangesAsync();
        }

        // 5. IT passes alone while MKT still has REVISION_REQUIRED -> G1 stays REVISION_REQUIRED (G2 stays PLANNED) until MKT also passes
        var passIt = await Body<GateRoundConsensusDto>(await leadStaff.PostAsJsonAsync(
            $"/api/v1/projects/{s.ProjectId}/review-gates/G1/round-scope-decisions",
            new RecordRoundScopeDecisionRequest(itMajorId.ToString(), 1, "PASSED", "IT verified OK")));
        Assert.False(passIt.AllRequiredScopesPassed);
        Assert.Equal("REVISION_REQUIRED", passIt.GateStatus);

        // 6. MKT also passes -> G1 becomes PASSED and opens G2!
        var passMkt = await Body<GateRoundConsensusDto>(await mktStaffClient.PostAsJsonAsync(
            $"/api/v1/projects/{s.ProjectId}/review-gates/G1/round-scope-decisions",
            new RecordRoundScopeDecisionRequest(mktMajorId.ToString(), 1, "PASSED", "MKT verified with Attribution Limits")));
        Assert.True(passMkt.AllRequiredScopesPassed);
        Assert.Equal("PASSED", passMkt.GateStatus);

        await using (var db = database.CreateContext())
        {
            var g2 = await db.ProjectGateInstances.SingleAsync(g => g.ProjectId == s.ProjectId && g.Code == "G2");
            Assert.Equal("READY", g2.ReviewEligibility);
        }
    }

    [Fact]
    public void HybridTeamRules_validates_explicit_three_tracks_without_name_inference()
    {
        var policy = new TeamFormationPolicy(2, 5, 24, "v1", 1);
        var okItOnly = new TeamAcademicScope("SINGLE_MAJOR", 1, 10, [new(1, 2, 5, "IT")], Guid.Empty, AcademicTrackCodes.ItOnly);
        Assert.Empty(HybridTeamRules.ConfigurationErrors(okItOnly, policy));

        var okMktOnly = new TeamAcademicScope("SINGLE_MAJOR", 2, 20, [new(2, 2, 5, "MKT")], Guid.Empty, AcademicTrackCodes.MktOnly, false, "Pilot 50 users; no paid ad ROAS.");
        Assert.Empty(HybridTeamRules.ConfigurationErrors(okMktOnly, policy));

        var mismatch = new TeamAcademicScope("SINGLE_MAJOR", 1, 10, [new(1, 2, 5, "IT")], Guid.Empty, AcademicTrackCodes.ItMkt);
        Assert.Contains("TRACK_MODE_MISMATCH", HybridTeamRules.ConfigurationErrors(mismatch, policy));

        var invalidTrack = new TeamAcademicScope("SINGLE_MAJOR", 1, 10, [new(1, 2, 5, "IT")], Guid.Empty, "UNKNOWN_TRACK");
        Assert.Contains("INVALID_TRACK_CODE", HybridTeamRules.ConfigurationErrors(invalidTrack, policy));
    }
}
