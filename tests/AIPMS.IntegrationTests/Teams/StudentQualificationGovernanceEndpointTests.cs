using System.Net;
using System.Net.Http.Json;
using AIPMS.Application.Features.Projects.DTOs;
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
        var outsideResponse = await outside.PostAsync($"/api/v1/student-qualifications/{qualificationId}/verify", null);
        Assert.True(outsideResponse.StatusCode == HttpStatusCode.Forbidden,
            await FailureDiagnosticsAsync(outsideResponse));
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

    private static async Task<string> FailureDiagnosticsAsync(HttpResponseMessage response)
    {
        var logs = Directory.GetFiles(Path.GetTempPath(), "aipms-tests-*.log")
            .Select(File.ReadAllText);
        return $"HTTP {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}\n{string.Join(Environment.NewLine, logs)}";
    }
}
