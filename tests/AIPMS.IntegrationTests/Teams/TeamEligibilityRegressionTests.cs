using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.Projects.DTOs;
using AIPMS.Application.Features.Teams.DTOs;
using AIPMS.Domain.Teams;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Generated.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace AIPMS.IntegrationTests.Teams;

public sealed class TeamEligibilityRegressionTests(TeamDatabaseFixture database) : IClassFixture<TeamDatabaseFixture>
{
    private static async Task<T> BodyAsync<T>(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private async Task<long> EnsureRoleAsync(string roleCode, string roleName)
    {
        await using var ctx = database.CreateContext();
        var existing = await ctx.Roles.FirstOrDefaultAsync(r => r.Code == roleCode);
        if (existing is not null) return existing.Id;

        var role = new Role { Code = roleCode, Name = roleName, IsSystemRole = true };
        ctx.Roles.Add(role);
        await ctx.SaveChangesAsync();
        return role.Id;
    }

    // ==========================================
    // 1. P1 — DEPARTMENT-SCOPED READ AUTHORIZATION
    // ==========================================

    [Fact]
    public async Task Auth_DepartmentStaff_MatchingDepartment_CanReadEligibilityAndHistory()
    {
        var s = await database.SeedAsync();
        var staffRoleId = await EnsureRoleAsync(AppRoles.DepartmentStaff, "Department Staff");

        long staffUserId;
        await using (var ctx = database.CreateContext())
        {
            var major = await ctx.Majors.Include(m => m.Department).SingleAsync(m => m.Id == s.SeMajorId);
            var staff = new User
            {
                Email = $"staff-match-{Guid.NewGuid():N}@example.test",
                FullName = "Matching Staff",
                PasswordHash = "hash",
                Status = "ACTIVE",
                AcademicProfileStatus = "VERIFIED",
                DepartmentId = major.DepartmentId,
                UserRoleUsers = new List<UserRole> { new() { RoleId = staffRoleId } }
            };
            ctx.Users.Add(staff);
            await ctx.SaveChangesAsync();
            staffUserId = staff.Id;
        }

        using var app = new TeamTestFactory(database, s, minMembers: 1, maxMembers: 3);
        using var clientLeader = app.CreateAuthenticatedClient(s.Students[0]);
        using var clientStaff = app.CreateAuthenticatedClient(staffUserId, roles: [AppRoles.DepartmentStaff]);

        var team = await BodyAsync<TeamDto>(await clientLeader.PostAsJsonAsync("/api/v1/teams",
            new { academicSemesterId = s.SemesterId, code = "TAUTH1", name = "Auth Team 1", description = "Test" }));

        await clientLeader.PostAsync($"/api/v1/teams/{team.Id}/eligibility/check", null);

        // GET /eligibility
        var getRes = await clientStaff.GetAsync($"/api/v1/teams/{team.Id}/eligibility");
        Assert.Equal(HttpStatusCode.OK, getRes.StatusCode);

        // GET /eligibility/history
        var histRes = await clientStaff.GetAsync($"/api/v1/teams/{team.Id}/eligibility/history");
        Assert.Equal(HttpStatusCode.OK, histRes.StatusCode);
        var history = await BodyAsync<IReadOnlyList<TeamEligibilityCheckDto>>(histRes);
        Assert.NotEmpty(history);
    }

    [Fact]
    public async Task Auth_DepartmentStaff_OtherDepartment_Forbidden()
    {
        var s = await database.SeedAsync();
        var staffRoleId = await EnsureRoleAsync(AppRoles.DepartmentStaff, "Department Staff");

        long foreignStaffUserId;
        await using (var ctx = database.CreateContext())
        {
            var org = await ctx.Organizations.FirstAsync();
            var foreignDept = new Department { Code = $"BIZ_{Guid.NewGuid():N}"[..10], Name = "Business", OrganizationId = org.Id, IsActive = true };
            ctx.Departments.Add(foreignDept);
            await ctx.SaveChangesAsync();

            var foreignStaff = new User
            {
                Email = $"staff-foreign-{Guid.NewGuid():N}@example.test",
                FullName = "Foreign Staff",
                PasswordHash = "hash",
                Status = "ACTIVE",
                AcademicProfileStatus = "VERIFIED",
                DepartmentId = foreignDept.Id,
                UserRoleUsers = new List<UserRole> { new() { RoleId = staffRoleId } }
            };
            ctx.Users.Add(foreignStaff);
            await ctx.SaveChangesAsync();
            foreignStaffUserId = foreignStaff.Id;
        }

        using var app = new TeamTestFactory(database, s, minMembers: 1, maxMembers: 3);
        using var clientLeader = app.CreateAuthenticatedClient(s.Students[0]);
        using var clientForeignStaff = app.CreateAuthenticatedClient(foreignStaffUserId, roles: [AppRoles.DepartmentStaff]);

        var team = await BodyAsync<TeamDto>(await clientLeader.PostAsJsonAsync("/api/v1/teams",
            new { academicSemesterId = s.SemesterId, code = "TAUTH2", name = "Auth Team 2", description = "Test" }));

        await clientLeader.PostAsync($"/api/v1/teams/{team.Id}/eligibility/check", null);

        var getRes = await clientForeignStaff.GetAsync($"/api/v1/teams/{team.Id}/eligibility");
        Assert.Equal(HttpStatusCode.Forbidden, getRes.StatusCode);

        var histRes = await clientForeignStaff.GetAsync($"/api/v1/teams/{team.Id}/eligibility/history");
        Assert.Equal(HttpStatusCode.Forbidden, histRes.StatusCode);
    }

    [Fact]
    public async Task Auth_DepartmentStaff_NoDepartmentScope_Forbidden()
    {
        var s = await database.SeedAsync();
        var staffRoleId = await EnsureRoleAsync(AppRoles.DepartmentStaff, "Department Staff");

        long noDeptStaffUserId;
        await using (var ctx = database.CreateContext())
        {
            var noDeptStaff = new User
            {
                Email = $"staff-nodept-{Guid.NewGuid():N}@example.test",
                FullName = "No Dept Staff",
                PasswordHash = "hash",
                Status = "ACTIVE",
                AcademicProfileStatus = "VERIFIED",
                DepartmentId = null,
                UserRoleUsers = new List<UserRole> { new() { RoleId = staffRoleId } }
            };
            ctx.Users.Add(noDeptStaff);
            await ctx.SaveChangesAsync();
            noDeptStaffUserId = noDeptStaff.Id;
        }

        using var app = new TeamTestFactory(database, s, minMembers: 1, maxMembers: 3);
        using var clientLeader = app.CreateAuthenticatedClient(s.Students[0]);
        using var clientNoDeptStaff = app.CreateAuthenticatedClient(noDeptStaffUserId, roles: [AppRoles.DepartmentStaff]);

        var team = await BodyAsync<TeamDto>(await clientLeader.PostAsJsonAsync("/api/v1/teams",
            new { academicSemesterId = s.SemesterId, code = "TAUTH3", name = "Auth Team 3", description = "Test" }));

        await clientLeader.PostAsync($"/api/v1/teams/{team.Id}/eligibility/check", null);

        var getRes = await clientNoDeptStaff.GetAsync($"/api/v1/teams/{team.Id}/eligibility");
        Assert.Equal(HttpStatusCode.Forbidden, getRes.StatusCode);

        var histRes = await clientNoDeptStaff.GetAsync($"/api/v1/teams/{team.Id}/eligibility/history");
        Assert.Equal(HttpStatusCode.Forbidden, histRes.StatusCode);
    }

    [Fact]
    public async Task Auth_RevokedPersistedRole_StaleJwt_Forbidden()
    {
        var s = await database.SeedAsync();
        var studentRoleId = await EnsureRoleAsync(AppRoles.Student, "Student");

        long studentOnlyUserId;
        await using (var ctx = database.CreateContext())
        {
            var major = await ctx.Majors.Include(m => m.Department).SingleAsync(m => m.Id == s.SeMajorId);
            var user = new User
            {
                Email = $"student-stalejwt-{Guid.NewGuid():N}@example.test",
                FullName = "Revoked Staff",
                PasswordHash = "hash",
                Status = "ACTIVE",
                AcademicProfileStatus = "VERIFIED",
                DepartmentId = major.DepartmentId,
                UserRoleUsers = new List<UserRole> { new() { RoleId = studentRoleId } } // in DB only STUDENT
            };
            ctx.Users.Add(user);
            await ctx.SaveChangesAsync();
            studentOnlyUserId = user.Id;
        }

        using var app = new TeamTestFactory(database, s, minMembers: 1, maxMembers: 3);
        using var clientLeader = app.CreateAuthenticatedClient(s.Students[0]);
        // JWT claims DEPARTMENT_STAFF, but persisted role in DB was revoked (only STUDENT)
        using var clientStaleJwt = app.CreateAuthenticatedClient(studentOnlyUserId, roles: [AppRoles.DepartmentStaff]);

        var team = await BodyAsync<TeamDto>(await clientLeader.PostAsJsonAsync("/api/v1/teams",
            new { academicSemesterId = s.SemesterId, code = "TAUTH4", name = "Auth Team 4", description = "Test" }));

        await clientLeader.PostAsync($"/api/v1/teams/{team.Id}/eligibility/check", null);

        var getRes = await clientStaleJwt.GetAsync($"/api/v1/teams/{team.Id}/eligibility");
        Assert.Equal(HttpStatusCode.Forbidden, getRes.StatusCode);

        var histRes = await clientStaleJwt.GetAsync($"/api/v1/teams/{team.Id}/eligibility/history");
        Assert.Equal(HttpStatusCode.Forbidden, histRes.StatusCode);
    }

    [Fact]
    public async Task Auth_InactiveAccount_Forbidden()
    {
        var s = await database.SeedAsync();
        var staffRoleId = await EnsureRoleAsync(AppRoles.DepartmentStaff, "Department Staff");

        long inactiveStaffUserId;
        await using (var ctx = database.CreateContext())
        {
            var major = await ctx.Majors.Include(m => m.Department).SingleAsync(m => m.Id == s.SeMajorId);
            var inactiveStaff = new User
            {
                Email = $"staff-inactive-{Guid.NewGuid():N}@example.test",
                FullName = "Inactive Staff",
                PasswordHash = "hash",
                Status = "INACTIVE",
                AcademicProfileStatus = "VERIFIED",
                DepartmentId = major.DepartmentId,
                UserRoleUsers = new List<UserRole> { new() { RoleId = staffRoleId } }
            };
            ctx.Users.Add(inactiveStaff);
            await ctx.SaveChangesAsync();
            inactiveStaffUserId = inactiveStaff.Id;
        }

        using var app = new TeamTestFactory(database, s, minMembers: 1, maxMembers: 3);
        using var clientLeader = app.CreateAuthenticatedClient(s.Students[0]);
        using var clientInactiveStaff = app.CreateAuthenticatedClient(inactiveStaffUserId, roles: [AppRoles.DepartmentStaff]);

        var team = await BodyAsync<TeamDto>(await clientLeader.PostAsJsonAsync("/api/v1/teams",
            new { academicSemesterId = s.SemesterId, code = "TAUTH5", name = "Auth Team 5", description = "Test" }));

        await clientLeader.PostAsync($"/api/v1/teams/{team.Id}/eligibility/check", null);

        var getRes = await clientInactiveStaff.GetAsync($"/api/v1/teams/{team.Id}/eligibility");
        Assert.Equal(HttpStatusCode.Forbidden, getRes.StatusCode);

        var histRes = await clientInactiveStaff.GetAsync($"/api/v1/teams/{team.Id}/eligibility/history");
        Assert.Equal(HttpStatusCode.Forbidden, histRes.StatusCode);
    }

    // ==========================================
    // 2. P1 — A -> B -> A SNAPSHOT SELECTION BUG
    // ==========================================

    [Fact]
    public async Task A_B_A_ExplicitRecheck_DedupsSnapshot_AndAllowsLockAndSubmit()
    {
        var s = await database.SeedAsync();
        using var app = new TeamTestFactory(database, s, minMembers: 2, maxMembers: 3);
        using var clientLeader = app.CreateAuthenticatedClient(s.Students[0]);
        using var clientMember = app.CreateAuthenticatedClient(s.Students[1]);

        // Form team with 2 students
        var team = await BodyAsync<TeamDto>(await clientLeader.PostAsJsonAsync("/api/v1/teams",
            new { academicSemesterId = s.SemesterId, code = "TABA1", name = "ABA Team 1", description = "Test" }));

        var invite = await BodyAsync<TeamInvitationDto>(await clientLeader.PostAsJsonAsync($"/api/v1/teams/{team.Id}/invitations",
            new { invitedUserId = s.Students[1], message = "Join" }));
        await clientMember.PostAsync($"/api/v1/teams/invitations/{invite.Id}/accept", null);

        // Step 1: VERIFIED -> explicit Check => Snapshot A = PASS / CURRENT
        var checkA = await BodyAsync<TeamEligibilityCheckDto>(await clientLeader.PostAsync($"/api/v1/teams/{team.Id}/eligibility/check", null));
        Assert.Equal("PASS", checkA.Result);
        Assert.Equal("CURRENT", checkA.Freshness);
        var idA = checkA.CheckId;
        var timeA = checkA.CheckedAt;

        // Step 3: Mutate profile of student 1 to REJECTED
        await using (var ctx = database.CreateContext())
        {
            await ctx.Users.Where(u => u.Id == s.Students[1]).ExecuteUpdateAsync(u => u.SetProperty(x => x.AcademicProfileStatus, "REJECTED"));
        }

        // Step 4: explicit Check => Snapshot B = FAIL / CURRENT
        var checkB = await BodyAsync<TeamEligibilityCheckDto>(await clientLeader.PostAsync($"/api/v1/teams/{team.Id}/eligibility/check", null));
        Assert.Equal("FAIL", checkB.Result);
        Assert.Equal("CURRENT", checkB.Freshness);
        var idB = checkB.CheckId;
        var timeB = checkB.CheckedAt;
        Assert.NotEqual(idA, idB);

        // Step 5: Restore profile to VERIFIED
        await using (var ctx = database.CreateContext())
        {
            await ctx.Users.Where(u => u.Id == s.Students[1]).ExecuteUpdateAsync(u => u.SetProperty(x => x.AcademicProfileStatus, "VERIFIED"));
        }

        // Step 6: explicit Check => returns A, not a duplicate row
        var checkA2 = await BodyAsync<TeamEligibilityCheckDto>(await clientLeader.PostAsync($"/api/v1/teams/{team.Id}/eligibility/check", null));
        Assert.Equal(idA, checkA2.CheckId);
        Assert.Equal("PASS", checkA2.Result);
        Assert.Equal("CURRENT", checkA2.Freshness);

        // Step 7: assert snapshot count still exactly 2
        await using (var ctx = database.CreateContext())
        {
            var count = await ctx.TeamEligibilityChecks.CountAsync(c => c.TeamId == team.Id);
            Assert.Equal(2, count);

            // Step 8: assert Snapshot A CheckedAt was NOT rewritten
            var dbA = await ctx.TeamEligibilityChecks.SingleAsync(c => c.Id == idA);
            Assert.Equal(timeA, dbA.CheckedAt);

            // Step 9: assert Snapshot B remains in immutable history
            var dbB = await ctx.TeamEligibilityChecks.SingleAsync(c => c.Id == idB);
            Assert.Equal("FAIL", dbB.Result);
        }

        // Step 10: GET /eligibility => resolves Snapshot A = PASS / CURRENT
        var getRes = await clientLeader.GetAsync($"/api/v1/teams/{team.Id}/eligibility");
        var getCheck = await BodyAsync<TeamEligibilityCheckDto>(getRes);
        Assert.Equal(idA, getCheck.CheckId);
        Assert.Equal("PASS", getCheck.Result);
        Assert.Equal("CURRENT", getCheck.Freshness);

        // Step 11: Lock => succeeds using A
        var lockRes = await clientLeader.PostAsync($"/api/v1/teams/{team.Id}/eligibility/lock", null);
        var lockedTeam = await BodyAsync<TeamDto>(lockRes);
        Assert.Equal("LOCKED", lockedTeam.Status);

        // Step 10: where applicable prepare project submission correctly
        long projectId;
        string concurrencyToken;
        await using (var ctx = database.CreateContext())
        {
            var project = new Project
            {
                TeamId = team.Id,
                Code = "PRJ-ABA1",
                Title = "ABA Project",
                Status = "DRAFT",
                ProposalSource = "STUDENT_PROPOSAL",
                ProblemStatement = "Problem Statement",
                Objectives = "Objectives",
                ExpectedOutput = "Output",
                RegisteredAt = DateTime.UtcNow,
                CreatedBy = s.Students[0],
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            project.ProjectMajors.Add(new ProjectMajor { MajorId = s.SeMajorId });
            project.ProjectTags.Add(new ProjectTag { Tag = new Tag { Name = "Education", NormalizedName = "EDUCATION", TagType = "DOMAIN", CreatedAt = DateTime.UtcNow } });
            project.ProjectTags.Add(new ProjectTag { Tag = new Tag { Name = ".NET", NormalizedName = ".NET", TagType = "TECHNOLOGY", CreatedAt = DateTime.UtcNow } });
            project.ProjectTags.Add(new ProjectTag { Tag = new Tag { Name = "Test", NormalizedName = "TEST", TagType = "KEYWORD", CreatedAt = DateTime.UtcNow } });
            ctx.Projects.Add(project);
            await ctx.SaveChangesAsync();
            projectId = project.Id;
            concurrencyToken = Convert.ToBase64String(project.RowVersion);
        }

        // Explicit Check for INITIAL round
        var projCheck = await BodyAsync<TeamEligibilityCheckDto>(await clientLeader.PostAsync($"/api/v1/teams/{team.Id}/eligibility/check", null));
        Assert.Equal("PASS", projCheck.Result);
        Assert.Equal("CURRENT", projCheck.Freshness);
        Assert.Equal("INITIAL", projCheck.RoundType);

        // Step 11: Submit succeeds using the same current valid eligibility state
        var submitRes = await clientLeader.PostAsJsonAsync($"/api/v1/projects/{projectId}/submit", new { concurrencyToken });
        Assert.Equal(HttpStatusCode.OK, submitRes.StatusCode);
        var submitted = await BodyAsync<ProjectDto>(submitRes);
        Assert.Equal("SUBMITTED", submitted.Status);
    }

    // ==========================================
    // 3. P2 — HISTORY MUST WORK AFTER REGISTRATION WINDOW CLOSES
    // ==========================================

    [Fact]
    public async Task History_ReturnsSnapshots_AfterRegistrationWindowCloses()
    {
        var s = await database.SeedAsync();
        using var app = new TeamTestFactory(database, s, minMembers: 1, maxMembers: 3);
        using var clientLeader = app.CreateAuthenticatedClient(s.Students[0]);

        // Form team and check eligibility while window is open
        var team = await BodyAsync<TeamDto>(await clientLeader.PostAsJsonAsync("/api/v1/teams",
            new { academicSemesterId = s.SemesterId, code = "THIST1", name = "History Team 1", description = "Test" }));

        var check = await BodyAsync<TeamEligibilityCheckDto>(await clientLeader.PostAsync($"/api/v1/teams/{team.Id}/eligibility/check", null));
        Assert.Equal("PASS", check.Result);

        // Close registration window in DB
        await using (var ctx = database.CreateContext())
        {
            await ctx.ProjectPeriods
                .Where(p => p.Id == s.PeriodId)
                .ExecuteUpdateAsync(p => p.SetProperty(x => x.EndAt, TeamDatabaseFixture.Now.AddDays(-1)));
        }

        // GET /eligibility/history succeeds and returns the historical snapshot
        var histRes = await clientLeader.GetAsync($"/api/v1/teams/{team.Id}/eligibility/history");
        Assert.Equal(HttpStatusCode.OK, histRes.StatusCode);

        var history = await BodyAsync<IReadOnlyList<TeamEligibilityCheckDto>>(histRes);
        Assert.Single(history);
        Assert.Equal(check.CheckId, history[0].CheckId);
        Assert.Equal("PASS", history[0].Result);
    }

    [Fact]
    public async Task History_Succeeds_WhenNoActiveWindowExists()
    {
        var s = await database.SeedAsync();
        using var app = new TeamTestFactory(database, s, minMembers: 1, maxMembers: 3);
        using var clientLeader = app.CreateAuthenticatedClient(s.Students[0]);

        var team = await BodyAsync<TeamDto>(await clientLeader.PostAsJsonAsync("/api/v1/teams",
            new { academicSemesterId = s.SemesterId, code = "THIST2", name = "History Team 2", description = "Test" }));

        var check = await BodyAsync<TeamEligibilityCheckDto>(await clientLeader.PostAsync($"/api/v1/teams/{team.Id}/eligibility/check", null));

        // Mark period CLOSED so no active window exists in semester
        await using (var ctx = database.CreateContext())
        {
            await ctx.ProjectPeriods
                .Where(p => p.Id == s.PeriodId)
                .ExecuteUpdateAsync(p => p.SetProperty(x => x.Status, "CLOSED"));
        }

        // GET history must remain readable
        var histRes = await clientLeader.GetAsync($"/api/v1/teams/{team.Id}/eligibility/history");
        Assert.Equal(HttpStatusCode.OK, histRes.StatusCode);

        var history = await BodyAsync<IReadOnlyList<TeamEligibilityCheckDto>>(histRes);
        Assert.Single(history);
        Assert.Equal(check.CheckId, history[0].CheckId);
    }

    [Fact]
    public async Task History_DepartmentAuthorization_EnforcedWhenWindowClosed()
    {
        var s = await database.SeedAsync();
        var staffRoleId = await EnsureRoleAsync(AppRoles.DepartmentStaff, "Department Staff");

        long foreignStaffUserId;
        long matchingStaffUserId;
        await using (var ctx = database.CreateContext())
        {
            var org = await ctx.Organizations.FirstAsync();
            var foreignDept = new Department { Code = $"BIZ_{Guid.NewGuid():N}"[..10], Name = "Business", OrganizationId = org.Id, IsActive = true };
            ctx.Departments.Add(foreignDept);
            await ctx.SaveChangesAsync();

            var foreignStaff = new User
            {
                Email = $"staff-foreign-closed-{Guid.NewGuid():N}@example.test",
                FullName = "Foreign Staff Closed",
                PasswordHash = "hash",
                Status = "ACTIVE",
                AcademicProfileStatus = "VERIFIED",
                DepartmentId = foreignDept.Id,
                UserRoleUsers = new List<UserRole> { new() { RoleId = staffRoleId } }
            };
            ctx.Users.Add(foreignStaff);

            var major = await ctx.Majors.Include(m => m.Department).SingleAsync(m => m.Id == s.SeMajorId);
            var matchingStaff = new User
            {
                Email = $"staff-match-closed-{Guid.NewGuid():N}@example.test",
                FullName = "Matching Staff Closed",
                PasswordHash = "hash",
                Status = "ACTIVE",
                AcademicProfileStatus = "VERIFIED",
                DepartmentId = major.DepartmentId,
                UserRoleUsers = new List<UserRole> { new() { RoleId = staffRoleId } }
            };
            ctx.Users.Add(matchingStaff);

            await ctx.SaveChangesAsync();
            foreignStaffUserId = foreignStaff.Id;
            matchingStaffUserId = matchingStaff.Id;
        }

        using var app = new TeamTestFactory(database, s, minMembers: 1, maxMembers: 3);
        using var clientLeader = app.CreateAuthenticatedClient(s.Students[0]);
        using var clientForeignStaff = app.CreateAuthenticatedClient(foreignStaffUserId, roles: [AppRoles.DepartmentStaff]);
        using var clientMatchingStaff = app.CreateAuthenticatedClient(matchingStaffUserId, roles: [AppRoles.DepartmentStaff]);

        var team = await BodyAsync<TeamDto>(await clientLeader.PostAsJsonAsync("/api/v1/teams",
            new { academicSemesterId = s.SemesterId, code = "THIST3", name = "History Team 3", description = "Test" }));

        await clientLeader.PostAsync($"/api/v1/teams/{team.Id}/eligibility/check", null);

        // Close registration window
        await using (var ctx = database.CreateContext())
        {
            await ctx.ProjectPeriods
                .Where(p => p.Id == s.PeriodId)
                .ExecuteUpdateAsync(p => p.SetProperty(x => x.EndAt, TeamDatabaseFixture.Now.AddDays(-1)));
        }

        // Out-of-scope DEPARTMENT_STAFF is still denied with 403
        var foreignHistRes = await clientForeignStaff.GetAsync($"/api/v1/teams/{team.Id}/eligibility/history");
        Assert.Equal(HttpStatusCode.Forbidden, foreignHistRes.StatusCode);

        // Matching department staff succeeds with 200 OK
        var matchHistRes = await clientMatchingStaff.GetAsync($"/api/v1/teams/{team.Id}/eligibility/history");
        Assert.Equal(HttpStatusCode.OK, matchHistRes.StatusCode);
    }
}
