using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.AcademicBlueprints;
using AIPMS.Application.Features.Projects.DTOs;
using AIPMS.Application.Features.WorkingAgreements;
using AIPMS.Domain.Teams;
using AIPMS.Infrastructure.Persistence.Models;
using AIPMS.IntegrationTests.Evaluations;
using Microsoft.EntityFrameworkCore;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.IntegrationTests.Wave1;

public sealed class Wave1CoreGovernanceIntegrationTests(EvaluationDraftDatabaseFixture database)
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
    public async Task W1_01_and_W1_18_Admin_academic_boundary_and_force_activate_work()
    {
        var s = await database.Seed(false);
        await using (var db = database.CreateContext())
        {
            var project = await db.Projects.SingleAsync(p => p.Id == s.ProjectId);
            project.Status = "SUPERVISOR_PENDING";
            // Remove existing primary assignment to test 409 SUPERVISION_INCOMPLETE first
            var existing = await db.SupervisorAssignments.Where(a => a.ProjectId == s.ProjectId && a.EndedAt == null).ToListAsync();
            foreach (var a in existing) a.EndedAt = Now.AddDays(-1);
            await db.SaveChangesAsync();
        }

        using var app = new EvaluationFactory(database);
        using var admin = app.CreateAuthenticatedClient(s.Scope.Users.Admin, roles: ["ADMIN"]);
        using var leadStaff = app.CreateAuthenticatedClient(s.Scope.Users.Staff, roles: ["DEPARTMENT_STAFF"]);

        // Negative (TK-W1-01): Pure ADMIN cannot force-activate (403)
        var adminRes = await admin.PostAsJsonAsync(
            $"/api/v1/projects/{s.ProjectId}/working-agreement/force-activate",
            new ForceActivateProjectRequest("Admin trying to force activate academic project"));
        Assert.Equal(HttpStatusCode.Forbidden, adminRes.StatusCode);

        // Negative (TK-W1-18): Reason too short => 422 REASON_REQUIRED
        var shortRes = await leadStaff.PostAsJsonAsync(
            $"/api/v1/projects/{s.ProjectId}/working-agreement/force-activate",
            new ForceActivateProjectRequest("too short"));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, shortRes.StatusCode);
        Assert.Equal(WorkflowErrorCodes.ReasonRequired, await ReadProblemCode(shortRes));

        // Negative (TK-W1-18): Missing PRIMARY supervisor => 409 SUPERVISION_INCOMPLETE
        var noSupRes = await leadStaff.PostAsJsonAsync(
            $"/api/v1/projects/{s.ProjectId}/working-agreement/force-activate",
            new ForceActivateProjectRequest("Department override reason with more than fifteen characters"));
        Assert.Equal(HttpStatusCode.Conflict, noSupRes.StatusCode);
        Assert.Equal(WorkflowErrorCodes.SupervisionIncomplete, await ReadProblemCode(noSupRes));

        // Restore active PRIMARY supervisor assignment
        await using (var db = database.CreateContext())
        {
            var latest = await db.SupervisorAssignments.Where(a => a.ProjectId == s.ProjectId).OrderByDescending(a => a.Id).FirstAsync();
            latest.EndedAt = null;
            await db.SaveChangesAsync();
        }

        // Positive (TK-W1-18): Lead Staff force-activates SUPERVISOR_PENDING -> ACTIVE
        var ok = await Body<ForceActivateProjectResponse>(await leadStaff.PostAsJsonAsync(
            $"/api/v1/projects/{s.ProjectId}/working-agreement/force-activate",
            new ForceActivateProjectRequest("Khoa xac nhan kich hoat ngoai le cho nhom trien khai dung tien do")));
        Assert.Equal("ACTIVE", ok.Status);

        await using (var db = database.CreateContext())
        {
            Assert.Equal("ACTIVE", (await db.Projects.SingleAsync(p => p.Id == s.ProjectId)).Status);
            Assert.True(await db.AuditLogs.AnyAsync(a => a.Action == "PROJECT_FORCE_ACTIVATED" && a.EntityId == s.ProjectId.ToString()));
        }
    }

    [Fact]
    public async Task W1_11_Manual_team_lock_is_disabled_and_W1_15_track_presets_return_valid_presets()
    {
        var s = await database.Seed(false);
        long teamId;
        await using (var db = database.CreateContext())
        {
            teamId = await db.Projects.Where(p => p.Id == s.ProjectId).Select(p => p.TeamId).SingleAsync();
        }

        using var app = new EvaluationFactory(database);
        using var student = app.CreateAuthenticatedClient(s.Scope.Users.Student, roles: ["STUDENT"]);

        // Negative (TK-W1-11): Student manual lock returns 409 TEAM_MANUAL_LOCK_DISABLED
        var lockRes = await student.PostAsync($"/api/v1/teams/{teamId}/eligibility/lock", null);
        Assert.Equal(HttpStatusCode.Conflict, lockRes.StatusCode);
        Assert.Equal(WorkflowErrorCodes.TeamManualLockDisabled, await ReadProblemCode(lockRes));

        // Positive (TK-W1-15): Track presets endpoint returns configured tracks for semester
        var presets = await Body<SemesterTrackPresetDto[]>(
            await student.GetAsync($"/api/v1/academic/semesters/{s.Scope.SemesterId}/track-presets"));
        Assert.NotEmpty(presets);
        Assert.Contains(presets, p => p.TrackCode is AcademicTrackCodes.ItOnly or AcademicTrackCodes.MktOnly or AcademicTrackCodes.ItMkt);
    }

    [Fact]
    public async Task W1_17_Proposal_participating_revision_consolidated_notes_and_lead_override_work()
    {
        var s = await database.Seed(false);
        long leadDeptId, mktDeptId, mktStaffId, snapshotId;

        await using (var db = database.CreateContext())
        {
            var itMajorId = await db.ProjectMajors.Where(pm => pm.ProjectId == s.ProjectId).Select(pm => pm.MajorId).FirstAsync();
            leadDeptId = (await db.Majors.SingleAsync(m => m.Id == itMajorId)).DepartmentId;
            mktDeptId = (await db.Users.SingleAsync(u => u.Id == s.Scope.Users.OtherLecturer)).DepartmentId!.Value;

            var mktMajor = new M.Major { DepartmentId = mktDeptId, Code = "MKT_" + Guid.NewGuid().ToString("N")[..4], Name = "Marketing", IsActive = true };
            db.Majors.Add(mktMajor);
            await db.SaveChangesAsync();

            var staffRoleId = await db.Roles.Where(r => r.Code == "DEPARTMENT_STAFF").Select(r => r.Id).SingleAsync();
            var mktStaff = new M.User
            {
                Email = $"w117_mkt_{Guid.NewGuid():N}@e2e.invalid",
                FullName = "MKT Staff W1-17",
                PasswordHash = "unused",
                Status = "ACTIVE",
                DepartmentId = mktDeptId,
                UserRoleUsers = [new() { RoleId = staffRoleId }]
            };
            db.Users.Add(mktStaff);
            db.ProjectMajors.Add(new M.ProjectMajor { ProjectId = s.ProjectId, MajorId = mktMajor.Id });

            var project = await db.Projects.SingleAsync(p => p.Id == s.ProjectId);
            project.Status = "UNDER_REVIEW";

            var snap = await db.Set<ProjectRegistrationSnapshot>().Include(x => x.Decisions).SingleAsync(r => r.ProjectId == s.ProjectId);
            var reg = JsonSerializer.Deserialize<RegistrationEvidence>(snap.SnapshotJson)!;
            snap.SubmittedAt = Now.AddHours(-24); // Only 1 WD elapsed initially
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
                        new(mktMajor.Id, 1, 4, "Marketing Strategy")
                    ]
                },
                DepartmentIds = [leadDeptId, mktDeptId],
                MajorDepartmentIds = new Dictionary<long, long> { [itMajorId] = leadDeptId, [mktMajor.Id] = mktDeptId }
            });
            snap.Decisions.Clear();
            snap.Decisions.Add(new ProjectDepartmentDecision { SnapshotId = snap.Id, DepartmentId = leadDeptId, Decision = "APPROVED", DecidedBy = s.Scope.Users.Staff, DecidedAt = Now.AddHours(-12) });
            snap.Decisions.Add(new ProjectDepartmentDecision { SnapshotId = snap.Id, DepartmentId = mktDeptId, Decision = "PENDING" });
            await db.SaveChangesAsync();
            mktStaffId = mktStaff.Id;
            snapshotId = snap.Id;
        }

        using var app = new EvaluationFactory(database);
        using var leadStaff = app.CreateAuthenticatedClient(s.Scope.Users.Staff, roles: ["DEPARTMENT_STAFF"]);
        using var mktStaffClient = app.CreateAuthenticatedClient(mktStaffId, roles: ["DEPARTMENT_STAFF"]);
        using var admin = app.CreateAuthenticatedClient(s.Scope.Users.Admin, roles: ["ADMIN"]);

        var review0 = await Body<ProjectAcademicReviewDto>(await leadStaff.GetAsync($"/api/v1/projects/{s.ProjectId}/academic-review"));
        Assert.False(review0.CanLeadOverride);

        // Negative: Pure ADMIN cannot lead-override (403 ADMIN_NOT_ACADEMIC_AUTHORITY)
        var adminOverride = await admin.PostAsJsonAsync(
            $"/api/v1/projects/{s.ProjectId}/lead-override-approve",
            new LeadOverrideApproveRequest(review0.ConcurrencyToken, "Admin trying to override interdisciplinary proposal"));
        Assert.Equal(HttpStatusCode.Forbidden, adminOverride.StatusCode);
        Assert.Equal(WorkflowErrorCodes.AdminNotAcademicAuthority, await ReadProblemCode(adminOverride));

        // Negative: Lead override before 3 WD & with no disagreement => 409 LEAD_OVERRIDE_SLA_NOT_ELAPSED
        var tooEarly = await leadStaff.PostAsJsonAsync(
            $"/api/v1/projects/{s.ProjectId}/lead-override-approve",
            new LeadOverrideApproveRequest(review0.ConcurrencyToken, "Lead attempting override before 3 working days have elapsed"));
        Assert.Equal(HttpStatusCode.Conflict, tooEarly.StatusCode);
        Assert.Equal(WorkflowErrorCodes.LeadOverrideSlaNotElapsed, await ReadProblemCode(tooEarly));

        // Negative: Short override reason => 422 OVERRIDE_REASON_TOO_SHORT
        var shortOverride = await leadStaff.PostAsJsonAsync(
            $"/api/v1/projects/{s.ProjectId}/lead-override-approve",
            new LeadOverrideApproveRequest(review0.ConcurrencyToken, "Too short"));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, shortOverride.StatusCode);
        Assert.Equal(WorkflowErrorCodes.OverrideReasonTooShort, await ReadProblemCode(shortOverride));

        // Positive: Participating dept records REVISION_REQUIRED (POL-02)
        var afterMkt = await Body<ProjectAcademicReviewDto>(await mktStaffClient.PostAsJsonAsync(
            $"/api/v1/projects/{s.ProjectId}/department-decisions",
            new DepartmentDecisionRequest(snapshotId, review0.ConcurrencyToken, "REVISION_REQUIRED", "Can bo sung Attribution Limits POL-04")));
        Assert.True(afterMkt.CanLeadOverride);
        Assert.Contains("Attribution Limits", afterMkt.ConsolidatedRevisionNotes);

        // Negative: Participating dept cannot execute Lead Override (403)
        var mktOverride = await mktStaffClient.PostAsJsonAsync(
            $"/api/v1/projects/{s.ProjectId}/lead-override-approve",
            new LeadOverrideApproveRequest(afterMkt.ConcurrencyToken, "Participating department should not be allowed to override"));
        Assert.Equal(HttpStatusCode.Forbidden, mktOverride.StatusCode);

        // Positive: Lead Dept executes Lead Override approval (disagreement resolved by Lead authority)
        var approved = await Body<ProjectDto>(await leadStaff.PostAsJsonAsync(
            $"/api/v1/projects/{s.ProjectId}/lead-override-approve",
            new LeadOverrideApproveRequest(afterMkt.ConcurrencyToken, "Khoa chu tri xac nhan chap thuan vuot quyen theo POL-02 sau khi ra soat")));
        Assert.Contains(approved.Status, new[] { "APPROVED", "SUPERVISOR_PENDING", "ACTIVE" });

        await using (var db = database.CreateContext())
        {
            var snap = await db.Set<ProjectRegistrationSnapshot>().SingleAsync(x => x.Id == snapshotId);
            Assert.True(snap.IsLeadOverride);
            Assert.NotNull(snap.OverriddenBy);
            Assert.True(await db.AuditLogs.AnyAsync(a => a.Action == "PROJECT_PROPOSAL_LEAD_OVERRIDE" && a.EntityId == s.ProjectId.ToString()));
        }
    }
}
