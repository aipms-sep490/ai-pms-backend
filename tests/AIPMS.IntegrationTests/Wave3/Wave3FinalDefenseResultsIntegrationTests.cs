using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.AcademicTracking;
using AIPMS.Application.Features.Cold;
using AIPMS.Application.Features.Deliverables.DTOs;
using AIPMS.Application.Features.Evaluations.DTOs;
using AIPMS.Application.Features.FinalSubmissions.DTOs;
using AIPMS.Application.Features.ProjectChecklists;
using AIPMS.Application.Features.Results.DTOs;
using AIPMS.Application.Features.Topics.DTOs;
using AIPMS.Domain.Teams;
using AIPMS.Infrastructure.Persistence.Models;
using AIPMS.IntegrationTests.Evaluations;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.IntegrationTests.Wave3;

public sealed class Wave3FinalDefenseResultsIntegrationTests(EvaluationDraftDatabaseFixture database)
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
        if (string.IsNullOrWhiteSpace(text)) return null;
        using var doc = JsonDocument.Parse(text);
        if (doc.RootElement.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.String) return c.GetString();
        if (doc.RootElement.TryGetProperty("errorCode", out var ec) && ec.ValueKind == JsonValueKind.String) return ec.GetString();
        return null;
    }

    private static async Task<long> SeedConfirmedCommitteeAsync(
        EvaluationDraftDatabaseFixture database,
        long projectId,
        long staffId,
        params (long UserId, long MajorId, string Role)[] members)
    {
        await using var db = database.CreateContext();
        var committee = new DefenseCommittee
        {
            ProjectId = projectId,
            Name = "Confirmed Committee " + Guid.NewGuid().ToString("N")[..5],
            Status = "CONFIRMED",
            CreatedBy = staffId,
            CreatedAt = Now,
            Members = members.Select(m => new DefenseCommitteeMember
            {
                UserId = m.UserId,
                MajorId = m.MajorId,
                Role = m.Role
            }).ToList()
        };
        db.Set<DefenseCommittee>().Add(committee);
        await db.SaveChangesAsync();
        return committee.Id;
    }

    private static async Task LockColdPackageViaApiAsync(
        EvaluationDraftDatabaseFixture database,
        HttpClient staff,
        HttpClient leader,
        long projectId,
        long periodId,
        long leaderUserId)
    {
        await using (var db = database.CreateContext())
        {
            var proj = await db.Projects.SingleAsync(p => p.Id == projectId);
            var member = await db.TeamMembers.SingleAsync(m => m.TeamId == proj.TeamId && m.UserId == leaderUserId);
            member.IsLeader = true;
            await db.SaveChangesAsync();
        }
        var coldTemplate = await Body<ColdTemplateDto>(await staff.PostAsJsonAsync("/api/v1/cold-report-templates",
            new CreateColdTemplateRequest(null, periodId, "Cold Report " + Guid.NewGuid().ToString("N")[..5], [new("REPORT", "Cold Package Report")], "COMMON")));
        var checklist = await Body<ColdChecklistDto>(await staff.PostAsJsonAsync($"/api/v1/projects/{projectId}/cold/checklist",
            new ApplyColdChecklistRequest(periodId, new(Now.AddHours(4)), [new(coldTemplate.Id, "COMMON")])));
        var item = Assert.Single(checklist.Items);
        var coldDraft = await Body<ColdSubmissionDto>(await leader.PostAsJsonAsync($"/api/v1/cold-checklist-items/{item.Id}/draft",
            new SaveColdDraftRequest(0, "Cold evidence")));
        using (var form = new MultipartFormDataContent())
        {
            var file = new ByteArrayContent(Encoding.UTF8.GetBytes("%PDF-1.7\nWave3 cold report"));
            file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
            form.Add(file, "file", "cold-report.pdf");
            form.Add(new StringContent(coldDraft.ConcurrencyToken), "concurrencyToken");
            coldDraft = await Body<ColdSubmissionDto>(await leader.PostAsync($"/api/v1/cold-submissions/{coldDraft.Id}/files", form));
        }
        await Body<ColdSubmissionDto>(await leader.PostAsJsonAsync($"/api/v1/cold-submissions/{coldDraft.Id}/submit",
            new ColdTokenRequest(coldDraft.ConcurrencyToken)));
        checklist = await Body<ColdChecklistDto>(await leader.GetAsync($"/api/v1/projects/{projectId}/cold/checklist"));
        await Body<ColdChecklistDto>(await leader.PostAsJsonAsync($"/api/v1/projects/{projectId}/cold/lock",
            new ColdTokenRequest(checklist.ConcurrencyToken)));
    }

    private static async Task<DeliverableVersionDto> UploadVersionAsync(HttpClient client, long deliverableId, int expectedLatest, string content, string fileName = "artifact.txt")
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(content));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        form.Add(file, "file", fileName);
        form.Add(new StringContent(expectedLatest.ToString()), "expectedLatestVersion");
        return await Body<DeliverableVersionDto>(await client.PostAsync($"/api/v1/deliverables/{deliverableId}/versions", form));
    }

    private static async Task<(long PrimarySupId, long ItMajorId, long MktDeptId, long MktMajorId, long MktStudentId, long MktStaffId)>
        ConfigureInterdisciplinaryProjectAsync(EvaluationDraftDatabaseFixture database, EvaluationScenario s, string projectStatus = "ACTIVE")
    {
        await using var db = database.CreateContext();
        var project = await db.Projects.SingleAsync(p => p.Id == s.ProjectId);
        project.Status = projectStatus;
        project.TrackCode = AcademicTrackCodes.ItMkt;

        var itMajorId = await db.ProjectMajors.Where(pm => pm.ProjectId == s.ProjectId).Select(pm => pm.MajorId).FirstAsync();
        var leadDeptId = (await db.Majors.SingleAsync(m => m.Id == itMajorId)).DepartmentId;
        var mktDeptId = (await db.Users.SingleAsync(u => u.Id == s.Scope.Users.OtherLecturer)).DepartmentId!.Value;
        var mktMajor = new M.Major { DepartmentId = mktDeptId, Code = "MKTW3_" + Guid.NewGuid().ToString("N")[..4], Name = "Marketing W3", IsActive = true };
        db.Majors.Add(mktMajor);
        await db.SaveChangesAsync();
        db.ProjectMajors.Add(new M.ProjectMajor { ProjectId = s.ProjectId, MajorId = mktMajor.Id });

        var studentRoleId = await db.Roles.Where(r => r.Code == AppRoles.Student).Select(r => r.Id).SingleAsync();
        var staffRoleId = await db.Roles.Where(r => r.Code == AppRoles.DepartmentStaff).Select(r => r.Id).SingleAsync();

        var mktStudent = new M.User
        {
            Email = $"mkt_stu_w3_{Guid.NewGuid():N}@e2e.invalid",
            FullName = "MKT Student W3",
            StudentCode = "MKT" + Guid.NewGuid().ToString("N")[..6],
            PasswordHash = "unused",
            Status = "ACTIVE",
            DepartmentId = mktDeptId,
            MajorId = mktMajor.Id,
            AcademicProfileStatus = "VERIFIED",
            UserRoleUsers = [new() { RoleId = studentRoleId }]
        };
        var mktStaff = new M.User
        {
            Email = $"mkt_staff_w3_{Guid.NewGuid():N}@e2e.invalid",
            FullName = "MKT Staff W3",
            PasswordHash = "unused",
            Status = "ACTIVE",
            DepartmentId = mktDeptId,
            UserRoleUsers = [new() { RoleId = staffRoleId }]
        };
        db.Users.AddRange(mktStudent, mktStaff);
        await db.SaveChangesAsync();

        var leaderMember = await db.TeamMembers.SingleAsync(m => m.TeamId == project.TeamId && m.UserId == s.Scope.Users.Student);
        leaderMember.IsLeader = true;
        db.TeamMembers.Add(new M.TeamMember
        {
            TeamId = project.TeamId,
            AcademicSemesterId = s.Scope.SemesterId,
            UserId = mktStudent.Id,
            IsLeader = false,
            JoinedAt = Now.AddDays(-10)
        });

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
                    new(mktMajor.Id, 1, 4, "Marketing Execution")
                ]
            },
            Members = [.. reg.Members, new(mktStudent.Id, mktStudent.FullName, mktMajor.Id, false)],
            DepartmentIds = [leadDeptId, mktDeptId],
            MajorDepartmentIds = new Dictionary<long, long> { [itMajorId] = leadDeptId, [mktMajor.Id] = mktDeptId }
        });

        var primarySupId = await db.SupervisorAssignments
            .Where(a => a.ProjectId == s.ProjectId && a.IsPrimary && a.EndedAt == null)
            .Select(a => a.SupervisorProfile.UserId)
            .SingleAsync();
        await db.SaveChangesAsync();
        return (primarySupId, itMajorId, mktDeptId, mktMajor.Id, mktStudent.Id, mktStaff.Id);
    }

    [Fact]
    public async Task W3_01_Two_step_lock_blocks_legacy_1step_freezes_final_and_cold_and_supports_force_lock_and_reopen()
    {
        var s = await database.Seed(false);
        long finalPeriodId, primarySupId;
        await using (var db = database.CreateContext())
        {
            var project = await db.Projects.SingleAsync(p => p.Id == s.ProjectId);
            project.Status = "ACTIVE";
            var leaderMember = await db.TeamMembers.SingleAsync(m => m.TeamId == project.TeamId && m.UserId == s.Scope.Users.Student);
            leaderMember.IsLeader = true;
            var finalPeriod = new M.ProjectPeriod
            {
                AcademicSemesterId = s.Scope.SemesterId,
                Code = "FINW3_" + Guid.NewGuid().ToString("N")[..5],
                Name = "Final W3",
                PeriodType = "FINAL_SUBMISSION",
                Status = "ACTIVE",
                StartAt = Now.AddDays(-1),
                EndAt = Now.AddDays(1)
            };
            var execPeriod = new M.ProjectPeriod
            {
                AcademicSemesterId = s.Scope.SemesterId,
                Code = "EXECW3_" + Guid.NewGuid().ToString("N")[..5],
                Name = "Execution W3",
                PeriodType = "EXECUTION",
                Status = "ACTIVE",
                StartAt = Now.AddDays(-10),
                EndAt = Now.AddDays(10)
            };
            db.ProjectPeriods.AddRange(finalPeriod, execPeriod);
            await db.SaveChangesAsync();
            finalPeriodId = finalPeriod.Id;
            var majorId = await db.ProjectMajors.Where(pm => pm.ProjectId == s.ProjectId).Select(pm => pm.MajorId).FirstAsync();
            primarySupId = await db.SupervisorAssignments.Where(a => a.ProjectId == s.ProjectId && a.IsPrimary)
                .Select(a => a.SupervisorProfile.UserId).SingleAsync();
            db.Set<DefenseCommittee>().Add(new DefenseCommittee
            {
                ProjectId = s.ProjectId, Name = "W3-01 Committee", Status = "CONFIRMED", CreatedBy = s.Scope.Users.Staff, CreatedAt = Now,
                Members = [new() { UserId = s.Scope.Users.NewLecturer, MajorId = majorId, Role = "CHAIR" }]
            });
            await db.SaveChangesAsync();
        }

        using var app = new EvaluationFactory(database);
        using var leader = app.CreateAuthenticatedClient(s.Scope.Users.Student, roles: [AppRoles.Student]);
        using var staff = app.CreateAuthenticatedClient(s.Scope.Users.Staff, roles: [AppRoles.DepartmentStaff]);
        using var admin = app.CreateAuthenticatedClient(s.Scope.Users.Admin, roles: [AppRoles.Admin]);
        using var primary = app.CreateAuthenticatedClient(primarySupId, roles: [AppRoles.Lecturer]);

        var deliv = await Body<DeliverableDto>(await leader.PostAsJsonAsync($"/api/v1/projects/{s.ProjectId}/deliverables",
            new SaveDeliverableRequest(null, "Final Report", null, "REPORT", Now.AddDays(1))));
        var ver = await UploadVersionAsync(leader, deliv.Id, 0, "Final report bytes");
        var draft = await Body<FinalSubmissionDraftDto>(await leader.PostAsJsonAsync($"/api/v1/projects/{s.ProjectId}/final-submission-draft",
            new CreateFinalSubmissionDraftRequest(finalPeriodId, "Ready for supervisor lock", [ver.Id])));
        var reqs = await Body<FinalRequirementsDto>(await staff.PutAsJsonAsync($"/api/v1/projects/{s.ProjectId}/final-submission/requirements",
            new ConfigureFinalRequirementsRequest([deliv.Id], null)));

        // Negative: Legacy 1-step POST /final-submission is disabled by default -> 409 LEGACY_ONE_STEP_FINAL_LOCK_DISABLED
        var legacyAttempt = await leader.PostAsJsonAsync($"/api/v1/projects/{s.ProjectId}/final-submission",
            new SubmitFinalSubmissionRequest(draft.ConcurrencyToken, reqs.ConcurrencyToken!));
        Assert.Equal(HttpStatusCode.Conflict, legacyAttempt.StatusCode);
        Assert.Equal("LEGACY_ONE_STEP_FINAL_LOCK_DISABLED", await ReadProblemCode(legacyAttempt));

        // GET lock-status -> OPEN
        var initialStatus = await Body<PreDefenseLockStatusDto>(await leader.GetAsync($"/api/v1/projects/{s.ProjectId}/final-submission/lock-status"));
        Assert.Equal("OPEN", initialStatus.LockWorkflowStatus);

        // Step 1: Request lock -> PENDING_SUPERVISOR_LOCK
        var requested = await Body<PreDefenseLockStatusDto>(await leader.PostAsJsonAsync(
            $"/api/v1/projects/{s.ProjectId}/final-submission/request-lock",
            new RequestFinalLockRequest(draft.ConcurrencyToken)));
        Assert.Equal("PENDING_SUPERVISOR_LOCK", requested.LockWorkflowStatus);

        // Negative: Editing draft while PENDING_SUPERVISOR_LOCK -> 409 FINAL_LOCK_PENDING
        var editWhilePending = await leader.PutAsJsonAsync($"/api/v1/projects/{s.ProjectId}/final-submission-draft",
            new UpdateFinalSubmissionDraftRequest(finalPeriodId, "Edited", [ver.Id], requested.DraftConcurrencyToken!));
        Assert.Equal(HttpStatusCode.Conflict, editWhilePending.StatusCode);
        Assert.Equal("FINAL_LOCK_PENDING", await ReadProblemCode(editWhilePending));

        // Step 2a: Supervisor returns to OPEN
        var returned = await Body<PreDefenseLockStatusDto>(await primary.PostAsJsonAsync(
            $"/api/v1/projects/{s.ProjectId}/final-submission/supervisor-lock",
            new SupervisorFinalLockDecisionRequest("RETURN_TO_OPEN", "Please verify appendix", requested.DraftConcurrencyToken)));
        Assert.Equal("OPEN", returned.LockWorkflowStatus);

        // Re-request and Step 2b: Supervisor signs LOCK -> unified FinalSubmission + ColdManifestSha256
        var reRequested = await Body<PreDefenseLockStatusDto>(await leader.PostAsJsonAsync(
            $"/api/v1/projects/{s.ProjectId}/final-submission/request-lock",
            new RequestFinalLockRequest(returned.DraftConcurrencyToken)));
        var locked = await Body<PreDefenseLockStatusDto>(await primary.PostAsJsonAsync(
            $"/api/v1/projects/{s.ProjectId}/final-submission/supervisor-lock",
            new SupervisorFinalLockDecisionRequest("LOCK", "Approved for defense", reRequested.DraftConcurrencyToken)));
        Assert.Equal("LOCKED", locked.LockWorkflowStatus);
        Assert.Equal("FINAL_SUBMISSION", locked.ProjectStatus);
        Assert.NotNull(locked.FinalSubmissionId);
        Assert.False(string.IsNullOrWhiteSpace(locked.ColdManifestSha256));

        // Negative: ADMIN cannot reopen (403); Lead Staff can reopen before evaluation starts
        var adminReopen = await admin.PostAsJsonAsync($"/api/v1/projects/{s.ProjectId}/final-submission/reopen",
            new ReopenFinalLockRequest("Admin attempt", locked.DraftConcurrencyToken));
        Assert.Equal(HttpStatusCode.Forbidden, adminReopen.StatusCode);

        var reopened = await Body<PreDefenseLockStatusDto>(await staff.PostAsJsonAsync(
            $"/api/v1/projects/{s.ProjectId}/final-submission/reopen",
            new ReopenFinalLockRequest("Student uploaded wrong appendix before evaluation", locked.DraftConcurrencyToken)));
        Assert.Equal("OPEN", reopened.LockWorkflowStatus);
        Assert.Equal("ACTIVE", reopened.ProjectStatus);

        // Expire final submission deadline -> ADMIN cannot Force-Lock (403); Lead Staff CAN Force-Lock after deadline
        await using (var db = database.CreateContext())
        {
            var period = await db.ProjectPeriods.SingleAsync(p => p.Id == finalPeriodId);
            period.EndAt = Now.AddMinutes(-5);
            await db.SaveChangesAsync();
        }
        var adminForce = await admin.PostAsJsonAsync($"/api/v1/projects/{s.ProjectId}/final-submission/force-lock",
            new ForceLockFinalRequest("Admin force-lock attempt", reopened.DraftConcurrencyToken));
        Assert.Equal(HttpStatusCode.Forbidden, adminForce.StatusCode);

        var forceLocked = await Body<PreDefenseLockStatusDto>(await staff.PostAsJsonAsync(
            $"/api/v1/projects/{s.ProjectId}/final-submission/force-lock",
            new ForceLockFinalRequest("Deadline passed while supervisor on leave", reopened.DraftConcurrencyToken)));
        Assert.Equal("LOCKED", forceLocked.LockWorkflowStatus);
        Assert.Equal(s.Scope.Users.Staff, forceLocked.ForceLockedBy);

        // Once evaluation starts (assignment exists), Reopen MUST return 409 EVALUATION_ALREADY_STARTED
        var schemeDraft = await Body<EvaluationSchemeDto>(await staff.PostAsJsonAsync("/api/v1/evaluation-schemes",
            new SaveEvaluationSchemeRequest(s.ProjectId, s.PeriodId, "Reopen Lock Guard Scheme", 5,
                [new("Common", "COMMON", null, s.RubricId, 100, 100, 1, Stage: "DEFENSE")])));
        var scheme = await Body<EvaluationSchemeDto>(await staff.PostAsJsonAsync($"/api/v1/evaluation-schemes/{schemeDraft.Id}/publish",
            new SchemeTokenRequest(schemeDraft.ConcurrencyToken)));
        await Body<EvaluationAssignmentDto>(await staff.PostAsJsonAsync($"/api/v1/projects/{s.ProjectId}/evaluation-assignments",
            new AssignEvaluatorRequest(s.Scope.Users.NewLecturer, s.PeriodId, "LECTURER", "COMMON", null, null, scheme.Components.Single().Id)));

        var blockedReopen = await staff.PostAsJsonAsync($"/api/v1/projects/{s.ProjectId}/final-submission/reopen",
            new ReopenFinalLockRequest("Too late after evaluator assigned", forceLocked.DraftConcurrencyToken));
        Assert.Equal(HttpStatusCode.Conflict, blockedReopen.StatusCode);
        Assert.Equal("EVALUATION_ALREADY_STARTED", await ReadProblemCode(blockedReopen));
    }

    [Fact]
    public async Task W3_03_Mentor_reads_common_and_own_major_final_files_and_gets_403_on_cross_major_download()
    {
        var s = await database.Seed(false);
        var (primarySupId, itMajorId, _, mktMajorId, mktStudentId, _) = await ConfigureInterdisciplinaryProjectAsync(database, s, "ACTIVE");

        long finalPeriodId;
        await using (var db = database.CreateContext())
        {
            var finalPeriod = new M.ProjectPeriod
            {
                AcademicSemesterId = s.Scope.SemesterId,
                Code = "FINM_" + Guid.NewGuid().ToString("N")[..5],
                Name = "Final Scope W3",
                PeriodType = "FINAL_SUBMISSION",
                Status = "ACTIVE",
                StartAt = Now.AddDays(-1),
                EndAt = Now.AddDays(2)
            };
            var execPeriod = new M.ProjectPeriod
            {
                AcademicSemesterId = s.Scope.SemesterId,
                Code = "EXECM_" + Guid.NewGuid().ToString("N")[..5],
                Name = "Execution Scope W3",
                PeriodType = "EXECUTION",
                Status = "ACTIVE",
                StartAt = Now.AddDays(-10),
                EndAt = Now.AddDays(10)
            };
            db.ProjectPeriods.AddRange(finalPeriod, execPeriod);
            var mentorProfile = new M.SupervisorProfile { UserId = s.Scope.Users.OtherLecturer };
            db.SupervisorProfiles.Add(mentorProfile);
            await db.SaveChangesAsync();
            finalPeriodId = finalPeriod.Id;
            var mentorReq = new M.SupervisorRequest
            {
                ProjectId = s.ProjectId,
                SupervisorProfileId = mentorProfile.Id,
                RequestedBy = s.Scope.Users.Student,
                Status = "ACCEPTED",
                RequestedAt = Now.AddDays(-10),
                RespondedAt = Now.AddDays(-9)
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
                AssignedAt = Now.AddDays(-9)
            });
            await db.SaveChangesAsync();
        }

        using var app = new EvaluationFactory(database);
        using var leader = app.CreateAuthenticatedClient(s.Scope.Users.Student, roles: [AppRoles.Student]);
        using var mktStudentClient = app.CreateAuthenticatedClient(mktStudentId, roles: [AppRoles.Student]);
        using var staff = app.CreateAuthenticatedClient(s.Scope.Users.Staff, roles: [AppRoles.DepartmentStaff]);
        using var primary = app.CreateAuthenticatedClient(primarySupId, roles: [AppRoles.Lecturer]);
        using var mktMentor = app.CreateAuthenticatedClient(s.Scope.Users.OtherLecturer, roles: [AppRoles.Lecturer]);

        var dCommon = await Body<DeliverableDto>(await leader.PostAsJsonAsync($"/api/v1/projects/{s.ProjectId}/deliverables",
            new SaveDeliverableRequest(null, "Integration Report", null, "REPORT", Now.AddDays(1), "COMMON", null, null)));
        var dIt = await Body<DeliverableDto>(await leader.PostAsJsonAsync($"/api/v1/projects/{s.ProjectId}/deliverables",
            new SaveDeliverableRequest(null, "IT Architecture", null, "REPORT", Now.AddDays(1), "MAJOR_SPECIFIC", itMajorId, null)));
        var dMkt = await Body<DeliverableDto>(await leader.PostAsJsonAsync($"/api/v1/projects/{s.ProjectId}/deliverables",
            new SaveDeliverableRequest(null, "MKT Campaign", null, "REPORT", Now.AddDays(1), "MAJOR_SPECIFIC", mktMajorId, null)));

        var vCommon = await UploadVersionAsync(leader, dCommon.Id, 0, "COMMON content", "common.txt");
        var vIt = await UploadVersionAsync(leader, dIt.Id, 0, "IT secret content", "it.txt");
        var vMkt = await UploadVersionAsync(mktStudentClient, dMkt.Id, 0, "MKT campaign content", "mkt.txt");

        var draft = await Body<FinalSubmissionDraftDto>(await leader.PostAsJsonAsync($"/api/v1/projects/{s.ProjectId}/final-submission-draft",
            new CreateFinalSubmissionDraftRequest(finalPeriodId, "All scopes", [vCommon.Id, vIt.Id, vMkt.Id])));
        await Body<FinalRequirementsDto>(await staff.PutAsJsonAsync($"/api/v1/projects/{s.ProjectId}/final-submission/requirements",
            new ConfigureFinalRequirementsRequest([dCommon.Id, dIt.Id, dMkt.Id], null)));

        var req = await Body<PreDefenseLockStatusDto>(await leader.PostAsJsonAsync($"/api/v1/projects/{s.ProjectId}/final-submission/request-lock",
            new RequestFinalLockRequest(draft.ConcurrencyToken)));
        await Body<PreDefenseLockStatusDto>(await primary.PostAsJsonAsync($"/api/v1/projects/{s.ProjectId}/final-submission/supervisor-lock",
            new SupervisorFinalLockDecisionRequest("LOCK", "Locked", req.DraftConcurrencyToken)));

        // Positive: MKT Mentor sees COMMON + MKT items, NEVER IT item
        var mentorView = await Body<FinalSubmissionDto>(await mktMentor.GetAsync($"/api/v1/projects/{s.ProjectId}/final-submission"));
        Assert.Equal(2, mentorView.Items.Count);
        Assert.Contains(mentorView.Items, i => i.DeliverableVersionId == vCommon.Id);
        Assert.Contains(mentorView.Items, i => i.DeliverableVersionId == vMkt.Id);
        Assert.DoesNotContain(mentorView.Items, i => i.DeliverableVersionId == vIt.Id);

        // Positive: MKT Mentor can download COMMON & MKT files
        var dlCommon = await mktMentor.GetAsync($"/api/v1/projects/{s.ProjectId}/final-submission/files/{vCommon.Files[0].Id}/download");
        Assert.Equal(HttpStatusCode.OK, dlCommon.StatusCode);
        var dlMkt = await mktMentor.GetAsync($"/api/v1/projects/{s.ProjectId}/final-submission/files/{vMkt.Files[0].Id}/download");
        Assert.Equal(HttpStatusCode.OK, dlMkt.StatusCode);

        // Negative: MKT Mentor trying to download IT major file -> 403 SCOPE_ACCESS_DENIED
        var dlIt = await mktMentor.GetAsync($"/api/v1/projects/{s.ProjectId}/final-submission/files/{vIt.Files[0].Id}/download");
        Assert.Equal(HttpStatusCode.Forbidden, dlIt.StatusCode);
        Assert.Equal(WorkflowErrorCodes.ScopeAccessDenied, await ReadProblemCode(dlIt));
    }

    [Fact]
    public async Task W3_04_05_Participating_dept_roster_cross_dept_similarity_cold_template_and_common_scoring()
    {
        var s = await database.Seed(true);
        var (_, itMajorId, mktDeptId, mktMajorId, _, mktStaffId) = await ConfigureInterdisciplinaryProjectAsync(database, s, "FINAL_SUBMISSION");

        var uniqueTopicTitle = "Quantum Smart Supply Chain Platform " + Guid.NewGuid().ToString("N")[..8];
        long foreignDeptId;
        await using (var db = database.CreateContext())
        {
            var project = await db.Projects.SingleAsync(p => p.Id == s.ProjectId);
            project.Title = uniqueTopicTitle;

            // Give OtherLecturer (MKT dept) same org as Lead dept and seed a REGISTRATION period for similarity window
            var leadDept = await db.Departments.SingleAsync(d => d.Id == s.Scope.Users.DepartmentId);
            var mktDept = await db.Departments.SingleAsync(d => d.Id == mktDeptId);
            mktDept.OrganizationId = leadDept.OrganizationId;

            db.ProjectPeriods.Add(new M.ProjectPeriod
            {
                AcademicSemesterId = s.Scope.SemesterId,
                Code = "REGW3_" + Guid.NewGuid().ToString("N")[..5],
                Name = "Registration W3",
                PeriodType = "REGISTRATION",
                Status = "ACTIVE",
                StartAt = Now.AddDays(-30),
                EndAt = Now.AddDays(-15)
            });

            var foreignOrg = new M.Organization { Code = "FORG_" + Guid.NewGuid().ToString("N")[..6], Name = "Foreign Org", IsActive = true };
            var foreignDept = new M.Department { Code = "FDEPT_" + Guid.NewGuid().ToString("N")[..6], Name = "Foreign Dept", Organization = foreignOrg, IsActive = true };
            db.Add(foreignDept);
            await db.SaveChangesAsync();
            foreignDeptId = foreignDept.Id;
        }
        await SeedConfirmedCommitteeAsync(database, s.ProjectId, s.Scope.Users.Staff,
            (s.Scope.Users.NewLecturer, itMajorId, "CHAIR"),
            (s.Scope.Users.OtherLecturer, mktMajorId, "MEMBER"));

        using var app = new EvaluationFactory(database);
        using var leadStaff = app.CreateAuthenticatedClient(s.Scope.Users.Staff, roles: [AppRoles.DepartmentStaff]);
        using var leader = app.CreateAuthenticatedClient(s.Scope.Users.Student, roles: [AppRoles.Student]);
        using var mktStaff = app.CreateAuthenticatedClient(mktStaffId, roles: [AppRoles.DepartmentStaff]);
        using var mktLecturer = app.CreateAuthenticatedClient(s.Scope.Users.OtherLecturer, roles: [AppRoles.Lecturer]);
        using var admin = app.CreateAuthenticatedClient(s.Scope.Users.Admin, roles: [AppRoles.Admin]);

        // TK-W3-04: Participating MKT Staff exports IT_MKT team roster -> contains BOTH IT and MKT students; ADMIN gets 403
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync($"/api/v1/teams/export?semesterId={s.Scope.SemesterId}")).StatusCode);
        var exportResp = await mktStaff.GetAsync($"/api/v1/teams/export?semesterId={s.Scope.SemesterId}");
        Assert.Equal(HttpStatusCode.OK, exportResp.StatusCode);
        using (var wb = new XLWorkbook(new MemoryStream(await exportResp.Content.ReadAsByteArrayAsync())))
        {
            var ws = wb.Worksheet(1);
            // Header + 2 active members (IT leader + MKT member)
            Assert.Equal(3, ws.LastRowUsed()!.RowNumber());
        }

        // TK-W3-04: Topic similarity detects cross-dept IT_MKT title AND blocks foreign-org leadDepartmentId with 403
        var sim = await Body<TopicSimilarityResultDto>(await mktStaff.GetAsync(
            $"/api/v1/topics/similarity-check?title={Uri.EscapeDataString(uniqueTopicTitle)}&leadDepartmentId={mktDeptId}"));
        Assert.True(sim.ExactMatch is not null || sim.Similar.Count > 0, "Expected cross-department similarity match for IT_MKT project.");

        var foreignSim = await mktStaff.GetAsync(
            $"/api/v1/topics/similarity-check?title={Uri.EscapeDataString(uniqueTopicTitle)}&leadDepartmentId={foreignDeptId}");
        Assert.Equal(HttpStatusCode.Forbidden, foreignSim.StatusCode);

        // TK-W3-05: Participating MKT Staff creates ColdReportTemplate -> Lead IT Staff can read it org-wide
        var mktTemplate = await Body<ColdTemplateDto>(await mktStaff.PostAsJsonAsync("/api/v1/cold-report-templates",
            new CreateColdTemplateRequest(mktMajorId, s.PeriodId, "MKT Cold Checklist W3", [new("REPORT", "Marketing Pilot Report")])));
        var leadTemplates = await Body<PagedResult<ColdTemplateDto>>(await leadStaff.GetAsync($"/api/v1/cold-report-templates?majorId={mktMajorId}&projectPeriodId={s.PeriodId}"));
        Assert.Contains(leadTemplates.Items, t => t.Id == mktTemplate.Id);

        // Lock Cold package, then Participating MKT Lecturer scores COMMON component end-to-end
        await LockColdPackageViaApiAsync(database, leadStaff, leader, s.ProjectId, s.PeriodId, s.Scope.Users.Student);

        var schemeDraft = await Body<EvaluationSchemeDto>(await leadStaff.PostAsJsonAsync("/api/v1/evaluation-schemes",
            new SaveEvaluationSchemeRequest(s.ProjectId, s.PeriodId, "Cross-Dept Common Scoring", 5,
                [new("Common Cold", "COMMON", null, s.RubricId, 100, 100, 1, Stage: "COLD")])));
        var scheme = await Body<EvaluationSchemeDto>(await leadStaff.PostAsJsonAsync($"/api/v1/evaluation-schemes/{schemeDraft.Id}/publish",
            new SchemeTokenRequest(schemeDraft.ConcurrencyToken)));
        var compId = scheme.Components.Single().Id;

        var assigned = await Body<EvaluationAssignmentDto>(await leadStaff.PostAsJsonAsync($"/api/v1/projects/{s.ProjectId}/evaluation-assignments",
            new AssignEvaluatorRequest(s.Scope.Users.OtherLecturer, s.PeriodId, "LECTURER", "COMMON", null, null, compId)));
        var draft = await Body<EvaluationDraftDto>(await mktLecturer.PostAsync($"/api/v1/evaluation-assignments/{assigned.Id}/evaluation", null));
        draft = await Body<EvaluationDraftDto>(await mktLecturer.PutAsJsonAsync($"/api/v1/evaluations/{draft.Id}/draft",
            new SaveEvaluationDraftRequest(draft.ConcurrencyToken, "Solid interdisciplinary cold review",
                [new(s.Criteria[0], 8, null), new(s.Criteria[1], 16, null)])));
        var finalized = await Body<EvaluationDraftDto>(await mktLecturer.PostAsJsonAsync($"/api/v1/evaluations/{draft.Id}/finalize",
            new { draft.ConcurrencyToken }));
        Assert.Equal("FINALIZED", finalized.Status);
    }

    [Fact]
    public async Task W3_07_Atomic_replace_evaluator_preserves_finalized_cold_and_enforces_D17_and_finalized_lock()
    {
        var s = await database.Seed(true);
        long majorId, primarySupId, eval2Id, eval3Id;
        await using (var db = database.CreateContext())
        {
            majorId = await db.ProjectMajors.Where(pm => pm.ProjectId == s.ProjectId).Select(pm => pm.MajorId).FirstAsync();
            primarySupId = await db.SupervisorAssignments.Where(a => a.ProjectId == s.ProjectId && a.IsPrimary)
                .Select(a => a.SupervisorProfile.UserId).SingleAsync();
            var lecturerRoleId = await db.Roles.Where(r => r.Code == AppRoles.Lecturer).Select(r => r.Id).SingleAsync();
            var u2 = new M.User { Email = $"rep2_{Guid.NewGuid():N}@e2e.invalid", FullName = "Eval 2", PasswordHash = "unused", Status = "ACTIVE", DepartmentId = s.Scope.Users.DepartmentId, UserRoleUsers = [new() { RoleId = lecturerRoleId }] };
            var u3 = new M.User { Email = $"rep3_{Guid.NewGuid():N}@e2e.invalid", FullName = "Eval 3", PasswordHash = "unused", Status = "ACTIVE", DepartmentId = s.Scope.Users.DepartmentId, UserRoleUsers = [new() { RoleId = lecturerRoleId }] };
            db.Users.AddRange(u2, u3);
            await db.SaveChangesAsync();
            eval2Id = u2.Id;
            eval3Id = u3.Id;
        }
        await SeedConfirmedCommitteeAsync(database, s.ProjectId, s.Scope.Users.Staff,
            (s.Scope.Users.NewLecturer, majorId, "CHAIR"),
            (eval2Id, majorId, "MEMBER"),
            (eval3Id, majorId, "MEMBER"));

        using var app = new EvaluationFactory(database);
        using var staff = app.CreateAuthenticatedClient(s.Scope.Users.Staff, roles: [AppRoles.DepartmentStaff]);
        using var leader = app.CreateAuthenticatedClient(s.Scope.Users.Student, roles: [AppRoles.Student]);
        using var coldEval = app.CreateAuthenticatedClient(s.Scope.Users.NewLecturer, roles: [AppRoles.Lecturer]);

        await LockColdPackageViaApiAsync(database, staff, leader, s.ProjectId, s.PeriodId, s.Scope.Users.Student);

        var schemeDraft = await Body<EvaluationSchemeDto>(await staff.PostAsJsonAsync("/api/v1/evaluation-schemes",
            new SaveEvaluationSchemeRequest(s.ProjectId, s.PeriodId, "Cold + Defense Replacement", 5,
                [new("Cold", "COMMON", null, s.RubricId, 40, 40, 1, Stage: "COLD"),
                 new("Defense", "COMMON", null, s.RubricId, 60, 60, 1, Stage: "DEFENSE")])));
        var scheme = await Body<EvaluationSchemeDto>(await staff.PostAsJsonAsync($"/api/v1/evaluation-schemes/{schemeDraft.Id}/publish",
            new SchemeTokenRequest(schemeDraft.ConcurrencyToken)));
        var coldComp = scheme.Components.Single(c => c.Stage == "COLD");
        var defComp = scheme.Components.Single(c => c.Stage == "DEFENSE");

        var coldAssign = await Body<EvaluationAssignmentDto>(await staff.PostAsJsonAsync($"/api/v1/projects/{s.ProjectId}/evaluation-assignments",
            new AssignEvaluatorRequest(s.Scope.Users.NewLecturer, s.PeriodId, "LECTURER", "COMMON", null, null, coldComp.Id)));
        var defAssign = await Body<EvaluationAssignmentDto>(await staff.PostAsJsonAsync($"/api/v1/projects/{s.ProjectId}/evaluation-assignments",
            new AssignEvaluatorRequest(eval2Id, s.PeriodId, "LECTURER", "COMMON", null, null, defComp.Id)));

        // Finalize COLD evaluation
        var coldDraft = await Body<EvaluationDraftDto>(await coldEval.PostAsync($"/api/v1/evaluation-assignments/{coldAssign.Id}/evaluation", null));
        coldDraft = await Body<EvaluationDraftDto>(await coldEval.PutAsJsonAsync($"/api/v1/evaluations/{coldDraft.Id}/draft",
            new SaveEvaluationDraftRequest(coldDraft.ConcurrencyToken, "Cold finalized", [new(s.Criteria[0], 8, null), new(s.Criteria[1], 16, null)])));
        await Body<EvaluationDraftDto>(await coldEval.PostAsJsonAsync($"/api/v1/evaluations/{coldDraft.Id}/finalize", new { coldDraft.ConcurrencyToken }));

        // Negative: Replacing already-finalized COLD assignment -> 409 FINALIZED_ASSIGNMENT
        var replaceFinalized = await staff.PostAsJsonAsync($"/api/v1/evaluation-assignments/{coldAssign.Id}/replace",
            new ReplaceEvaluatorRequest(eval3Id, coldAssign.ConcurrencyToken, "Try replacing finalized cold"));
        Assert.Equal(HttpStatusCode.Conflict, replaceFinalized.StatusCode);

        // Negative: Replacing DEFENSE assignment with Primary Supervisor -> 409 EVALUATOR_ROLE_CONFLICT (D17)
        var replaceWithPrimary = await staff.PostAsJsonAsync($"/api/v1/evaluation-assignments/{defAssign.Id}/replace",
            new ReplaceEvaluatorRequest(primarySupId, defAssign.ConcurrencyToken, "COI attempt"));
        Assert.Equal(HttpStatusCode.Conflict, replaceWithPrimary.StatusCode);
        Assert.Equal(WorkflowErrorCodes.EvaluatorRoleConflict, await ReadProblemCode(replaceWithPrimary));

        // Positive: Atomic replace unfinalized DEFENSE evaluator -> revokes old, assigns eval3, keeps COLD finalized
        var replaced = await Body<ReplaceEvaluatorResponse>(await staff.PostAsJsonAsync($"/api/v1/evaluation-assignments/{defAssign.Id}/replace",
            new ReplaceEvaluatorRequest(eval3Id, defAssign.ConcurrencyToken, "Original evaluator sick on defense day")));
        Assert.Equal("REVOKED", replaced.RevokedAssignment.Status);
        Assert.Equal("ACTIVE", replaced.ReplacementAssignment.Status);
        Assert.Equal(eval3Id, replaced.ReplacementAssignment.EvaluatorId);

        // Verify list reports HasFinalizedEvaluation=true on COLD and false on new DEFENSE
        var list = await Body<PagedResult<EvaluationAssignmentDto>>(await staff.GetAsync($"/api/v1/projects/{s.ProjectId}/evaluation-assignments?page=1&pageSize=20"));
        Assert.True(list.Items.Single(a => a.Id == coldAssign.Id).HasFinalizedEvaluation);
        Assert.False(list.Items.Single(a => a.Id == replaced.ReplacementAssignment.Id).HasFinalizedEvaluation);
    }

    [Fact]
    public async Task W3_08_09_Official_POL01_validation_bulk_apply_and_QD05_common_defense_warning_without_blocking()
    {
        // Part A: TK-W3-08 Official POL_SINGLE_30_50_20 ProjectWeightPercent guard & BulkApplyByTrack
        var single = await database.Seed(true);
        long singleMajorId;
        await using (var db = database.CreateContext())
        {
            var project = await db.Projects.SingleAsync(p => p.Id == single.ProjectId);
            project.TrackCode = AcademicTrackCodes.ItOnly;
            singleMajorId = await db.ProjectMajors.Where(pm => pm.ProjectId == single.ProjectId).Select(pm => pm.MajorId).FirstAsync();
            await db.SaveChangesAsync();
        }
        using var app = new EvaluationFactory(database);
        using var singleStaff = app.CreateAuthenticatedClient(single.Scope.Users.Staff, roles: [AppRoles.DepartmentStaff]);

        var officialSinglePolicy = new SchemePolicyInput(
            IsDemo: false, ApprovalStatus: "APPROVED", ApprovalReference: "QD-2026-POL01",
            Formula: "POL_SINGLE_30_50_20", CalculationVersion: "POL_V4_DECIMAL",
            RoundingMode: "AWAY_FROM_ZERO", DecimalPlaces: 2, FailingComponentThreshold: 4.00m,
            IndividualGateWeights: [new(1, 20), new(2, 35), new(3, 45)]);

        // Negative: Invalid ProjectWeightPercent (50/50/0 instead of 37.5/62.5/0) -> 400 BadRequest (ValidationException)
        var badWeights = await singleStaff.PostAsJsonAsync("/api/v1/evaluation-schemes",
            new SaveEvaluationSchemeRequest(single.ProjectId, single.PeriodId, "Bad POL-01 Weights", 5,
                [new("Cold", "COMMON", null, single.RubricId, 50m, 30m, 1, Stage: "COLD"),
                 new("Defense", "COMMON", null, single.RubricId, 50m, 50m, 1, Stage: "DEFENSE"),
                 new("Process", "INDIVIDUAL", singleMajorId, single.RubricId, 0m, 20m, 1, Stage: "ON_GOING")],
                Policy: officialSinglePolicy));
        Assert.Equal(HttpStatusCode.BadRequest, badWeights.StatusCode);

        // Positive: Bulk Apply Official POL_SINGLE_30_50_20 across IT_ONLY track
        var bulkReq = new BulkApplyEvaluationSchemeRequest(
            single.PeriodId, AcademicTrackCodes.ItOnly, "Bulk POL-01 IT_ONLY", 5m, officialSinglePolicy,
            [new("Cold 30%", "COMMON", null, single.RubricId, 37.5m, 30m, 1, Stage: "COLD"),
             new("Defense 50%", "COMMON", null, single.RubricId, 62.5m, 50m, 1, Stage: "DEFENSE"),
             new("Individual 20%", "INDIVIDUAL", singleMajorId, single.RubricId, 0m, 20m, 1, Stage: "ON_GOING")],
            AutoPublish: true);
        var bulk1 = await Body<BulkApplyEvaluationSchemeResultDto>(await singleStaff.PostAsJsonAsync("/api/v1/evaluation-schemes/bulk-apply", bulkReq));
        Assert.True(bulk1.AppliedCount >= 1);
        Assert.Contains(single.ProjectId, bulk1.AppliedProjectIds);

        // Second bulk apply skips already-configured project
        var bulk2 = await Body<BulkApplyEvaluationSchemeResultDto>(await singleStaff.PostAsJsonAsync("/api/v1/evaluation-schemes/bulk-apply", bulkReq));
        Assert.True(bulk2.SkippedCount >= 1);

        // Part B: TK-W3-09 QD-05 non-blocking warning when POL_INTER_40_40_20 has COMMON DEFENSE < 4.00 but COMMON aggregate >= 4.00
        var inter = await database.Seed(true);
        var (primarySupId, itMajorId, mktDeptId, mktMajorId, mktStudentId, mktStaffId) = await ConfigureInterdisciplinaryProjectAsync(database, inter, "FINAL_SUBMISSION");
        long eval2Id, mktRubricId;
        long[] mktCriteria, gateIds;
        await using (var db = database.CreateContext())
        {
            var lecturerRoleId = await db.Roles.Where(r => r.Code == AppRoles.Lecturer).Select(r => r.Id).SingleAsync();
            var u2 = new M.User { Email = $"inter_e2_{Guid.NewGuid():N}@e2e.invalid", FullName = "Inter Eval 2", PasswordHash = "unused", Status = "ACTIVE", DepartmentId = mktDeptId, UserRoleUsers = [new() { RoleId = lecturerRoleId }] };
            db.Users.Add(u2);
            var mktRubric = new M.Rubric
            {
                DepartmentId = mktDeptId, AcademicSemesterId = inter.Scope.SemesterId,
                Code = "MKTRUB_" + Guid.NewGuid().ToString("N")[..6], Name = "MKT Rubric W3", IsActive = true, CreatedBy = mktStaffId,
                RubricCriteria =
                [
                    new() { Criterion = new() { Code = "MC1_" + Guid.NewGuid().ToString("N")[..5], Name = "Campaign", IsActive = true }, WeightPercent = 50, MaxScore = 10, SortOrder = 0, IsRequired = true },
                    new() { Criterion = new() { Code = "MC2_" + Guid.NewGuid().ToString("N")[..5], Name = "Analytics", IsActive = true }, WeightPercent = 50, MaxScore = 20, SortOrder = 1, IsRequired = true }
                ]
            };
            db.Rubrics.Add(mktRubric);
            var tpl = new ReviewGateTemplate
            {
                ProjectPeriodId = inter.PeriodId, Code = "GTW3_" + Guid.NewGuid().ToString("N")[..6], Name = "Inter Gates",
                Mode = "INTERDISCIPLINARY", Major = "IT_MKT", Version = 1, Status = "PUBLISHED",
                CreatedBy = inter.Scope.Users.Staff, CreatedAt = Now, UpdatedAt = Now
            };
            db.ReviewGateTemplates.Add(tpl);
            await db.SaveChangesAsync();
            db.Set<RubricVersion>().Add(new() { RubricId = mktRubric.Id, RootRubricId = mktRubric.Id, VersionNumber = 1, Status = "PUBLISHED", ConcurrencyToken = Guid.NewGuid() });
            var g1 = new ProjectGateInstance { ProjectId = inter.ProjectId, TemplateId = tpl.Id, TemplateVersion = 1, Code = "G1", Sequence = 1, Title = "G1", Objective = "G1", Status = "PASSED", CreatedAt = Now };
            var g2 = new ProjectGateInstance { ProjectId = inter.ProjectId, TemplateId = tpl.Id, TemplateVersion = 1, Code = "G2", Sequence = 2, Title = "G2", Objective = "G2", Status = "PASSED", CreatedAt = Now };
            var g3 = new ProjectGateInstance { ProjectId = inter.ProjectId, TemplateId = tpl.Id, TemplateVersion = 1, Code = "G3", Sequence = 3, Title = "G3", Objective = "G3", Status = "PASSED", CreatedAt = Now };
            db.ProjectGateInstances.AddRange(g1, g2, g3);
            await db.SaveChangesAsync();
            eval2Id = u2.Id;
            mktRubricId = mktRubric.Id;
            mktCriteria = mktRubric.RubricCriteria.OrderBy(c => c.SortOrder).Select(c => c.Id).ToArray();
            gateIds = [g1.Id, g2.Id, g3.Id];
        }
        await SeedConfirmedCommitteeAsync(database, inter.ProjectId, inter.Scope.Users.Staff,
            (inter.Scope.Users.NewLecturer, itMajorId, "CHAIR"),
            (eval2Id, mktMajorId, "MEMBER"));
        using var interStaff = app.CreateAuthenticatedClient(inter.Scope.Users.Staff, roles: [AppRoles.DepartmentStaff]);
        using var interLeader = app.CreateAuthenticatedClient(inter.Scope.Users.Student, roles: [AppRoles.Student]);
        using var primary = app.CreateAuthenticatedClient(primarySupId, roles: [AppRoles.Lecturer]);
        using var eval1 = app.CreateAuthenticatedClient(inter.Scope.Users.NewLecturer, roles: [AppRoles.Lecturer]);
        using var eval2 = app.CreateAuthenticatedClient(eval2Id, roles: [AppRoles.Lecturer]);

        await LockColdPackageViaApiAsync(database, interStaff, interLeader, inter.ProjectId, inter.PeriodId, inter.Scope.Users.Student);

        var officialInterPolicy = new SchemePolicyInput(
            IsDemo: false, ApprovalStatus: "APPROVED", ApprovalReference: "QD-2026-POL01-INTER",
            Formula: "POL_INTER_40_40_20", CalculationVersion: "POL_V4_DECIMAL",
            RoundingMode: "AWAY_FROM_ZERO", DecimalPlaces: 2, FailingComponentThreshold: 4.00m,
            IndividualGateWeights: [new(1, 20), new(2, 35), new(3, 45)]);

        var interSchemeDraft = await Body<EvaluationSchemeDto>(await interStaff.PostAsJsonAsync("/api/v1/evaluation-schemes",
            new SaveEvaluationSchemeRequest(inter.ProjectId, inter.PeriodId, "POL-01 Inter QD-05", 5m,
                [new("Common Cold", "COMMON", null, inter.RubricId, 15m, 12m, 1, Stage: "COLD", IsIntegration: true),
                 new("Common Defense", "COMMON", null, inter.RubricId, 35m, 28m, 1, Stage: "DEFENSE", IsIntegration: true),
                 new("Major IT", "MAJOR_SPECIFIC", itMajorId, inter.RubricId, 25m, 40m, 1, Stage: "DEFENSE"),
                 new("Major MKT", "MAJOR_SPECIFIC", mktMajorId, mktRubricId, 25m, 40m, 1, Stage: "DEFENSE"),
                 new("Ind IT", "INDIVIDUAL", itMajorId, inter.RubricId, 0m, 20m, 1, Stage: "ON_GOING"),
                 new("Ind MKT", "INDIVIDUAL", mktMajorId, mktRubricId, 0m, 20m, 1, Stage: "ON_GOING")],
                Policy: officialInterPolicy)));
        var interScheme = await Body<EvaluationSchemeDto>(await interStaff.PostAsJsonAsync($"/api/v1/evaluation-schemes/{interSchemeDraft.Id}/publish",
            new SchemeTokenRequest(interSchemeDraft.ConcurrencyToken)));

        // Record & finalize 20% Individual Process G1-G3 = 8.00 for both students
        foreach (var studentId in new[] { inter.Scope.Users.Student, mktStudentId })
        {
            for (var i = 0; i < 3; i++)
            {
                var saved = await Body<GateIndividualProcessScoreItemDto>(await primary.PutAsJsonAsync(
                    $"/api/v1/projects/{inter.ProjectId}/gates/{gateIds[i]}/individual-process-scores/{studentId}",
                    new UpsertIndividualProcessScoreRequest(8.0m, $"Solid G{i + 1}")));
                await Body<GateIndividualProcessScoreItemDto>(await primary.PostAsJsonAsync(
                    $"/api/v1/projects/{inter.ProjectId}/gates/{gateIds[i]}/individual-process-scores/{studentId}/finalize",
                    new FinalizeIndividualProcessScoreRequest(saved.ConcurrencyToken)));
            }
        }

        async Task ScoreComponentAsync(HttpClient client, long evaluatorId, SchemeComponentDto comp, long[] criteriaIds, decimal targetOutOf10)
        {
            var assign = await Body<EvaluationAssignmentDto>(await interStaff.PostAsJsonAsync($"/api/v1/projects/{inter.ProjectId}/evaluation-assignments",
                new AssignEvaluatorRequest(evaluatorId, inter.PeriodId, "LECTURER", comp.Scope, comp.MajorId, null, comp.Id)));
            var ev = await Body<EvaluationDraftDto>(await client.PostAsync($"/api/v1/evaluation-assignments/{assign.Id}/evaluation", null));
            // criteriaIds[0] max=10, criteriaIds[1] max=20 -> proportional score yields targetOutOf10
            ev = await Body<EvaluationDraftDto>(await client.PutAsJsonAsync($"/api/v1/evaluations/{ev.Id}/draft",
                new SaveEvaluationDraftRequest(ev.ConcurrencyToken, "Scored", [new(criteriaIds[0], targetOutOf10, null), new(criteriaIds[1], targetOutOf10 * 2m, null)])));
            await Body<EvaluationDraftDto>(await client.PostAsJsonAsync($"/api/v1/evaluations/{ev.Id}/finalize", new { ev.ConcurrencyToken }));
        }

        // Common Cold = 9.00, Common Defense = 3.50 (< 4.00 floor, but combined Common = (9*12 + 3.5*28)/40 = 5.15 >= 4.00!)
        await ScoreComponentAsync(eval1, inter.Scope.Users.NewLecturer, interScheme.Components.Single(c => c.Name == "Common Cold"), inter.Criteria, 9.0m);
        await ScoreComponentAsync(eval2, eval2Id, interScheme.Components.Single(c => c.Name == "Common Defense"), inter.Criteria, 3.5m);
        await ScoreComponentAsync(eval1, inter.Scope.Users.NewLecturer, interScheme.Components.Single(c => c.Name == "Major IT"), inter.Criteria, 8.0m);
        await ScoreComponentAsync(eval2, eval2Id, interScheme.Components.Single(c => c.Name == "Major MKT"), mktCriteria, 8.0m);

        // Verify workbench exposes CalculationFormula
        var wb = await Body<ProjectScoringWorkbenchDto>(await eval1.GetAsync($"/api/v1/projects/{inter.ProjectId}/scoring-workbench"));
        Assert.Equal("POL_INTER_40_40_20", wb.CalculationFormula);

        // Preview MUST pass (CanPublish=true, Outcome=PASSED) AND include non-blocking QD-05 warning!
        var preview = await Body<ProjectResultPreviewDto>(await interStaff.GetAsync($"/api/v1/projects/{inter.ProjectId}/result/preview"));
        Assert.True(preview.CanPublish, string.Join(", ", preview.Blockers));
        Assert.Equal("PASSED", preview.Outcome);
        Assert.NotNull(preview.Warnings);
        Assert.Contains(preview.Warnings!, w => w.Code == "INTERDISCIPLINARY_DEFENSE_COMMON_BELOW_FLOOR");
    }

    [Fact]
    public async Task W3_10_Optional_defense_room_config_GET_Sunday_capacity_and_24h_change_window()
    {
        var s = await database.Seed(true);
        long majorId, evalId, tinyRoomId, normalRoomId, committeeId;
        await using (var db = database.CreateContext())
        {
            majorId = await db.ProjectMajors.Where(pm => pm.ProjectId == s.ProjectId).Select(pm => pm.MajorId).FirstAsync();
            var lecturerRoleId = await db.Roles.Where(r => r.Code == AppRoles.Lecturer).Select(r => r.Id).SingleAsync();
            var e1 = new M.User { Email = $"d10_1_{Guid.NewGuid():N}@e2e.invalid", FullName = "E1", PasswordHash = "unused", Status = "ACTIVE", DepartmentId = s.Scope.Users.DepartmentId, UserRoleUsers = [new() { RoleId = lecturerRoleId }] };
            var e2 = new M.User { Email = $"d10_2_{Guid.NewGuid():N}@e2e.invalid", FullName = "E2", PasswordHash = "unused", Status = "ACTIVE", DepartmentId = s.Scope.Users.DepartmentId, UserRoleUsers = [new() { RoleId = lecturerRoleId }] };
            var e3 = new M.User { Email = $"d10_3_{Guid.NewGuid():N}@e2e.invalid", FullName = "E3", PasswordHash = "unused", Status = "ACTIVE", DepartmentId = s.Scope.Users.DepartmentId, UserRoleUsers = [new() { RoleId = lecturerRoleId }] };
            db.Users.AddRange(e1, e2, e3);
            var tinyRoom = new Room { Code = "TINY_" + Guid.NewGuid().ToString("N")[..5], Building = "B", Floor = 1, Capacity = 2, Status = "ACTIVE", CreatedAt = Now, UpdatedAt = Now };
            var normalRoom = new Room { Code = "NORM_" + Guid.NewGuid().ToString("N")[..5], Building = "B", Floor = 5, Capacity = 30, Status = "ACTIVE", CreatedAt = Now, UpdatedAt = Now };
            db.Set<Room>().AddRange(tinyRoom, normalRoom);
            await db.SaveChangesAsync();
            evalId = e1.Id;
            tinyRoomId = tinyRoom.Id;
            normalRoomId = normalRoom.Id;

            var committee = new DefenseCommittee
            {
                ProjectId = s.ProjectId, Name = "Confirmed Quorum Committee", Status = "CONFIRMED", CreatedBy = s.Scope.Users.Staff, CreatedAt = Now,
                Members =
                [
                    new() { UserId = e1.Id, MajorId = majorId, Role = "CHAIR" },
                    new() { UserId = e2.Id, MajorId = majorId, Role = "SECRETARY" },
                    new() { UserId = e3.Id, MajorId = majorId, Role = "MEMBER" }
                ]
            };
            db.Set<DefenseCommittee>().Add(committee);
            await db.SaveChangesAsync();
            committeeId = committee.Id;
        }

        using var app = new EvaluationFactory(database);
        using var admin = app.CreateAuthenticatedClient(s.Scope.Users.Admin, roles: [AppRoles.Admin]);
        using var staff = app.CreateAuthenticatedClient(s.Scope.Users.Staff, roles: [AppRoles.DepartmentStaff]);
        using var chair = app.CreateAuthenticatedClient(evalId, roles: [AppRoles.Lecturer]);

        // GET defense-room-scheduling -> default false; PATCH by Admin enables it
        var cfgBefore = await Body<PeriodDefenseRoomSchedulingDto>(await staff.GetAsync($"/api/v1/academic/project-periods/{s.PeriodId}/defense-room-scheduling"));
        Assert.False(cfgBefore.EnableDefenseRoomScheduling);

        var cfgAfter = await Body<PeriodDefenseRoomSchedulingDto>(await admin.PatchAsJsonAsync(
            $"/api/v1/academic/project-periods/{s.PeriodId}/defense-room-scheduling",
            new UpdatePeriodDefenseRoomSchedulingRequest(true, null, 0)));
        Assert.True(cfgAfter.EnableDefenseRoomScheduling);

        var cfgRead = await Body<PeriodDefenseRoomSchedulingDto>(await staff.GetAsync($"/api/v1/academic/project-periods/{s.PeriodId}/defense-room-scheduling"));
        Assert.True(cfgRead.EnableDefenseRoomScheduling);

        // EvaluationFactory clock is pinned to EvaluationDraftDatabaseFixture.Now (2026-09-07 08:00 UTC).
        var baseFuture = new DateOnly(2026, 9, 14); // Monday
        var sundayDate = new DateOnly(2026, 9, 13); // Sunday

        // Negative: Scheduling on Sunday -> 409 DEFENSE_NON_WORKING_DAY
        var sundayResp = await staff.PostAsJsonAsync("/api/v1/defense-scheduling/v4/sessions",
            new ScheduleDefenseV4Request(s.ProjectId, committeeId, normalRoomId, sundayDate, 1));
        Assert.Equal(HttpStatusCode.Conflict, sundayResp.StatusCode);
        Assert.Equal("DEFENSE_NON_WORKING_DAY", await ReadProblemCode(sundayResp));

        // Negative: Room capacity too small -> 409 DEFENSE_ROOM_CAPACITY
        var capResp = await staff.PostAsJsonAsync("/api/v1/defense-scheduling/v4/sessions",
            new ScheduleDefenseV4Request(s.ProjectId, committeeId, tinyRoomId, baseFuture, 1));
        Assert.Equal(HttpStatusCode.Conflict, capResp.StatusCode);
        Assert.Equal("DEFENSE_ROOM_CAPACITY", await ReadProblemCode(capResp));

        // Schedule valid future session, then move its StartAt to +6h from EvaluationFactory clock (Now) -> <24h change request returns 422 CHANGE_REQUEST_WINDOW_EXPIRED
        var scheduled = await Body<DefenseSessionV4Dto>(await staff.PostAsJsonAsync("/api/v1/defense-scheduling/v4/sessions",
            new ScheduleDefenseV4Request(s.ProjectId, committeeId, normalRoomId, baseFuture, 1)));
        await using (var db = database.CreateContext())
        {
            var ds = await db.Set<DefenseSession>().SingleAsync(x => x.Id == scheduled.Id);
            ds.StartAt = Now.AddHours(6);
            ds.EndAt = Now.AddHours(7);
            await db.SaveChangesAsync();
        }
        var nextSlot = DefenseSchedulingRules.SlotV4(baseFuture.AddDays(1), 2, false);
        var lateChange = await chair.PostAsJsonAsync($"/api/v1/defense-sessions/{scheduled.Id}/change-requests",
            new CreateDefenseChangeRequest(normalRoomId, nextSlot.Start, nextSlot.End, "ROOM_FACILITY_ISSUE", "Within 24 hours change attempt", null));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, lateChange.StatusCode);
        Assert.Equal("CHANGE_REQUEST_WINDOW_EXPIRED", await ReadProblemCode(lateChange));
    }

    [Fact]
    public async Task W3_11_12_Primary_supervisor_published_student_results_and_supervisor_recommendations_disabled_404()
    {
        var s = await database.Seed(true);
        long majorId, primarySupId;
        await using (var db = database.CreateContext())
        {
            majorId = await db.ProjectMajors.Where(pm => pm.ProjectId == s.ProjectId).Select(pm => pm.MajorId).FirstAsync();
            primarySupId = await db.SupervisorAssignments.Where(a => a.ProjectId == s.ProjectId && a.IsPrimary)
                .Select(a => a.SupervisorProfile.UserId).SingleAsync();
        }
        await SeedConfirmedCommitteeAsync(database, s.ProjectId, s.Scope.Users.Staff,
            (s.Scope.Users.NewLecturer, majorId, "CHAIR"));

        using var app = new EvaluationFactory(database);
        using var staff = app.CreateAuthenticatedClient(s.Scope.Users.Staff, roles: [AppRoles.DepartmentStaff]);
        using var leader = app.CreateAuthenticatedClient(s.Scope.Users.Student, roles: [AppRoles.Student]);
        using var primary = app.CreateAuthenticatedClient(primarySupId, roles: [AppRoles.Lecturer]);
        using var eval = app.CreateAuthenticatedClient(s.Scope.Users.NewLecturer, roles: [AppRoles.Lecturer]);

        await LockColdPackageViaApiAsync(database, staff, leader, s.ProjectId, s.PeriodId, s.Scope.Users.Student);

        // TK-W3-12: SupervisorRecommendations is disabled by default -> 404 FEATURE_DISABLED
        var recResp = await staff.GetAsync($"/api/v1/projects/{s.ProjectId}/supervisor-recommendations");
        Assert.Equal(HttpStatusCode.NotFound, recResp.StatusCode);
        Assert.Equal("FEATURE_DISABLED", await ReadProblemCode(recResp));

        // TK-W3-11 Negative: Before publish, Primary Supervisor gets 403 RESULT_NOT_PUBLISHED
        var prePublish = await primary.GetAsync($"/api/v1/projects/{s.ProjectId}/student-results");
        Assert.Equal(HttpStatusCode.Forbidden, prePublish.StatusCode);
        Assert.Equal("RESULT_NOT_PUBLISHED", await ReadProblemCode(prePublish));

        // Publish official result (using COLD component so no separate pre-defense Cold step is required)
        var schemeDraft = await Body<EvaluationSchemeDto>(await staff.PostAsJsonAsync("/api/v1/evaluation-schemes",
            new SaveEvaluationSchemeRequest(s.ProjectId, s.PeriodId, "W3-11 Published Result Scheme", 5m,
                [new("Cold Review", "COMMON", null, s.RubricId, 100m, 100m, 1, Stage: "COLD")],
                Policy: new SchemePolicyInput(IsDemo: false, ApprovalStatus: "APPROVED", ApprovalReference: "QD-2026-W3-11"))));
        var scheme = await Body<EvaluationSchemeDto>(await staff.PostAsJsonAsync($"/api/v1/evaluation-schemes/{schemeDraft.Id}/publish",
            new SchemeTokenRequest(schemeDraft.ConcurrencyToken)));
        var assign = await Body<EvaluationAssignmentDto>(await staff.PostAsJsonAsync($"/api/v1/projects/{s.ProjectId}/evaluation-assignments",
            new AssignEvaluatorRequest(s.Scope.Users.NewLecturer, s.PeriodId, "LECTURER", "COMMON", null, null, scheme.Components.Single().Id)));
        var ev = await Body<EvaluationDraftDto>(await eval.PostAsync($"/api/v1/evaluation-assignments/{assign.Id}/evaluation", null));
        ev = await Body<EvaluationDraftDto>(await eval.PutAsJsonAsync($"/api/v1/evaluations/{ev.Id}/draft",
            new SaveEvaluationDraftRequest(ev.ConcurrencyToken, "Great work", [new(s.Criteria[0], 8, null), new(s.Criteria[1], 16, null)])));
        await Body<EvaluationDraftDto>(await eval.PostAsJsonAsync($"/api/v1/evaluations/{ev.Id}/finalize", new { ev.ConcurrencyToken }));

        var preview = await Body<ProjectResultPreviewDto>(await staff.GetAsync($"/api/v1/projects/{s.ProjectId}/result/preview"));
        await Body<BatchPublishResultDto>(await staff.PostAsJsonAsync($"/api/v1/projects/{s.ProjectId}/results/publish-batch",
            new BatchPublishResultRequest(preview.ConfirmationToken)));

        // TK-W3-11 Positive: After publish, Primary Supervisor reads /student-results with SnapshotJson == "{}" (N6 safe)
        var publishedList = await Body<StudentResultDto[]>(await primary.GetAsync($"/api/v1/projects/{s.ProjectId}/student-results"));
        Assert.NotEmpty(publishedList);
        Assert.All(publishedList, r => Assert.Equal("{}", r.SnapshotJson));
    }
}
