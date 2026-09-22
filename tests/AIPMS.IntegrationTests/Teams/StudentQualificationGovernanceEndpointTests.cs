using System.Net;
using System.Net.Http.Json;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.Projects.DTOs;
using AIPMS.Application.Features.StudentQualifications.DTOs;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.IntegrationTests.Teams;

public sealed partial class TeamEndpointTests
{
    [Fact]
    public async Task Qualification_verification_requires_department_staff_in_the_students_scope()
    {
        var scenario = await database.SeedAsync();
        var otherScenario = await database.SeedAsync();
        using var app = new TeamTestFactory(database, scenario);
        using var student = app.CreateAuthenticatedClient(scenario.Students[0]);
        var qualificationId = await AddPendingQualificationAsync(scenario, scenario.Students[0]);
        var staffId = await AddStaffAsync(scenario);
        var outsideStaffId = await AddStaffAsync(otherScenario);
        using var staff = app.CreateAuthenticatedClient(staffId, roles: ["DEPARTMENT_STAFF"]);
        using var outside = app.CreateAuthenticatedClient(outsideStaffId, roles: ["DEPARTMENT_STAFF"]);

        Assert.Equal(HttpStatusCode.Forbidden, (await student.PostAsync(
            $"/api/v1/student-qualifications/{qualificationId}/verify", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await outside.PostAsync(
            $"/api/v1/student-qualifications/{qualificationId}/verify", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await staff.PostAsync(
            $"/api/v1/student-qualifications/{qualificationId}/verify", null)).StatusCode);

        await using var db = database.CreateContext();
        var stored = await db.Set<StudentQualification>().SingleAsync(x => x.Id == qualificationId);
        Assert.Equal("VERIFIED", stored.VerificationStatus);
        Assert.Equal(staffId, stored.VerifiedBy);
        Assert.NotNull(stored.VerifiedAt);
        Assert.True(await db.AuditLogs.AnyAsync(x => x.Action == "STUDENT_QUALIFICATION_VERIFIED"
            && x.EntityId == qualificationId.ToString()));
    }

    [Fact]
    public async Task Concurrent_qualification_verify_and_reject_allow_exactly_one_decision()
    {
        var scenario = await database.SeedAsync();
        using var app = new TeamTestFactory(database, scenario);
        var qualificationId = await AddPendingQualificationAsync(scenario, scenario.Students[0]);
        var staffId = await AddStaffAsync(scenario);
        using var verifier = app.CreateAuthenticatedClient(staffId, roles: ["DEPARTMENT_STAFF"]);
        using var rejecter = app.CreateAuthenticatedClient(staffId, roles: ["DEPARTMENT_STAFF"]);

        var responses = await Task.WhenAll(
            verifier.PostAsync($"/api/v1/student-qualifications/{qualificationId}/verify", null),
            rejecter.PostAsJsonAsync($"/api/v1/student-qualifications/{qualificationId}/reject", new { reason = "Concurrent review" }));

        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
        await using var db = database.CreateContext();
        var stored = await db.Set<StudentQualification>().SingleAsync(x => x.Id == qualificationId);
        Assert.Contains(stored.VerificationStatus, new[] { "VERIFIED", "REJECTED" });
        Assert.Equal(1, await db.AuditLogs.CountAsync(x => x.EntityId == qualificationId.ToString()
            && (x.Action == "STUDENT_QUALIFICATION_VERIFIED" || x.Action == "STUDENT_QUALIFICATION_REJECTED")));
    }

    [Theory]
    [InlineData("verify", "STUDENT_QUALIFICATION_VERIFIED", null)]
    [InlineData("reject", "STUDENT_QUALIFICATION_REJECTED", "Evidence rejected")]
    public async Task Qualification_decision_rolls_back_when_audit_fails(
        string decision,
        string auditAction,
        string? reason)
    {
        var scenario = await database.SeedAsync();
        var qualificationId = await AddPendingQualificationAsync(scenario, scenario.Students[0]);
        var staffId = await AddStaffAsync(scenario);
        using var app = new TeamTestFactory(database, scenario, failAuditAction: auditAction);
        using var staff = app.CreateAuthenticatedClient(staffId, roles: [AppRoles.DepartmentStaff]);

        var response = decision == "verify"
            ? await staff.PostAsync($"/api/v1/student-qualifications/{qualificationId}/verify", null)
            : await staff.PostAsJsonAsync($"/api/v1/student-qualifications/{qualificationId}/reject", new { reason });

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        await using var db = database.CreateContext();
        var stored = await db.Set<StudentQualification>().SingleAsync(x => x.Id == qualificationId);
        Assert.Equal("PENDING_VERIFICATION", stored.VerificationStatus);
        Assert.Null(stored.VerifiedBy);
        Assert.Null(stored.VerifiedAt);
        Assert.Null(stored.RejectionReason);
        Assert.False(await db.AuditLogs.AnyAsync(x => x.EntityId == qualificationId.ToString()
            && x.Action == auditAction));
    }

    [Fact]
    public async Task Qualification_evidence_submission_rolls_back_when_audit_fails()
    {
        var scenario = await database.SeedAsync();
        using var app = new TeamTestFactory(database, scenario,
            failAuditAction: "STUDENT_QUALIFICATION_EVIDENCE_SUBMITTED");
        using var student = app.CreateAuthenticatedClient(scenario.Students[0]);

        var response = await student.PostAsJsonAsync("/api/v1/student-qualifications/me/evidence", new
        {
            qualificationType = "CAPSTONE_READINESS",
            trainingStatus = "TRAINING_COMPLETED",
            certificateNumber = "CERT-ATOMIC-EVIDENCE",
            issuedAt = TeamDatabaseFixture.Now.AddDays(-1),
            expiresAt = TeamDatabaseFixture.Now.AddYears(1)
        });

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        await using var db = database.CreateContext();
        Assert.False(await db.Set<StudentQualification>().AnyAsync(x => x.UserId == scenario.Students[0]
            && x.QualificationType == "CAPSTONE_READINESS"));
        Assert.False(await db.AuditLogs.AnyAsync(x => x.Action == "STUDENT_QUALIFICATION_EVIDENCE_SUBMITTED"));
    }

    [Fact]
    public async Task Qualification_policy_update_rolls_back_when_audit_fails()
    {
        var scenario = await database.SeedAsync();
        using var app = new TeamTestFactory(database, scenario,
            failAuditAction: "PROJECT_PERIOD_QUALIFICATION_POLICY_UPDATED");
        using var admin = app.CreateAuthenticatedClient(scenario.Students[0], roles: [AppRoles.Admin]);

        var response = await admin.PutAsJsonAsync(
            $"/api/v1/academic/project-periods/{scenario.PeriodId}/qualification-policy", new
            {
                requireStudentQualification = true,
                qualificationType = "CAPSTONE_READINESS",
                requireCertificate = true,
                checkExpiration = true
            });

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        await using var db = database.CreateContext();
        Assert.False(await db.Set<ProjectPeriodQualificationPolicy>()
            .AnyAsync(x => x.ProjectPeriodId == scenario.PeriodId));
        Assert.False(await db.AuditLogs.AnyAsync(x => x.Action == "PROJECT_PERIOD_QUALIFICATION_POLICY_UPDATED"
            && x.EntityId == scenario.PeriodId.ToString()));
    }

    [Fact]
    public async Task Qualification_verification_queue_is_scoped_filterable_and_stably_paged()
    {
        var scenario = await database.SeedAsync();
        var outsideScenario = await database.SeedAsync();
        var firstPendingId = await AddPendingQualificationAsync(scenario, scenario.Students[0]);
        var secondPendingId = await AddPendingQualificationAsync(scenario, scenario.Students[1]);
        var verifiedId = await AddPendingQualificationAsync(scenario, scenario.Students[2]);
        var outsideId = await AddPendingQualificationAsync(outsideScenario, outsideScenario.Students[0]);
        var staffId = await AddStaffAsync(scenario);

        await using (var setup = database.CreateContext())
        {
            await setup.Users.Where(x => x.Id == scenario.Students[0]).ExecuteUpdateAsync(x => x
                .SetProperty(user => user.FullName, "Queue Student Alpha")
                .SetProperty(user => user.StudentCode, "QUEUE-ALPHA"));
            await setup.Users.Where(x => x.Id == scenario.Students[1]).ExecuteUpdateAsync(x => x
                .SetProperty(user => user.FullName, "Queue Student Beta")
                .SetProperty(user => user.StudentCode, "QUEUE-BETA"));
            await setup.Set<StudentQualification>().Where(x => x.Id == firstPendingId).ExecuteUpdateAsync(x => x
                .SetProperty(qualification => qualification.UpdatedAt, TeamDatabaseFixture.Now.AddMinutes(2)));
            await setup.Set<StudentQualification>().Where(x => x.Id == secondPendingId).ExecuteUpdateAsync(x => x
                .SetProperty(qualification => qualification.UpdatedAt, TeamDatabaseFixture.Now.AddMinutes(1)));
            await setup.Set<StudentQualification>().Where(x => x.Id == verifiedId).ExecuteUpdateAsync(x => x
                .SetProperty(qualification => qualification.VerificationStatus, "VERIFIED")
                .SetProperty(qualification => qualification.VerifiedBy, staffId)
                .SetProperty(qualification => qualification.VerifiedAt, TeamDatabaseFixture.Now)
                .SetProperty(qualification => qualification.UpdatedAt, TeamDatabaseFixture.Now));
        }

        using var app = new TeamTestFactory(database, scenario);
        using var staff = app.CreateAuthenticatedClient(staffId, roles: [AppRoles.DepartmentStaff]);
        using var student = app.CreateAuthenticatedClient(scenario.Students[0]);
        using var lecturer = app.CreateAuthenticatedClient(staffId, roles: [AppRoles.Lecturer]);

        Assert.Equal(HttpStatusCode.Forbidden,
            (await student.GetAsync("/api/v1/student-qualifications/verification-queue")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await lecturer.GetAsync("/api/v1/student-qualifications/verification-queue")).StatusCode);

        var firstPage = await BodyAsync<PagedResult<StudentQualificationDto>>(await staff.GetAsync(
            "/api/v1/student-qualifications/verification-queue?page=1&pageSize=1"));
        var secondPage = await BodyAsync<PagedResult<StudentQualificationDto>>(await staff.GetAsync(
            "/api/v1/student-qualifications/verification-queue?page=2&pageSize=1"));
        Assert.Equal(firstPendingId, Assert.Single(firstPage.Items).Id);
        Assert.Equal(secondPendingId, Assert.Single(secondPage.Items).Id);
        Assert.Equal(2, firstPage.TotalCount);
        Assert.Equal(2, firstPage.TotalPages);
        Assert.Equal(1, firstPage.PageSize);
        Assert.Equal(2, secondPage.Page);

        var byCode = await BodyAsync<PagedResult<StudentQualificationDto>>(await staff.GetAsync(
            "/api/v1/student-qualifications/verification-queue?search=QUEUE-ALPHA"));
        Assert.Equal(firstPendingId, Assert.Single(byCode.Items).Id);
        Assert.Equal("Queue Student Alpha", byCode.Items[0].FullName);

        var byName = await BodyAsync<PagedResult<StudentQualificationDto>>(await staff.GetAsync(
            "/api/v1/student-qualifications/verification-queue?search=Student%20Beta"));
        Assert.Equal(secondPendingId, Assert.Single(byName.Items).Id);

        var verified = await BodyAsync<PagedResult<StudentQualificationDto>>(await staff.GetAsync(
            "/api/v1/student-qualifications/verification-queue?status=VERIFIED"));
        Assert.Equal(verifiedId, Assert.Single(verified.Items).Id);
        Assert.Equal("VERIFIED", verified.Items[0].VerificationStatus);

        await using var db = database.CreateContext();
        Assert.Equal("PENDING_VERIFICATION", await db.Set<StudentQualification>().Where(x => x.Id == firstPendingId)
            .Select(x => x.VerificationStatus).SingleAsync());
        Assert.Equal("PENDING_VERIFICATION", await db.Set<StudentQualification>().Where(x => x.Id == secondPendingId)
            .Select(x => x.VerificationStatus).SingleAsync());
        Assert.Equal("VERIFIED", await db.Set<StudentQualification>().Where(x => x.Id == verifiedId)
            .Select(x => x.VerificationStatus).SingleAsync());
        Assert.Equal("PENDING_VERIFICATION", await db.Set<StudentQualification>().Where(x => x.Id == outsideId)
            .Select(x => x.VerificationStatus).SingleAsync());
        Assert.False(await db.AuditLogs.AnyAsync(x => x.EntityType == "STUDENT_QUALIFICATION"
            && (x.EntityId == firstPendingId.ToString()
                || x.EntityId == secondPendingId.ToString()
                || x.EntityId == verifiedId.ToString()
                || x.EntityId == outsideId.ToString())));
    }

    [Fact]
    public async Task Qualification_revoked_after_candidate_lookup_blocks_invitation_without_creating_it()
    {
        var scenario = await database.SeedAsync();
        await EnableQualificationAsync(scenario, scenario.Students[0], scenario.Students[1]);
        using var app = new TeamTestFactory(database, scenario);
        using var leader = app.CreateAuthenticatedClient(scenario.Students[0]);
        var team = await CreateAsync(leader, scenario);
        var candidates = await BodyAsync<AIPMS.Application.Common.Models.PagedResult<AIPMS.Application.Features.Teams.DTOs.TeamInvitationCandidateDto>>(
            await leader.GetAsync($"/api/v1/teams/{team.Id}/invitation-candidates"));
        Assert.Contains(candidates.Items, x => x.UserId == scenario.Students[1]);
        await RevokeQualificationAsync(scenario.Students[1]);

        Assert.Equal(HttpStatusCode.Conflict, (await leader.PostAsJsonAsync($"/api/v1/teams/{team.Id}/invitations",
            new { invitedUserId = scenario.Students[1] })).StatusCode);
        await using var db = database.CreateContext();
        Assert.False(await db.TeamInvitations.AnyAsync(x => x.TeamId == team.Id && x.InvitedUserId == scenario.Students[1]));
    }

    [Fact]
    public async Task Qualification_revoked_after_invitation_blocks_acceptance_without_membership_change()
    {
        var scenario = await database.SeedAsync();
        await EnableQualificationAsync(scenario, scenario.Students[0], scenario.Students[1]);
        using var app = new TeamTestFactory(database, scenario);
        using var leader = app.CreateAuthenticatedClient(scenario.Students[0]);
        using var invitee = app.CreateAuthenticatedClient(scenario.Students[1]);
        var team = await CreateAsync(leader, scenario);
        var invitation = await InviteAsync(leader, team.Id, scenario.Students[1]);
        await RevokeQualificationAsync(scenario.Students[1]);

        Assert.Equal(HttpStatusCode.Conflict, (await AcceptAsync(invitee, invitation.Id)).StatusCode);
        await using var db = database.CreateContext();
        Assert.Equal("PENDING", await db.TeamInvitations.Where(x => x.Id == invitation.Id).Select(x => x.Status).SingleAsync());
        Assert.False(await db.TeamMembers.AnyAsync(x => x.TeamId == team.Id && x.UserId == scenario.Students[1] && x.LeftAt == null));
    }

    [Fact]
    public async Task Qualification_revoked_after_eligible_team_blocks_project_submission_and_preserves_draft()
    {
        var scenario = await database.SeedAsync();
        await EnableQualificationAsync(scenario, scenario.Students[0], scenario.Students[1]);
        using var app = new TeamTestFactory(database, scenario);
        using var leader = app.CreateAuthenticatedClient(scenario.Students[0]);
        var team = await EligibleTeamAsync(app, scenario, leader);
        Assert.True(team.Eligibility.CanRegister);
        var draft = await BodyAsync<ProjectDto>(await DraftResponseAsync(leader, scenario));
        await RevokeQualificationAsync(scenario.Students[1]);

        Assert.Equal(HttpStatusCode.Conflict, (await SubmitAsync(leader, draft)).StatusCode);
        await using var db = database.CreateContext();
        Assert.Equal("DRAFT", await db.Projects.Where(x => x.Id == draft.Id).Select(x => x.Status).SingleAsync());
        Assert.False(await db.ProjectStatusHistories.AnyAsync(x => x.ProjectId == draft.Id));
    }

    private async Task EnableQualificationAsync(TeamScenario scenario, params long[] userIds)
    {
        await using var db = database.CreateContext();
        var organizationId = await db.AcademicSemesters.Where(x => x.Id == scenario.SemesterId)
            .Select(x => x.OrganizationId).SingleAsync();
        db.Set<ProjectPeriodQualificationPolicy>().Add(new ProjectPeriodQualificationPolicy
        {
            ProjectPeriodId = scenario.PeriodId, RequireStudentQualification = true,
            QualificationType = "CAPSTONE_READINESS", RequireCertificate = true,
            CheckExpiration = true, UpdatedAt = TeamDatabaseFixture.Now
        });
        foreach (var userId in userIds)
        {
            db.Set<StudentQualification>().Add(new StudentQualification
            {
                UserId = userId, OrganizationId = organizationId, QualificationType = "CAPSTONE_READINESS",
                TrainingStatus = "TRAINING_COMPLETED", VerificationStatus = "VERIFIED",
                CertificateNumber = "CERT-" + userId, IssuedAt = TeamDatabaseFixture.Now.AddDays(-1),
                ExpiresAt = TeamDatabaseFixture.Now.AddYears(1), VerifiedBy = userId, VerifiedAt = TeamDatabaseFixture.Now,
                ConcurrencyToken = Guid.NewGuid(), CreatedAt = TeamDatabaseFixture.Now, UpdatedAt = TeamDatabaseFixture.Now
            });
        }
        await db.SaveChangesAsync();
    }

    private async Task<long> AddPendingQualificationAsync(TeamScenario scenario, long userId)
    {
        await using var db = database.CreateContext();
        var organizationId = await db.AcademicSemesters.Where(x => x.Id == scenario.SemesterId)
            .Select(x => x.OrganizationId).SingleAsync();
        var qualification = new StudentQualification
        {
            UserId = userId, OrganizationId = organizationId, QualificationType = "CAPSTONE_READINESS",
            TrainingStatus = "TRAINING_COMPLETED", VerificationStatus = "PENDING_VERIFICATION",
            CertificateNumber = "CERT-PENDING-" + userId, IssuedAt = TeamDatabaseFixture.Now.AddDays(-1),
            ExpiresAt = TeamDatabaseFixture.Now.AddYears(1), ConcurrencyToken = Guid.NewGuid(),
            CreatedAt = TeamDatabaseFixture.Now, UpdatedAt = TeamDatabaseFixture.Now
        };
        db.Set<StudentQualification>().Add(qualification);
        await db.SaveChangesAsync();
        return qualification.Id;
    }

    private async Task RevokeQualificationAsync(long userId)
    {
        await using var db = database.CreateContext();
        await db.Set<StudentQualification>().Where(x => x.UserId == userId && x.QualificationType == "CAPSTONE_READINESS")
            .ExecuteUpdateAsync(x => x
                .SetProperty(q => q.VerificationStatus, "REJECTED")
                .SetProperty(q => q.UpdatedAt, TeamDatabaseFixture.Now.AddMinutes(1))
                .SetProperty(q => q.ConcurrencyToken, Guid.NewGuid()));
    }

    private async Task<long> AddStaffAsync(TeamScenario scenario)
    {
        await using var db = database.CreateContext();
        var departmentId = await db.Users.Where(x => x.Id == scenario.Students[0]).Select(x => x.DepartmentId).SingleAsync();
        var user = new AIPMS.Infrastructure.Persistence.Generated.Models.User
        {
            Email = $"staff-{Guid.NewGuid():N}@example.test", FullName = "Qualification Staff", PasswordHash = "unused",
            Status = "ACTIVE", DepartmentId = departmentId, EmployeeCode = "QS" + Guid.NewGuid().ToString("N")[..8]
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

}
