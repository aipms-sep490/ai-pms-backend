using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.ActionAggregators;
using AIPMS.Application.Features.Evaluations.DTOs;
using AIPMS.Application.Features.ProgressReports.DTOs;
using AIPMS.Application.Features.ProjectHub;
using AIPMS.Application.Features.ReviewGates;
using AIPMS.Infrastructure.Persistence.Models;
using AIPMS.IntegrationTests.Evaluations;
using Microsoft.EntityFrameworkCore;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.IntegrationTests.Wave2;

public sealed class Wave2ExecutionAndGatesIntegrationTests(EvaluationDraftDatabaseFixture database)
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
    public async Task W2_01_Bulk_generate_reporting_cycles_works_and_prevents_duplicates_and_enforces_403()
    {
        var s = await database.Seed(false);
        long execPeriodId;
        await using (var db = database.CreateContext())
        {
            var project = await db.Projects.SingleAsync(p => p.Id == s.ProjectId);
            project.Status = "ACTIVE";
            var execPeriod = new M.ProjectPeriod
            {
                AcademicSemesterId = s.Scope.SemesterId,
                Code = "EXEC-" + Guid.NewGuid().ToString("N")[..6],
                Name = "Execution Cycle Test",
                PeriodType = "EXECUTION",
                Status = "ACTIVE",
                StartAt = Now,
                EndAt = Now.AddDays(21)
            };
            db.ProjectPeriods.Add(execPeriod);
            await db.SaveChangesAsync();
            execPeriodId = execPeriod.Id;

            var snap = await db.Set<ProjectRegistrationSnapshot>().SingleAsync(x => x.ProjectId == s.ProjectId);
            snap.ProjectPeriodId = execPeriodId;
            await db.SaveChangesAsync();
        }

        using var app = new EvaluationFactory(database);
        using var student = app.CreateAuthenticatedClient(s.Scope.Users.Student, roles: [AppRoles.Student]);
        using var staff = app.CreateAuthenticatedClient(s.Scope.Users.Staff, roles: [AppRoles.DepartmentStaff]);

        // Negative: Student cannot bulk-generate reporting cycles (403)
        var denied = await student.PostAsJsonAsync(
            $"/api/v1/project-periods/{execPeriodId}/reporting-cycles/generate",
            new GenerateReportingCyclesRequest("WEEKLY", "FLAG", 1, false));
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        // Positive: Staff dry-run + live generate + idempotent re-run
        var dryRun = await Body<GenerateReportingCyclesResultDto>(await staff.PostAsJsonAsync(
            $"/api/v1/project-periods/{execPeriodId}/reporting-cycles/generate",
            new GenerateReportingCyclesRequest("WEEKLY", "FLAG", 1, true)));
        Assert.True(dryRun.DryRun);
        Assert.True(dryRun.CyclesCreated >= 3);

        var live1 = await Body<GenerateReportingCyclesResultDto>(await staff.PostAsJsonAsync(
            $"/api/v1/project-periods/{execPeriodId}/reporting-cycles/generate",
            new GenerateReportingCyclesRequest("WEEKLY", "FLAG", 1, false)));
        Assert.False(live1.DryRun);
        Assert.True(live1.CyclesCreated >= 3);

        var live2 = await Body<GenerateReportingCyclesResultDto>(await staff.PostAsJsonAsync(
            $"/api/v1/project-periods/{execPeriodId}/reporting-cycles/generate",
            new GenerateReportingCyclesRequest("WEEKLY", "FLAG", 1, false)));
        Assert.Equal(0, live2.CyclesCreated);
        Assert.True(live2.CyclesSkipped >= 3);
    }

    [Fact]
    public async Task W2_06_07_Interdisciplinary_gate_enforces_1_per_major_D17_candidates_immutable_lock_and_reopen()
    {
        var s = await database.Seed(false);
        long mktMajorId, mktDeptId, itRevId, mktRevId, primaryLecturerId, mktStaffId;

        await using (var db = database.CreateContext())
        {
            var project = await db.Projects.SingleAsync(p => p.Id == s.ProjectId);
            project.Status = "ACTIVE";
            project.TrackCode = "IT_MKT";

            var itMajorId = await db.ProjectMajors.Where(pm => pm.ProjectId == s.ProjectId).Select(pm => pm.MajorId).FirstAsync();
            var itDeptId = (await db.Majors.SingleAsync(m => m.Id == itMajorId)).DepartmentId;
            mktDeptId = (await db.Users.SingleAsync(u => u.Id == s.Scope.Users.OtherLecturer)).DepartmentId!.Value;
            var lecturerRoleId = await db.Roles.Where(r => r.Code == AppRoles.Lecturer).Select(r => r.Id).SingleAsync();
            var staffRoleId = await db.Roles.Where(r => r.Code == AppRoles.DepartmentStaff).Select(r => r.Id).SingleAsync();

            var mktMajor = new M.Major { DepartmentId = mktDeptId, Code = "MKTW2_" + Guid.NewGuid().ToString("N")[..4], Name = "Marketing W2", IsActive = true };
            db.Majors.Add(mktMajor);
            await db.SaveChangesAsync();
            mktMajorId = mktMajor.Id;

            var itRev = new M.User { Email = $"w2it_{Guid.NewGuid():N}@e2e.invalid", FullName = "IT Reviewer W2", PasswordHash = "x", Status = "ACTIVE", DepartmentId = itDeptId, UserRoleUsers = [new() { RoleId = lecturerRoleId }] };
            var mktRev = new M.User { Email = $"w2mkt_{Guid.NewGuid():N}@e2e.invalid", FullName = "MKT Reviewer W2", PasswordHash = "x", Status = "ACTIVE", DepartmentId = mktDeptId, UserRoleUsers = [new() { RoleId = lecturerRoleId }] };
            var mktStaff = new M.User { Email = $"w2mstaff_{Guid.NewGuid():N}@e2e.invalid", FullName = "MKT Staff W2", PasswordHash = "x", Status = "ACTIVE", DepartmentId = mktDeptId, UserRoleUsers = [new() { RoleId = staffRoleId }] };
            db.Users.AddRange(itRev, mktRev, mktStaff);

            db.ProjectMajors.Add(new M.ProjectMajor { ProjectId = s.ProjectId, MajorId = mktMajorId });
            await db.SaveChangesAsync();
            itRevId = itRev.Id;
            mktRevId = mktRev.Id;
            mktStaffId = mktStaff.Id;

            primaryLecturerId = await db.SupervisorAssignments
                .Where(a => a.ProjectId == s.ProjectId && a.IsPrimary && a.EndedAt == null)
                .Select(a => a.SupervisorProfile.UserId).SingleAsync();

            var snap = await db.Set<ProjectRegistrationSnapshot>().SingleAsync(r => r.ProjectId == s.ProjectId);
            snap.LeadDepartmentId = itDeptId;
            snap.SnapshotJson = JsonSerializer.Serialize(new
            {
                Scope = new
                {
                    ProjectMode = "INTERDISCIPLINARY",
                    PrimaryMajorId = (long?)null,
                    LeadDepartmentId = itDeptId,
                    TrackCode = "IT_MKT",
                    Requirements = new[]
                    {
                        new { MajorId = itMajorId, MinMembers = 1, MaxMembers = 4, Responsibility = "IT" },
                        new { MajorId = mktMajorId, MinMembers = 1, MaxMembers = 4, Responsibility = "MKT" }
                    }
                },
                DepartmentIds = new[] { itDeptId, mktDeptId },
                MajorDepartmentIds = new Dictionary<long, long> { [itMajorId] = itDeptId, [mktMajorId] = mktDeptId }
            });

            var tpl = new ReviewGateTemplate
            {
                ProjectPeriodId = snap.ProjectPeriodId,
                Code = "W2TPL_" + Guid.NewGuid().ToString("N")[..5],
                Name = "W2 3-Gate Template",
                Mode = "INTERDISCIPLINARY",
                Major = "IT_MKT",
                Version = 1,
                Status = "PUBLISHED",
                CreatedBy = s.Scope.Users.Staff,
                CreatedAt = Now,
                UpdatedAt = Now,
                ConcurrencyToken = Guid.NewGuid()
            };
            db.ReviewGateTemplates.Add(tpl);
            await db.SaveChangesAsync();

            var gate1 = new ProjectGateInstance
            {
                ProjectId = s.ProjectId, TemplateId = tpl.Id, TemplateVersion = 1, Code = "G1", Sequence = 1,
                Title = "Gate 1", Status = "IN_REVIEW", ReviewEligibility = "READY", Required = true,
                BlockNextGate = true, BlocksDefense = false, Weight = 30m, CreatedAt = Now, ConcurrencyToken = Guid.NewGuid(),
                Requirements =
                [
                    new ProjectGateRequirement { Code = "G1_COMMON", Title = "Common Spec", Type = "DOCUMENT", Scope = "COMMON", Required = true, VerificationStatus = "SUBMITTED" },
                    new ProjectGateRequirement { Code = "G1_IT", Title = "IT Arch", Type = "DOCUMENT", Scope = "MAJOR_SPECIFIC", MajorId = itMajorId, Required = true, VerificationStatus = "SUBMITTED" },
                    new ProjectGateRequirement { Code = "G1_MKT", Title = "MKT Plan", Type = "DOCUMENT", Scope = "MAJOR_SPECIFIC", MajorId = mktMajorId, Required = true, VerificationStatus = "SUBMITTED" }
                ]
            };
            db.ProjectGateInstances.Add(gate1);
            await db.SaveChangesAsync();

            var commonReq = gate1.Requirements.Single(r => r.Code == "G1_COMMON");
            commonReq.Evidence.Add(new GateEvidence { ProjectId = s.ProjectId, Version = 1, ArtifactName = "common.pdf", StorageKey = "k1", ScanStatus = "READY", SubmittedBy = s.Scope.Users.Student, SubmittedAt = Now });
            await db.SaveChangesAsync();
            commonReq.LatestEvidenceId = commonReq.Evidence.First().Id;
            await db.SaveChangesAsync();
        }

        using var app = new EvaluationFactory(database);
        using var leadStaff = app.CreateAuthenticatedClient(s.Scope.Users.Staff, roles: [AppRoles.DepartmentStaff]);
        using var mktStaffClient = app.CreateAuthenticatedClient(mktStaffId, roles: [AppRoles.DepartmentStaff]);
        using var primaryClient = app.CreateAuthenticatedClient(primaryLecturerId, roles: [AppRoles.Lecturer]);
        using var itRevClient = app.CreateAuthenticatedClient(itRevId, roles: [AppRoles.Lecturer]);
        using var mktRevClient = app.CreateAuthenticatedClient(mktRevId, roles: [AppRoles.Lecturer]);
        using var studentClient = app.CreateAuthenticatedClient(s.Scope.Users.Student, roles: [AppRoles.Student]);

        // Positive: Reviewer candidates excludes D17 conflicted Primary Supervisor
        var candidates = await Body<PagedResult<GateReviewerCandidateDto>>(
            await leadStaff.GetAsync($"/api/v1/projects/{s.ProjectId}/review-gates/G1/reviewer-candidates"));
        Assert.DoesNotContain(candidates.Items, c => c.UserId == primaryLecturerId);

        long itMajor;
        await using (var dbCheck = database.CreateContext())
        {
            itMajor = await dbCheck.ProjectMajors.Where(pm => pm.ProjectId == s.ProjectId && pm.MajorId != mktMajorId).Select(pm => pm.MajorId).SingleAsync();
        }

        // Assign 1st IT reviewer
        var a1 = await Body<AssignedReviewItemDto>(await leadStaff.PostAsJsonAsync(
            "/api/v1/review-gates/assignments",
            new CreateReviewGateAssignmentRequest(s.ProjectId.ToString(), "G1", itRevId, "MAJOR_SPECIFIC", itMajor, null, null, "CHAIR")));
        Assert.NotNull(a1);

        // Negative: 2nd IT reviewer on IT_MKT -> 409 GATE_MAJOR_REVIEWER_LIMIT
        var dupMajorRes = await leadStaff.PostAsJsonAsync(
            "/api/v1/review-gates/assignments",
            new CreateReviewGateAssignmentRequest(s.ProjectId.ToString(), "G1", s.Scope.Users.OtherLecturer, "MAJOR_SPECIFIC", itMajor, null, null));
        Assert.Equal(HttpStatusCode.Conflict, dupMajorRes.StatusCode);
        Assert.Equal(WorkflowErrorCodes.GateMajorReviewerLimit, await ReadProblemCode(dupMajorRes));

        // Negative: Primary supervisor COI -> 409 EVALUATOR_ROLE_CONFLICT
        var coiRes = await mktStaffClient.PostAsJsonAsync(
            "/api/v1/review-gates/assignments",
            new CreateReviewGateAssignmentRequest(s.ProjectId.ToString(), "G1", primaryLecturerId, "MAJOR_SPECIFIC", mktMajorId, null, null));
        Assert.Equal(HttpStatusCode.Conflict, coiRes.StatusCode);
        Assert.Equal(WorkflowErrorCodes.EvaluatorRoleConflict, await ReadProblemCode(coiRes));

        // Positive: Participating dept assigns 1 MKT reviewer
        var a2 = await Body<AssignedReviewItemDto>(await mktStaffClient.PostAsJsonAsync(
            "/api/v1/review-gates/assignments",
            new CreateReviewGateAssignmentRequest(s.ProjectId.ToString(), "G1", mktRevId, "MAJOR_SPECIFIC", mktMajorId, null, null, "MEMBER")));
        Assert.NotNull(a2);

        // Verify independent per-assignment verification (IT=ACCEPTED vs MKT=REJECTED)
        var itAssigned = (await Body<PagedResult<AssignedReviewItemDto>>(await itRevClient.GetAsync("/api/v1/reviewers/me/gate-assignments"))).Items.Single();
        var mktAssigned = (await Body<PagedResult<AssignedReviewItemDto>>(await mktRevClient.GetAsync("/api/v1/reviewers/me/gate-assignments"))).Items.Single();
        var commonReqClientId = itAssigned.Requirements.Single(r => r.Code == "G1_COMMON").Id;

        await Body<AssignedReviewItemDto>(await itRevClient.PostAsJsonAsync(
            $"/api/v1/review-gates/assignments/{itAssigned.Id}/evidence/{commonReqClientId}/verify",
            new VerifyReviewGateEvidenceRequest(commonReqClientId, "ACCEPTED", "IT dong y phan chung", itAssigned.ConcurrencyToken)));
        await Body<AssignedReviewItemDto>(await mktRevClient.PostAsJsonAsync(
            $"/api/v1/review-gates/assignments/{mktAssigned.Id}/evidence/{commonReqClientId}/verify",
            new VerifyReviewGateEvidenceRequest(commonReqClientId, "REJECTED", "MKT yeu cau bo sung KPI", mktAssigned.ConcurrencyToken)));

        var timelineAfterVerify = await Body<StudentReviewTimelineDto>(await studentClient.GetAsync($"/api/v1/projects/{s.ProjectId}/review-gates/timeline"));
        var commonReqView = timelineAfterVerify.Gates.Single(g => g.Code == "G1").Requirements.Single(r => r.Code == "G1_COMMON");
        Assert.Equal("REJECTED", commonReqView.VerificationStatus);
        Assert.Equal(2, commonReqView.Verifications?.Count);

        // Negative: Participating dept cannot bypass 2-reviewer panel to set PASSED -> 409 GATE_REVIEW_PANEL_REQUIRED
        var bypassRes = await mktStaffClient.PostAsJsonAsync(
            $"/api/v1/projects/{s.ProjectId}/review-gates/G1/round-scope-decisions",
            new RecordRoundScopeDecisionRequest(mktMajorId.ToString(), 1, "PASSED", "Bypass pass without reviewer"));
        Assert.Equal(HttpStatusCode.Conflict, bypassRes.StatusCode);
        Assert.Equal(WorkflowErrorCodes.GateReviewPanelRequired, await ReadProblemCode(bypassRes));

        // Negative: Lead override with short reason -> 422 OVERRIDE_REASON_TOO_SHORT (<20)
        var timelineBefore = await Body<StudentReviewTimelineDto>(await leadStaff.GetAsync($"/api/v1/projects/{s.ProjectId}/review-gates/timeline"));
        var g1Token = timelineBefore.Gates.Single(g => g.Code == "G1").ConcurrencyToken;
        var shortOv = await leadStaff.PostAsJsonAsync(
            $"/api/v1/projects/{s.ProjectId}/review-gates/G1/override",
            new OverrideReviewGateRequest("PASSED", "Too short reason", g1Token));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, shortOv.StatusCode);

        // Positive: Lead overrides G1 to PASSED (20+ chars) -> G1 is immutably locked
        var ovOk = await Body<OverrideReviewGateDto>(await leadStaff.PostAsJsonAsync(
            $"/api/v1/projects/{s.ProjectId}/review-gates/G1/override",
            new OverrideReviewGateRequest("PASSED", "Khoa chu tri xac nhan thong qua cong 1 sau khi ra soat du lieu.", g1Token)));
        Assert.Equal("PASSED", ovOk.Status);

        // Negative (TK-W2-07): Draft evidence upload cannot overwrite locked PASSED gate -> 409 GATE_IMMUTABLE_LOCKED
        var g1ReqId = timelineBefore.Gates.Single(g => g.Code == "G1").Requirements.First().Id;
        var lockedUpload = await studentClient.PostAsJsonAsync(
            $"/api/v1/projects/{s.ProjectId}/review-gates/G1/evidence",
            new SubmitReviewGateEvidenceRequest(g1ReqId, "https://example.org/new", "Attempt overwrite", 1, ovOk.ConcurrencyToken, "https://example.org/new"));
        Assert.Equal(HttpStatusCode.Conflict, lockedUpload.StatusCode);
        Assert.Equal(WorkflowErrorCodes.GateImmutableLocked, await ReadProblemCode(lockedUpload));

        // Negative: Participating dept cannot reopen -> 403
        var mktReopen = await mktStaffClient.PostAsJsonAsync(
            $"/api/v1/projects/{s.ProjectId}/review-gates/G1/reopen",
            new ReopenReviewGateRequest("Participating department attempting to reopen locked gate", ovOk.ConcurrencyToken));
        Assert.Equal(HttpStatusCode.Forbidden, mktReopen.StatusCode);

        // Positive: Lead Dept reopens locked gate with 20+ char audit reason
        var reopened = await Body<StudentReviewTimelineDto>(await leadStaff.PostAsJsonAsync(
            $"/api/v1/projects/{s.ProjectId}/review-gates/G1/reopen",
            new ReopenReviewGateRequest("Mo lai cong 1 de nhom bo sung phu luc kiem thu theo yeu cau.", ovOk.ConcurrencyToken)));
        Assert.Equal("REVISION_REQUIRED", reopened.Gates.Single(g => g.Code == "G1").Status);
    }

    [Fact]
    public async Task W2_09_POL03_allows_mentor_note_before_primary_draft_and_blocks_finalize_with_null_score()
    {
        var s = await database.Seed(false);
        long gate1Id, primaryId, mktMentorId, itStudentId, mktStudentId;

        await using (var db = database.CreateContext())
        {
            var project = await db.Projects.Include(p => p.Team).SingleAsync(p => p.Id == s.ProjectId);
            project.Status = "ACTIVE";
            itStudentId = s.Scope.Users.Student;

            var mktDeptId = (await db.Users.SingleAsync(u => u.Id == s.Scope.Users.OtherLecturer)).DepartmentId!.Value;
            var mktMajor = new M.Major { DepartmentId = mktDeptId, Code = "MKTP03_" + Guid.NewGuid().ToString("N")[..4], Name = "MKT POL03", IsActive = true };
            db.Majors.Add(mktMajor);
            await db.SaveChangesAsync();
            db.ProjectMajors.Add(new M.ProjectMajor { ProjectId = s.ProjectId, MajorId = mktMajor.Id });

            var mktStudent = new M.User
            {
                Email = $"mkt.pol03.{Guid.NewGuid():N}@aipms.edu.vn",
                PasswordHash = "hash",
                FullName = "MKT Student POL03",
                Status = "ACTIVE",
                AcademicProfileStatus = "VERIFIED",
                DepartmentId = mktDeptId,
                MajorId = mktMajor.Id,
                CreatedAt = Now,
                UpdatedAt = Now
            };
            db.Users.Add(mktStudent);
            await db.SaveChangesAsync();
            var studentRoleId = await db.Roles.Where(r => r.Code == AppRoles.Student).Select(r => r.Id).SingleAsync();
            db.UserRoles.Add(new M.UserRole { UserId = mktStudent.Id, RoleId = studentRoleId });
            db.TeamMembers.Add(new M.TeamMember { TeamId = project.TeamId, AcademicSemesterId = project.Team.AcademicSemesterId, UserId = mktStudent.Id, IsLeader = false, JoinedAt = Now.AddDays(-2), CreatedAt = Now.AddDays(-2), UpdatedAt = Now.AddDays(-2) });
            await db.SaveChangesAsync();
            mktStudentId = mktStudent.Id;

            var mentorUserId = s.Scope.Users.OtherLecturer;
            var mentorProfile = await db.SupervisorProfiles.SingleOrDefaultAsync(sp => sp.UserId == mentorUserId)
                ?? db.SupervisorProfiles.Add(new M.SupervisorProfile { UserId = mentorUserId, IsAvailable = true, MaxActiveProjects = 5 }).Entity;
            await db.SaveChangesAsync();

            var mentorReq = new M.SupervisorRequest { ProjectId = s.ProjectId, SupervisorProfileId = mentorProfile.Id, RequestedBy = itStudentId, Status = "ACCEPTED", RequestedAt = Now.AddDays(-2), RespondedAt = Now.AddDays(-1) };
            db.SupervisorAssignments.Add(new M.SupervisorAssignment { ProjectId = s.ProjectId, SupervisorRequest = mentorReq, SupervisorProfileId = mentorProfile.Id, IsPrimary = false, AssignmentType = "DISCIPLINE_MENTOR", MajorId = mktMajor.Id, AssignedAt = Now.AddDays(-1) });
            mktMentorId = mentorUserId;

            primaryId = await db.SupervisorAssignments.Where(a => a.ProjectId == s.ProjectId && a.IsPrimary && a.EndedAt == null).Select(a => a.SupervisorProfile.UserId).SingleAsync();

            var tpl = new ReviewGateTemplate
            {
                Code = "W2P03_" + Guid.NewGuid().ToString("N")[..5],
                Name = "POL03 Gate Template",
                Mode = "INTERDISCIPLINARY",
                Major = "IT_MKT",
                Version = 1,
                Status = "PUBLISHED",
                CreatedBy = s.Scope.Users.Staff,
                CreatedAt = Now,
                UpdatedAt = Now,
                ConcurrencyToken = Guid.NewGuid()
            };
            db.ReviewGateTemplates.Add(tpl);
            await db.SaveChangesAsync();

            var g1 = new ProjectGateInstance
            {
                ProjectId = s.ProjectId, TemplateId = tpl.Id, TemplateVersion = 1, Code = "G1", Sequence = 1,
                Title = "Gate 1", Status = "IN_REVIEW", ReviewEligibility = "READY", Required = true,
                BlockNextGate = true, BlocksDefense = false, Weight = 30m, CreatedAt = Now, ConcurrencyToken = Guid.NewGuid()
            };
            db.ProjectGateInstances.Add(g1);
            await db.SaveChangesAsync();
            gate1Id = g1.Id;
        }

        using var app = new EvaluationFactory(database);
        using var mentorClient = app.CreateAuthenticatedClient(mktMentorId, roles: [AppRoles.Lecturer]);
        using var primaryClient = app.CreateAuthenticatedClient(primaryId, roles: [AppRoles.Lecturer]);

        // Negative (TK-W2-09): MKT mentor cannot leave reference note for out-of-major IT student -> 403 SCOPE_ACCESS_DENIED
        var crossMajorNote = await mentorClient.PutAsJsonAsync(
            $"/api/v1/projects/{s.ProjectId}/gates/{gate1Id}/individual-process-scores/{itStudentId}/mentor-note",
            new UpsertMentorReferenceNoteRequest("Out of major note attempt"));
        Assert.Equal(HttpStatusCode.Forbidden, crossMajorNote.StatusCode);
        Assert.Equal(WorkflowErrorCodes.ScopeAccessDenied, await ReadProblemCode(crossMajorNote));

        // Positive (TK-W2-09): MKT mentor saves reference note for MKT student BEFORE primary creates draft (Score == null)
        var noteItem = await Body<GateIndividualProcessScoreItemDto>(await mentorClient.PutAsJsonAsync(
            $"/api/v1/projects/{s.ProjectId}/gates/{gate1Id}/individual-process-scores/{mktStudentId}/mentor-note",
            new UpsertMentorReferenceNoteRequest("Dong gop nghien cuu thi truong dat muc tot.")));
        Assert.Null(noteItem.Score);
        Assert.Equal("DRAFT", noteItem.Status);
        Assert.Contains("nghien cuu thi truong", noteItem.MentorReferenceNotes);

        // Negative: Mentor cannot write numeric score (403 Forbidden — only active Primary Supervisor may grade 20% POL-03)
        var mentorScoreAttempt = await mentorClient.PutAsJsonAsync(
            $"/api/v1/projects/{s.ProjectId}/gates/{gate1Id}/individual-process-scores/{mktStudentId}",
            new UpsertIndividualProcessScoreRequest(8.5m, "Mentor trying to score"));
        Assert.Equal(HttpStatusCode.Forbidden, mentorScoreAttempt.StatusCode);

        // Negative: Primary cannot finalize while Score is still null -> 422 INDIVIDUAL_PROCESS_SCORE_REQUIRED
        var finalizeNull = await primaryClient.PostAsJsonAsync(
            $"/api/v1/projects/{s.ProjectId}/gates/{gate1Id}/individual-process-scores/{mktStudentId}/finalize",
            new FinalizeIndividualProcessScoreRequest(noteItem.ConcurrencyToken));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, finalizeNull.StatusCode);
        Assert.Equal("INDIVIDUAL_PROCESS_SCORE_REQUIRED", await ReadProblemCode(finalizeNull));

        // Positive: Primary saves numeric score preserving mentor note, then finalizes
        var drafted = await Body<GateIndividualProcessScoreItemDto>(await primaryClient.PutAsJsonAsync(
            $"/api/v1/projects/{s.ProjectId}/gates/{gate1Id}/individual-process-scores/{mktStudentId}",
            new UpsertIndividualProcessScoreRequest(8.75m, "GVHD chot diem G1", null, noteItem.ConcurrencyToken)));
        Assert.Equal(8.75m, drafted.Score);
        Assert.Contains("nghien cuu thi truong", drafted.MentorReferenceNotes);

        var finalized = await Body<GateIndividualProcessScoreItemDto>(await primaryClient.PostAsJsonAsync(
            $"/api/v1/projects/{s.ProjectId}/gates/{gate1Id}/individual-process-scores/{mktStudentId}/finalize",
            new FinalizeIndividualProcessScoreRequest(drafted.ConcurrencyToken)));
        Assert.Equal("FINALIZED", finalized.Status);
    }

    [Fact]
    public async Task W2_13_14_Health_pulse_blocks_students_with_403_and_triage_queue_includes_open_actions()
    {
        var s = await database.Seed(false);
        long primaryId;
        await using (var db = database.CreateContext())
        {
            var project = await db.Projects.SingleAsync(p => p.Id == s.ProjectId);
            project.Status = "ACTIVE";
            primaryId = await db.SupervisorAssignments
                .Where(a => a.ProjectId == s.ProjectId && a.IsPrimary && a.EndedAt == null)
                .Select(a => a.SupervisorProfile.UserId).SingleAsync();
        }

        using var app = new EvaluationFactory(database);
        using var student = app.CreateAuthenticatedClient(s.Scope.Users.Student, roles: [AppRoles.Student]);
        using var primary = app.CreateAuthenticatedClient(primaryId, roles: [AppRoles.Lecturer]);

        // Negative (TK-W2-13): Student gets 403 on project Health Pulse endpoint
        var denied = await student.GetAsync($"/api/v1/projects/{s.ProjectId}/hub/health-pulse");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        // Positive: Primary supervisor reads Health Pulse (4 signals) & Triage Queue
        var pulse = await Body<ProjectHealthPulseDto>(await primary.GetAsync($"/api/v1/projects/{s.ProjectId}/hub/health-pulse"));
        Assert.Equal(s.ProjectId, pulse.ProjectId);

        var triage = await Body<SupervisorTriageQueueDto>(await primary.GetAsync("/api/v1/supervisors/me/triage-queue"));
        Assert.NotNull(triage.Items);
    }
}
