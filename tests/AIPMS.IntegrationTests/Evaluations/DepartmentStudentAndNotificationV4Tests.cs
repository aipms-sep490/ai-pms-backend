using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Academic.DTOs;
using AIPMS.Application.Features.AccountSecurity.DTOs;
using AIPMS.Application.Features.DepartmentStudents;
using AIPMS.Application.Features.ProjectChecklists;
using AIPMS.Application.Features.ProjectHub;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.IntegrationTests.Evaluations;

public sealed class DepartmentStudentAndNotificationV4Tests(EvaluationDraftDatabaseFixture database)
    : IClassFixture<EvaluationDraftDatabaseFixture>
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private static async Task<T> ReadJson<T>(HttpResponseMessage res)
    {
        var text = await res.Content.ReadAsStringAsync();
        Assert.True(res.IsSuccessStatusCode, $"{(int)res.StatusCode}: {text}");
        return JsonSerializer.Deserialize<T>(text, JsonOpts)!;
    }

    private static async Task<string?> ReadProblemCode(HttpResponseMessage res)
    {
        var text = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(text);
        return doc.RootElement.TryGetProperty("code", out var c) ? c.GetString() : null;
    }

    [Fact]
    public async Task Department_students_specializations_self_lock_exports_and_v4_notifications_work()
    {
        var s = await database.Seed(false);
        long majorId, semesterId, otherDeptId, otherMajorId, otherStudentId, otherStaffId, pendingStudentId, gateId, checklistId, supervisorUserId;

        await using (var db = database.CreateContext())
        {
            var project = await db.Projects.Include(p => p.Team).SingleAsync(p => p.Id == s.ProjectId);
            project.Status = "ACTIVE";
            semesterId = project.Team.AcademicSemesterId;
            majorId = await db.Majors.Where(m => m.DepartmentId == s.Scope.Users.DepartmentId).Select(m => m.Id).FirstAsync();

            var dept = await db.Departments.SingleAsync(d => d.Id == s.Scope.Users.DepartmentId);
            var otherDept = new M.Department
            {
                OrganizationId = dept.OrganizationId,
                Code = $"MKT_{Guid.NewGuid():N}"[..16],
                Name = "Khoa Marketing",
                IsActive = true
            };
            db.Departments.Add(otherDept);
            await db.SaveChangesAsync();
            otherDeptId = otherDept.Id;

            var otherMajor = new M.Major
            {
                DepartmentId = otherDeptId,
                Code = $"DM_{Guid.NewGuid():N}"[..12],
                Name = "Digital Marketing",
                IsActive = true
            };
            db.Majors.Add(otherMajor);
            await db.SaveChangesAsync();
            otherMajorId = otherMajor.Id;

            var studentRole = await db.Roles.SingleAsync(r => r.Code == "STUDENT");
            var staffRole = await db.Roles.SingleAsync(r => r.Code == "DEPARTMENT_STAFF");

            var pendingStudent = new M.User
            {
                DepartmentId = s.Scope.Users.DepartmentId,
                MajorId = majorId,
                Email = $"pending_{Guid.NewGuid():N}@fpt.edu.vn",
                StudentCode = $"SE{Random.Shared.Next(100000, 999999)}",
                FullName = "Nguyễn Pending",
                PasswordHash = "unused",
                Status = "ACTIVE",
                AcademicProfileStatus = "PENDING"
            };
            pendingStudent.UserRoleUsers.Add(new M.UserRole { RoleId = studentRole.Id, AssignedBy = s.Scope.Users.Admin });

            var otherStudent = new M.User
            {
                DepartmentId = otherDeptId,
                MajorId = otherMajorId,
                Email = $"other_{Guid.NewGuid():N}@fpt.edu.vn",
                StudentCode = $"MK{Random.Shared.Next(100000, 999999)}",
                FullName = "Trần Khoa Khác",
                PasswordHash = "unused",
                Status = "ACTIVE",
                AcademicProfileStatus = "PENDING"
            };
            otherStudent.UserRoleUsers.Add(new M.UserRole { RoleId = studentRole.Id, AssignedBy = s.Scope.Users.Admin });

            var otherStaff = new M.User
            {
                DepartmentId = otherDeptId,
                Email = $"staff_mkt_{Guid.NewGuid():N}@fpt.edu.vn",
                FullName = "Giáo vụ MKT",
                PasswordHash = "unused",
                Status = "ACTIVE"
            };
            otherStaff.UserRoleUsers.Add(new M.UserRole { RoleId = staffRole.Id, AssignedBy = s.Scope.Users.Admin });

            db.Users.AddRange(pendingStudent, otherStudent, otherStaff);
            await db.SaveChangesAsync();
            pendingStudentId = pendingStudent.Id;
            otherStudentId = otherStudent.Id;
            otherStaffId = otherStaff.Id;

            // Ensure Gate 3 PASSED + Checklist Draft for notification checks
            var gateTpl = new ReviewGateTemplate
            {
                ProjectPeriodId = s.PeriodId,
                Version = 1,
                Code = $"TPL_{Guid.NewGuid():N}"[..16],
                Name = "Gate Template",
                Mode = "SINGLE_MAJOR",
                Major = "SE",
                Status = "PUBLISHED",
                CreatedBy = s.Scope.Users.Staff,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                ConcurrencyToken = Guid.NewGuid()
            };
            db.ReviewGateTemplates.Add(gateTpl);
            await db.SaveChangesAsync();

            var gate = new ProjectGateInstance
            {
                ProjectId = s.ProjectId,
                TemplateId = gateTpl.Id,
                TemplateVersion = 1,
                Code = "G3",
                Sequence = 3,
                Title = "Review Gate 3",
                Objective = "Pre-defense readiness",
                Status = "PASSED",
                BlocksDefense = true,
                WindowStart = DateTime.UtcNow.AddDays(-10),
                WindowEnd = DateTime.UtcNow.AddDays(5),
                ConcurrencyToken = Guid.NewGuid()
            };
            db.ProjectGateInstances.Add(gate);
            await db.SaveChangesAsync();
            gateId = gate.Id;

            var checklist = new ProjectGateChecklist
            {
                ProjectId = s.ProjectId,
                GateInstanceId = gateId,
                Version = 1,
                Status = "DRAFT",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                ConcurrencyToken = Guid.NewGuid()
            };
            db.ProjectGateChecklists.Add(checklist);
            await db.SaveChangesAsync();
            checklistId = checklist.Id;

            supervisorUserId = await db.SupervisorAssignments
                .Where(a => a.ProjectId == s.ProjectId && a.EndedAt == null && a.IsPrimary)
                .Select(a => a.SupervisorProfile.UserId)
                .FirstAsync();
        }

        using var factory = new EvaluationFactory(database);
        using var adminClient = factory.CreateAuthenticatedClient(s.Scope.Users.Admin, roles: ["ADMIN"]);
        using var staffClient = factory.CreateAuthenticatedClient(s.Scope.Users.Staff, roles: ["DEPARTMENT_STAFF"]);
        using var otherStaffClient = factory.CreateAuthenticatedClient(otherStaffId, roles: ["DEPARTMENT_STAFF"]);
        using var pendingStudentClient = factory.CreateAuthenticatedClient(pendingStudentId, roles: ["STUDENT"]);
        using var leaderClient = factory.CreateAuthenticatedClient(s.Scope.Users.Student, roles: ["STUDENT"]);
        using var supervisorClient = factory.CreateAuthenticatedClient(supervisorUserId, roles: ["LECTURER"]);

        // 1. Department Staff creates Specialization in own dept; invalid code -> 400; another dept gets 403 SCOPE_ACCESS_DENIED when editing it
        var invalidSpecCode = await staffClient.PostAsJsonAsync(
            "/api/v1/departments/me/specializations",
            new CreateDepartmentSpecializationRequest(majorId, "---bad", "Invalid Code", null));
        Assert.Equal(HttpStatusCode.BadRequest, invalidSpecCode.StatusCode);

        var spec = await ReadJson<DepartmentSpecializationDto>(await staffClient.PostAsJsonAsync(
            "/api/v1/departments/me/specializations",
            new CreateDepartmentSpecializationRequest(majorId, "DOTNET", ".NET Enterprise", "Backend .NET")));
        Assert.Equal("DOTNET", spec.Code);

        var otherSpec = await ReadJson<DepartmentSpecializationDto>(await otherStaffClient.PostAsJsonAsync(
            "/api/v1/departments/me/specializations",
            new CreateDepartmentSpecializationRequest(otherMajorId, "SEO", "SEO & Performance", null)));

        var crossEdit = await otherStaffClient.PutAsJsonAsync(
            $"/api/v1/departments/me/specializations/{spec.Id}",
            new UpdateDepartmentSpecializationRequest(otherMajorId, "HACK", "Hack", null, true));
        Assert.Equal(HttpStatusCode.Forbidden, crossEdit.StatusCode);
        Assert.Equal("SCOPE_ACCESS_DENIED", await ReadProblemCode(crossEdit));

        // 2. Student self-update: invalid class format -> 400; wrong dept specialization -> 422 INVALID_SPECIALIZATION_FOR_SCOPE; valid -> 200; after VERIFIED -> 409 ACADEMIC_PROFILE_LOCKED
        var badClassRes = await pendingStudentClient.PutAsJsonAsync(
            "/api/v1/users/me/academic-profile",
            new UpdateMyStudentAcademicRequest("###", spec.Id));
        Assert.Equal(HttpStatusCode.BadRequest, badClassRes.StatusCode);

        var badSpecRes = await pendingStudentClient.PutAsJsonAsync(
            "/api/v1/users/me/academic-profile",
            new UpdateMyStudentAcademicRequest("SE1801", otherSpec.Id));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, badSpecRes.StatusCode);
        Assert.Equal("INVALID_SPECIALIZATION_FOR_SCOPE", await ReadProblemCode(badSpecRes));

        var selfUpdated = await ReadJson<AcademicProfileDto>(await pendingStudentClient.PutAsJsonAsync(
            "/api/v1/users/me/academic-profile",
            new UpdateMyStudentAcademicRequest("se1801", spec.Id)));
        Assert.Equal("SE1801", selfUpdated.ClassCode);
        Assert.Equal(spec.Id, selfUpdated.SpecializationId);

        // Verify profile -> student self-update now locked (409 ACADEMIC_PROFILE_LOCKED)
        var verifyRes = await staffClient.PostAsync($"/api/v1/users/{pendingStudentId}/academic-profile/verify", null);
        Assert.Equal(HttpStatusCode.OK, verifyRes.StatusCode);

        var lockedRes = await pendingStudentClient.PutAsJsonAsync(
            "/api/v1/users/me/academic-profile",
            new UpdateMyStudentAcademicRequest("SE1899", spec.Id));
        Assert.Equal(HttpStatusCode.Conflict, lockedRes.StatusCode);
        Assert.Equal("ACADEMIC_PROFILE_LOCKED", await ReadProblemCode(lockedRes));

        // 3. Department Student Directory (both WITHOUT_TEAM and invalid query rejection), 360° Detail, Academic Update, Cross-dept 403, and Scoped Exports
        Assert.Equal(HttpStatusCode.BadRequest, (await staffClient.GetAsync("/api/v1/departments/me/students?teamStatus=BOGUS")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await staffClient.GetAsync("/api/v1/departments/me/students?profileStatus=BOGUS")).StatusCode);

        var dir = await ReadJson<DepartmentStudentDirectoryPageDto>(await staffClient.GetAsync("/api/v1/departments/me/students?teamStatus=WITHOUT_TEAM"));
        Assert.Contains(dir.Items, x => x.UserId == pendingStudentId && x.ClassCode == "SE1801" && x.SpecializationCode == "DOTNET");
        Assert.DoesNotContain(dir.Items, x => x.UserId == otherStudentId);
        Assert.True(dir.Stats.WithoutTeamCount >= 1);

        var detail = await ReadJson<DepartmentStudentDetailDto>(await staffClient.GetAsync($"/api/v1/departments/me/students/{pendingStudentId}"));
        Assert.Equal("SE1801", detail.Summary.ClassCode);

        var crossDetail = await staffClient.GetAsync($"/api/v1/departments/me/students/{otherStudentId}");
        Assert.Equal(HttpStatusCode.Forbidden, crossDetail.StatusCode);
        Assert.Equal("SCOPE_ACCESS_DENIED", await ReadProblemCode(crossDetail));

        var updatedDetail = await ReadJson<DepartmentStudentDetailDto>(await staffClient.PutAsJsonAsync(
            $"/api/v1/departments/me/students/{pendingStudentId}/academic",
            new UpdateDepartmentStudentAcademicRequest(majorId, "SE1805", spec.Id, "BIT_SE_18D", detail.ConcurrencyToken)));
        Assert.Equal("SE1805", updatedDetail.Summary.ClassCode);
        Assert.Equal("BIT_SE_18D", updatedDetail.Summary.CurriculumCode);

        // Department student export + Department team roster export (own dept 200, cross dept 403)
        var deptStudentExport = await staffClient.GetAsync("/api/v1/departments/me/students/export");
        Assert.Equal(HttpStatusCode.OK, deptStudentExport.StatusCode);
        Assert.True((await deptStudentExport.Content.ReadAsByteArrayAsync()).Length > 100);

        var deptRosterExport = await staffClient.GetAsync($"/api/v1/teams/export?semesterId={semesterId}");
        Assert.Equal(HttpStatusCode.OK, deptRosterExport.StatusCode);

        var crossRosterExport = await staffClient.GetAsync($"/api/v1/teams/export?semesterId={semesterId}&departmentId={otherDeptId}");
        Assert.Equal(HttpStatusCode.Forbidden, crossRosterExport.StatusCode);
        Assert.Equal("SCOPE_ACCESS_DENIED", await ReadProblemCode(crossRosterExport));

        // 4. Admin role/department user filters + Account Export (Admin 200, Staff 403)
        var adminUsers = await ReadJson<PagedResult<UserAccountDto>>(await adminClient.GetAsync($"/api/v1/users?role=STUDENT&departmentId={s.Scope.Users.DepartmentId}"));
        Assert.Contains(adminUsers.Items, u => u.Id == pendingStudentId && u.ClassCode == "SE1805");
        Assert.DoesNotContain(adminUsers.Items, u => u.Id == otherStudentId);

        var adminExport = await adminClient.GetAsync($"/api/v1/users/export?role=STUDENT&departmentId={s.Scope.Users.DepartmentId}");
        Assert.Equal(HttpStatusCode.OK, adminExport.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await adminClient.GetAsync("/api/v1/users/export?role=BOGUS")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staffClient.GetAsync("/api/v1/users/export")).StatusCode);

        // 5. V4 Notifications emitted end-to-end (Checklist Published, Critical Comment, Final Lock Request + Return)
        var publishChecklistRes = await staffClient.PostAsync($"/api/v1/projects/{s.ProjectId}/checklists/{checklistId}/publish", null);
        Assert.Equal(HttpStatusCode.OK, publishChecklistRes.StatusCode);

        var critCommentRes = await supervisorClient.PostAsJsonAsync(
            $"/api/v1/projects/{s.ProjectId}/hub/gates/G3/anchored-comments",
            new CreateAnchoredCommentRequest(null, "SRS Section 3.2", "CRITICAL", "Missing non-functional security checklist."));
        Assert.Equal(HttpStatusCode.Created, critCommentRes.StatusCode);

        var reqLockRes = await leaderClient.PostAsync($"/api/v1/projects/{s.ProjectId}/final-submission/request-lock", null);
        Assert.Equal(HttpStatusCode.OK, reqLockRes.StatusCode);

        var returnLockRes = await supervisorClient.PostAsJsonAsync(
            $"/api/v1/projects/{s.ProjectId}/final-submission/supervisor-lock",
            new SupervisorFinalLockDecisionRequest("RETURN_TO_OPEN", "Please upload final demo video."));
        Assert.Equal(HttpStatusCode.OK, returnLockRes.StatusCode);

        await using (var verify = database.CreateContext())
        {
            var types = await verify.Notifications.AsNoTracking()
                .Where(n => n.RelatedEntityId == s.ProjectId || n.RelatedEntityId == gateId)
                .Select(n => n.NotificationType)
                .ToListAsync();

            Assert.Contains("CHECKLIST_PUBLISHED", types);
            Assert.Contains("CRITICAL_ANCHORED_COMMENT_ADDED", types);
            Assert.Contains("FINAL_LOCK_REQUESTED", types);
            Assert.Contains("FINAL_LOCK_RETURNED", types);

            // Critical 2-step Final Lock notifications enqueue SMTP email delivery
            var lockEmailQueued = await verify.Set<NotificationEmailDelivery>().AsNoTracking()
                .AnyAsync(d => d.NotificationRecipient.Notification.NotificationType == "FINAL_LOCK_REQUESTED");
            Assert.True(lockEmailQueued);
        }
    }
}
