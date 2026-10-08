using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.Projects.DTOs;
using AIPMS.Application.Features.Teams.DTOs;
using AIPMS.Domain.Teams;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Generated.Models;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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

    [Fact]
    public async Task Auth_SubmittedSingleMajor_MemberMajorChangeToForeignDepartment_DoesNotWidenReadScope()
    {
        var s = await database.SeedAsync();
        var staffRoleId = await EnsureRoleAsync(AppRoles.DepartmentStaff, "Department Staff");

        long deptAId;
        long staffAUserId;
        long deptBId;
        long deptBMajorId;
        long staffBUserId;

        await using (var ctx = database.CreateContext())
        {
            var majorA = await ctx.Majors.Include(m => m.Department).SingleAsync(m => m.Id == s.SeMajorId);
            deptAId = majorA.DepartmentId;

            var staffA = new User
            {
                Email = $"staffA-{Guid.NewGuid():N}@example.test",
                FullName = "Staff Dept A",
                PasswordHash = "hash",
                Status = "ACTIVE",
                AcademicProfileStatus = "VERIFIED",
                DepartmentId = deptAId,
                UserRoleUsers = new List<UserRole> { new() { RoleId = staffRoleId } }
            };
            ctx.Users.Add(staffA);

            var org = await ctx.Organizations.FirstAsync();
            var deptB = new Department { Code = $"DEP_{Guid.NewGuid():N}"[..10], Name = "Department B", OrganizationId = org.Id, IsActive = true };
            ctx.Departments.Add(deptB);
            await ctx.SaveChangesAsync();
            deptBId = deptB.Id;

            var majorB = new Major { Code = $"MB_{Guid.NewGuid():N}"[..10], Name = "Major B", DepartmentId = deptBId, IsActive = true };
            ctx.Majors.Add(majorB);
            await ctx.SaveChangesAsync();
            deptBMajorId = majorB.Id;

            var staffB = new User
            {
                Email = $"staffB-{Guid.NewGuid():N}@example.test",
                FullName = "Staff Dept B",
                PasswordHash = "hash",
                Status = "ACTIVE",
                AcademicProfileStatus = "VERIFIED",
                DepartmentId = deptBId,
                UserRoleUsers = new List<UserRole> { new() { RoleId = staffRoleId } }
            };
            ctx.Users.Add(staffB);
            await ctx.SaveChangesAsync();

            staffAUserId = staffA.Id;
            staffBUserId = staffB.Id;
        }

        using var app = new TeamTestFactory(database, s, minMembers: 1, maxMembers: 3);
        using var clientLeader = app.CreateAuthenticatedClient(s.Students[0]);
        using var clientStaffA = app.CreateAuthenticatedClient(staffAUserId, roles: [AppRoles.DepartmentStaff]);
        using var clientStaffB = app.CreateAuthenticatedClient(staffBUserId, roles: [AppRoles.DepartmentStaff]);

        var team = await BodyAsync<TeamDto>(await clientLeader.PostAsJsonAsync("/api/v1/teams",
            new { academicSemesterId = s.SemesterId, code = "TSINGLE1", name = "Single Major Team", description = "Test" }));

        // Create a submitted project with registration snapshot scoped only to Dept A
        long projectId;
        await using (var ctx = database.CreateContext())
        {
            var project = new Project
            {
                TeamId = team.Id,
                Code = "PRJ-SM1",
                Title = "Single Major Project",
                Status = "SUBMITTED",
                RegisteredAt = DateTime.UtcNow,
                CreatedBy = s.Students[0],
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            ctx.Projects.Add(project);
            await ctx.SaveChangesAsync();
            projectId = project.Id;

            var evidence = new RegistrationEvidence(
                new TeamAcademicScopeDto("SINGLE_MAJOR", s.SeMajorId, deptAId, [new MajorRequirementDto(s.SeMajorId, 1, 3, "All")], Guid.NewGuid()),
                new RegistrationPolicyDto(1, 3, 1, "v1"),
                (await ctx.Organizations.FirstAsync()).Id,
                DateTime.UtcNow.AddDays(-1),
                DateTime.UtcNow.AddDays(7),
                [new RegisteredMemberDto(s.Students[0], "Leader", s.SeMajorId, true)],
                [deptAId], MajorDepartmentIds: new Dictionary<long, long> { [s.SeMajorId] = deptAId }
            );

            var snapshot = new ProjectRegistrationSnapshot
            {
                ProjectId = project.Id,
                ProjectPeriodId = s.PeriodId,
                SubmittedBy = s.Students[0],
                SubmittedAt = DateTime.UtcNow,
                LeadDepartmentId = deptAId,
                SnapshotJson = JsonSerializer.Serialize(evidence),
                Decisions = []
            };
            ctx.Add(snapshot);
            await ctx.SaveChangesAsync();
        }

        await clientLeader.PostAsync($"/api/v1/teams/{team.Id}/eligibility/check", null);

        // Before member major change: Staff A allowed (200), Staff B denied (403)
        var getA = await clientStaffA.GetAsync($"/api/v1/teams/{team.Id}/eligibility");
        Assert.Equal(HttpStatusCode.OK, getA.StatusCode);
        var histA = await clientStaffA.GetAsync($"/api/v1/teams/{team.Id}/eligibility/history");
        Assert.Equal(HttpStatusCode.OK, histA.StatusCode);

        var getB = await clientStaffB.GetAsync($"/api/v1/teams/{team.Id}/eligibility");
        Assert.Equal(HttpStatusCode.Forbidden, getB.StatusCode);
        var histB = await clientStaffB.GetAsync($"/api/v1/teams/{team.Id}/eligibility/history");
        Assert.Equal(HttpStatusCode.Forbidden, histB.StatusCode);

        // Mutate member's CURRENT major to Department B in DB
        await using (var ctx = database.CreateContext())
        {
            var leaderUser = await ctx.Users.SingleAsync(u => u.Id == s.Students[0]);
            leaderUser.MajorId = deptBMajorId;
            leaderUser.DepartmentId = deptBId;
            await ctx.SaveChangesAsync();
        }

        // Assert: ProjectRegistrationSnapshot in DB was NOT changed
        await using (var ctx = database.CreateContext())
        {
            var snapshot = await ctx.Set<ProjectRegistrationSnapshot>().SingleAsync(x => x.ProjectId == projectId);
            var parsed = JsonSerializer.Deserialize<RegistrationEvidence>(snapshot.SnapshotJson)!;
            Assert.Single(parsed.DepartmentIds);
            Assert.Equal(deptAId, parsed.DepartmentIds[0]);
        }

        // After member major change: Staff A remains 200, Staff B remains STRICTLY 403 (no widening)
        getA = await clientStaffA.GetAsync($"/api/v1/teams/{team.Id}/eligibility");
        Assert.Equal(HttpStatusCode.OK, getA.StatusCode);
        histA = await clientStaffA.GetAsync($"/api/v1/teams/{team.Id}/eligibility/history");
        Assert.Equal(HttpStatusCode.OK, histA.StatusCode);

        getB = await clientStaffB.GetAsync($"/api/v1/teams/{team.Id}/eligibility");
        Assert.Equal(HttpStatusCode.Forbidden, getB.StatusCode);
        histB = await clientStaffB.GetAsync($"/api/v1/teams/{team.Id}/eligibility/history");
        Assert.Equal(HttpStatusCode.Forbidden, histB.StatusCode);
    }

    [Fact]
    public async Task Auth_SubmittedInterdisciplinary_MemberMajorChangeToForeignDepartment_DoesNotWidenReadScope()
    {
        var s = await database.SeedAsync();
        var staffRoleId = await EnsureRoleAsync(AppRoles.DepartmentStaff, "Department Staff");

        long deptAId;
        long staffAUserId;
        long deptBId;
        long deptBMajorId;
        long staffBUserId;
        long deptCId;
        long deptCMajorId;
        long staffCUserId;

        await using (var ctx = database.CreateContext())
        {
            var org = await ctx.Organizations.FirstAsync();
            var majorA = await ctx.Majors.Include(m => m.Department).SingleAsync(m => m.Id == s.SeMajorId);
            deptAId = majorA.DepartmentId;

            var staffA = new User
            {
                Email = $"staffA-inter-{Guid.NewGuid():N}@example.test",
                FullName = "Staff Dept A",
                PasswordHash = "hash",
                Status = "ACTIVE",
                AcademicProfileStatus = "VERIFIED",
                DepartmentId = deptAId,
                UserRoleUsers = new List<UserRole> { new() { RoleId = staffRoleId } }
            };
            ctx.Users.Add(staffA);

            var deptB = new Department { Code = $"DB_{Guid.NewGuid():N}"[..10], Name = "Department B", OrganizationId = org.Id, IsActive = true };
            ctx.Departments.Add(deptB);
            await ctx.SaveChangesAsync();
            deptBId = deptB.Id;

            var majorB = new Major { Code = $"MB_{Guid.NewGuid():N}"[..10], Name = "Major B", DepartmentId = deptBId, IsActive = true };
            ctx.Majors.Add(majorB);
            await ctx.SaveChangesAsync();
            deptBMajorId = majorB.Id;

            var staffB = new User
            {
                Email = $"staffB-inter-{Guid.NewGuid():N}@example.test",
                FullName = "Staff Dept B",
                PasswordHash = "hash",
                Status = "ACTIVE",
                AcademicProfileStatus = "VERIFIED",
                DepartmentId = deptBId,
                UserRoleUsers = new List<UserRole> { new() { RoleId = staffRoleId } }
            };
            ctx.Users.Add(staffB);

            var deptC = new Department { Code = $"DC_{Guid.NewGuid():N}"[..10], Name = "Department C", OrganizationId = org.Id, IsActive = true };
            ctx.Departments.Add(deptC);
            await ctx.SaveChangesAsync();
            deptCId = deptC.Id;

            var majorC = new Major { Code = $"MC_{Guid.NewGuid():N}"[..10], Name = "Major C", DepartmentId = deptCId, IsActive = true };
            ctx.Majors.Add(majorC);
            await ctx.SaveChangesAsync();
            deptCMajorId = majorC.Id;

            var staffC = new User
            {
                Email = $"staffC-inter-{Guid.NewGuid():N}@example.test",
                FullName = "Staff Dept C",
                PasswordHash = "hash",
                Status = "ACTIVE",
                AcademicProfileStatus = "VERIFIED",
                DepartmentId = deptCId,
                UserRoleUsers = new List<UserRole> { new() { RoleId = staffRoleId } }
            };
            ctx.Users.Add(staffC);
            await ctx.SaveChangesAsync();

            staffAUserId = staffA.Id;
            staffBUserId = staffB.Id;
            staffCUserId = staffC.Id;
        }

        using var app = new TeamTestFactory(database, s, minMembers: 1, maxMembers: 3);
        using var clientLeader = app.CreateAuthenticatedClient(s.Students[0]);
        using var clientStaffA = app.CreateAuthenticatedClient(staffAUserId, roles: [AppRoles.DepartmentStaff]);
        using var clientStaffB = app.CreateAuthenticatedClient(staffBUserId, roles: [AppRoles.DepartmentStaff]);
        using var clientStaffC = app.CreateAuthenticatedClient(staffCUserId, roles: [AppRoles.DepartmentStaff]);

        var team = await BodyAsync<TeamDto>(await clientLeader.PostAsJsonAsync("/api/v1/teams",
            new { academicSemesterId = s.SemesterId, code = "TINTER1", name = "Inter Team", description = "Test" }));

        // Create an interdisciplinary submitted project with snapshot containing Dept A and Dept B
        long projectId;
        await using (var ctx = database.CreateContext())
        {
            var project = new Project
            {
                TeamId = team.Id,
                Code = "PRJ-INT1",
                Title = "Inter Project",
                Status = "SUBMITTED",
                RegisteredAt = DateTime.UtcNow,
                CreatedBy = s.Students[0],
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            ctx.Projects.Add(project);
            await ctx.SaveChangesAsync();
            projectId = project.Id;

            var evidence = new RegistrationEvidence(
                new TeamAcademicScopeDto("INTERDISCIPLINARY", null, deptAId, [
                    new MajorRequirementDto(s.SeMajorId, 1, 2, "Backend"),
                    new MajorRequirementDto(deptBMajorId, 1, 2, "Frontend")
                ], Guid.NewGuid()),
                new RegistrationPolicyDto(2, 4, 2, "v1"),
                (await ctx.Organizations.FirstAsync()).Id,
                DateTime.UtcNow.AddDays(-1),
                DateTime.UtcNow.AddDays(7),
                [new RegisteredMemberDto(s.Students[0], "Leader", s.SeMajorId, true)],
                [deptAId, deptBId], MajorDepartmentIds: new Dictionary<long, long> { [s.SeMajorId] = deptAId, [deptBMajorId] = deptBId }
            );

            var snapshot = new ProjectRegistrationSnapshot
            {
                ProjectId = project.Id,
                ProjectPeriodId = s.PeriodId,
                SubmittedBy = s.Students[0],
                SubmittedAt = DateTime.UtcNow,
                LeadDepartmentId = deptAId,
                SnapshotJson = JsonSerializer.Serialize(evidence),
                Decisions =
                [
                    new ProjectDepartmentDecision { DepartmentId = deptAId },
                    new ProjectDepartmentDecision { DepartmentId = deptBId }
                ]
            };
            ctx.Add(snapshot);
            await ctx.SaveChangesAsync();
        }

        await clientLeader.PostAsync($"/api/v1/teams/{team.Id}/eligibility/check", null);

        // Before member major mutation: Staff A (200), Staff B (200), Staff C (403)
        var getA = await clientStaffA.GetAsync($"/api/v1/teams/{team.Id}/eligibility");
        Assert.Equal(HttpStatusCode.OK, getA.StatusCode);
        var histA = await clientStaffA.GetAsync($"/api/v1/teams/{team.Id}/eligibility/history");
        Assert.Equal(HttpStatusCode.OK, histA.StatusCode);

        var getB = await clientStaffB.GetAsync($"/api/v1/teams/{team.Id}/eligibility");
        Assert.Equal(HttpStatusCode.OK, getB.StatusCode);
        var histB = await clientStaffB.GetAsync($"/api/v1/teams/{team.Id}/eligibility/history");
        Assert.Equal(HttpStatusCode.OK, histB.StatusCode);

        var getC = await clientStaffC.GetAsync($"/api/v1/teams/{team.Id}/eligibility");
        Assert.Equal(HttpStatusCode.Forbidden, getC.StatusCode);
        var histC = await clientStaffC.GetAsync($"/api/v1/teams/{team.Id}/eligibility/history");
        Assert.Equal(HttpStatusCode.Forbidden, histC.StatusCode);

        // Mutate member's CURRENT major to Department C in DB
        await using (var ctx = database.CreateContext())
        {
            var leaderUser = await ctx.Users.SingleAsync(u => u.Id == s.Students[0]);
            leaderUser.MajorId = deptCMajorId;
            leaderUser.DepartmentId = deptCId;
            await ctx.SaveChangesAsync();
        }

        // Assert: ProjectRegistrationSnapshot in DB was NOT changed
        await using (var ctx = database.CreateContext())
        {
            var snapshot = await ctx.Set<ProjectRegistrationSnapshot>().SingleAsync(x => x.ProjectId == projectId);
            var parsed = JsonSerializer.Deserialize<RegistrationEvidence>(snapshot.SnapshotJson)!;
            Assert.Equal(2, parsed.DepartmentIds.Count);
            Assert.Contains(deptAId, parsed.DepartmentIds);
            Assert.Contains(deptBId, parsed.DepartmentIds);
            Assert.DoesNotContain(deptCId, parsed.DepartmentIds);
        }

        // After mutation: Staff A remains 200, Staff B remains 200, Staff C remains STRICTLY 403
        getA = await clientStaffA.GetAsync($"/api/v1/teams/{team.Id}/eligibility");
        Assert.Equal(HttpStatusCode.OK, getA.StatusCode);
        histA = await clientStaffA.GetAsync($"/api/v1/teams/{team.Id}/eligibility/history");
        Assert.Equal(HttpStatusCode.OK, histA.StatusCode);

        getB = await clientStaffB.GetAsync($"/api/v1/teams/{team.Id}/eligibility");
        Assert.Equal(HttpStatusCode.OK, getB.StatusCode);
        histB = await clientStaffB.GetAsync($"/api/v1/teams/{team.Id}/eligibility/history");
        Assert.Equal(HttpStatusCode.OK, histB.StatusCode);

        getC = await clientStaffC.GetAsync($"/api/v1/teams/{team.Id}/eligibility");
        Assert.Equal(HttpStatusCode.Forbidden, getC.StatusCode);
        histC = await clientStaffC.GetAsync($"/api/v1/teams/{team.Id}/eligibility/history");
        Assert.Equal(HttpStatusCode.Forbidden, histC.StatusCode);
    }

    [Fact]
    public async Task Auth_RejectedSingleMajor_MemberMajorChangeToForeignDepartment_DoesNotWidenReadScope()
    {
        var s = await database.SeedAsync();
        var staffRoleId = await EnsureRoleAsync(AppRoles.DepartmentStaff, "Department Staff");

        long deptAId;
        long staffAUserId;
        long deptBId;
        long deptBMajorId;
        long staffBUserId;

        await using (var ctx = database.CreateContext())
        {
            var org = await ctx.Organizations.FirstAsync();
            var majorA = await ctx.Majors.Include(m => m.Department).SingleAsync(m => m.Id == s.SeMajorId);
            deptAId = majorA.DepartmentId;

            var staffA = new User
            {
                Email = $"staffA-rej-{Guid.NewGuid():N}@example.test",
                FullName = "Staff Dept A",
                PasswordHash = "hash",
                Status = "ACTIVE",
                AcademicProfileStatus = "VERIFIED",
                DepartmentId = deptAId,
                UserRoleUsers = new List<UserRole> { new() { RoleId = staffRoleId } }
            };
            ctx.Users.Add(staffA);

            var deptB = new Department { Code = $"DB_{Guid.NewGuid():N}"[..10], Name = "Department B", OrganizationId = org.Id, IsActive = true };
            ctx.Departments.Add(deptB);
            await ctx.SaveChangesAsync();
            deptBId = deptB.Id;

            var majorB = new Major { Code = $"MB_{Guid.NewGuid():N}"[..10], Name = "Major B", DepartmentId = deptBId, IsActive = true };
            ctx.Majors.Add(majorB);
            await ctx.SaveChangesAsync();
            deptBMajorId = majorB.Id;

            var staffB = new User
            {
                Email = $"staffB-rej-{Guid.NewGuid():N}@example.test",
                FullName = "Staff Dept B",
                PasswordHash = "hash",
                Status = "ACTIVE",
                AcademicProfileStatus = "VERIFIED",
                DepartmentId = deptBId,
                UserRoleUsers = new List<UserRole> { new() { RoleId = staffRoleId } }
            };
            ctx.Users.Add(staffB);
            await ctx.SaveChangesAsync();

            staffAUserId = staffA.Id;
            staffBUserId = staffB.Id;
        }

        using var app = new TeamTestFactory(database, s, minMembers: 1, maxMembers: 3);
        using var clientLeader = app.CreateAuthenticatedClient(s.Students[0]);
        using var clientStaffA = app.CreateAuthenticatedClient(staffAUserId, roles: [AppRoles.DepartmentStaff]);
        using var clientStaffB = app.CreateAuthenticatedClient(staffBUserId, roles: [AppRoles.DepartmentStaff]);

        var team = await BodyAsync<TeamDto>(await clientLeader.PostAsJsonAsync("/api/v1/teams",
            new { academicSemesterId = s.SemesterId, code = "TREJSM1", name = "Rejected Single Major Team", description = "Test" }));

        // Create a submitted project with registration snapshot scoped only to Dept A
        long projectId;
        await using (var ctx = database.CreateContext())
        {
            var project = new Project
            {
                TeamId = team.Id,
                Code = "PRJ-REJ1",
                Title = "Rejected Single Major Project",
                Status = "SUBMITTED",
                RegisteredAt = DateTime.UtcNow,
                CreatedBy = s.Students[0],
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            ctx.Projects.Add(project);
            await ctx.SaveChangesAsync();
            projectId = project.Id;

            var evidence = new RegistrationEvidence(
                new TeamAcademicScopeDto("SINGLE_MAJOR", s.SeMajorId, deptAId, [new MajorRequirementDto(s.SeMajorId, 1, 3, "All")], Guid.NewGuid()),
                new RegistrationPolicyDto(1, 3, 1, "v1"),
                (await ctx.Organizations.FirstAsync()).Id,
                DateTime.UtcNow.AddDays(-1),
                DateTime.UtcNow.AddDays(7),
                [new RegisteredMemberDto(s.Students[0], "Leader", s.SeMajorId, true)],
                [deptAId], MajorDepartmentIds: new Dictionary<long, long> { [s.SeMajorId] = deptAId }
            );

            var snapshot = new ProjectRegistrationSnapshot
            {
                ProjectId = project.Id,
                ProjectPeriodId = s.PeriodId,
                SubmittedBy = s.Students[0],
                SubmittedAt = DateTime.UtcNow,
                LeadDepartmentId = deptAId,
                SnapshotJson = JsonSerializer.Serialize(evidence),
                Decisions = []
            };
            ctx.Add(snapshot);
            await ctx.SaveChangesAsync();
        }

        await clientLeader.PostAsync($"/api/v1/teams/{team.Id}/eligibility/check", null);

        // Transition project to REJECTED
        await using (var ctx = database.CreateContext())
        {
            var p = await ctx.Projects.SingleAsync(x => x.Id == projectId);
            p.Status = "REJECTED";
            await ctx.SaveChangesAsync();
        }

        // Mutate member's CURRENT major to Department B in DB
        await using (var ctx = database.CreateContext())
        {
            var leaderUser = await ctx.Users.SingleAsync(u => u.Id == s.Students[0]);
            leaderUser.MajorId = deptBMajorId;
            leaderUser.DepartmentId = deptBId;
            await ctx.SaveChangesAsync();
        }

        // Assert: ProjectRegistrationSnapshot in DB remains unchanged
        await using (var ctx = database.CreateContext())
        {
            var snapshot = await ctx.Set<ProjectRegistrationSnapshot>().SingleAsync(x => x.ProjectId == projectId);
            var parsed = JsonSerializer.Deserialize<RegistrationEvidence>(snapshot.SnapshotJson)!;
            Assert.Single(parsed.DepartmentIds);
            Assert.Equal(deptAId, parsed.DepartmentIds[0]);
        }

        // Assert BOTH endpoints: Staff A (200), Staff B (403)
        var getA = await clientStaffA.GetAsync($"/api/v1/teams/{team.Id}/eligibility");
        Assert.Equal(HttpStatusCode.OK, getA.StatusCode);
        var histA = await clientStaffA.GetAsync($"/api/v1/teams/{team.Id}/eligibility/history");
        Assert.Equal(HttpStatusCode.OK, histA.StatusCode);

        var getB = await clientStaffB.GetAsync($"/api/v1/teams/{team.Id}/eligibility");
        Assert.Equal(HttpStatusCode.Forbidden, getB.StatusCode);
        var histB = await clientStaffB.GetAsync($"/api/v1/teams/{team.Id}/eligibility/history");
        Assert.Equal(HttpStatusCode.Forbidden, histB.StatusCode);
    }

    [Fact]
    public async Task Auth_ArchivedSingleMajor_MemberMajorChangeToForeignDepartment_DoesNotWidenReadScope()
    {
        var s = await database.SeedAsync();
        var staffRoleId = await EnsureRoleAsync(AppRoles.DepartmentStaff, "Department Staff");

        long deptAId;
        long staffAUserId;
        long deptBId;
        long deptBMajorId;
        long staffBUserId;

        await using (var ctx = database.CreateContext())
        {
            var org = await ctx.Organizations.FirstAsync();
            var majorA = await ctx.Majors.Include(m => m.Department).SingleAsync(m => m.Id == s.SeMajorId);
            deptAId = majorA.DepartmentId;

            var staffA = new User
            {
                Email = $"staffA-arc-{Guid.NewGuid():N}@example.test",
                FullName = "Staff Dept A",
                PasswordHash = "hash",
                Status = "ACTIVE",
                AcademicProfileStatus = "VERIFIED",
                DepartmentId = deptAId,
                UserRoleUsers = new List<UserRole> { new() { RoleId = staffRoleId } }
            };
            ctx.Users.Add(staffA);

            var deptB = new Department { Code = $"DB_{Guid.NewGuid():N}"[..10], Name = "Department B", OrganizationId = org.Id, IsActive = true };
            ctx.Departments.Add(deptB);
            await ctx.SaveChangesAsync();
            deptBId = deptB.Id;

            var majorB = new Major { Code = $"MB_{Guid.NewGuid():N}"[..10], Name = "Major B", DepartmentId = deptBId, IsActive = true };
            ctx.Majors.Add(majorB);
            await ctx.SaveChangesAsync();
            deptBMajorId = majorB.Id;

            var staffB = new User
            {
                Email = $"staffB-arc-{Guid.NewGuid():N}@example.test",
                FullName = "Staff Dept B",
                PasswordHash = "hash",
                Status = "ACTIVE",
                AcademicProfileStatus = "VERIFIED",
                DepartmentId = deptBId,
                UserRoleUsers = new List<UserRole> { new() { RoleId = staffRoleId } }
            };
            ctx.Users.Add(staffB);
            await ctx.SaveChangesAsync();

            staffAUserId = staffA.Id;
            staffBUserId = staffB.Id;
        }

        using var app = new TeamTestFactory(database, s, minMembers: 1, maxMembers: 3);
        using var clientLeader = app.CreateAuthenticatedClient(s.Students[0]);
        using var clientStaffA = app.CreateAuthenticatedClient(staffAUserId, roles: [AppRoles.DepartmentStaff]);
        using var clientStaffB = app.CreateAuthenticatedClient(staffBUserId, roles: [AppRoles.DepartmentStaff]);

        var team = await BodyAsync<TeamDto>(await clientLeader.PostAsJsonAsync("/api/v1/teams",
            new { academicSemesterId = s.SemesterId, code = "TARCSM1", name = "Archived Single Major Team", description = "Test" }));

        // Create a submitted project with registration snapshot scoped only to Dept A
        long projectId;
        await using (var ctx = database.CreateContext())
        {
            var project = new Project
            {
                TeamId = team.Id,
                Code = "PRJ-ARC1",
                Title = "Archived Single Major Project",
                Status = "SUBMITTED",
                RegisteredAt = DateTime.UtcNow,
                CreatedBy = s.Students[0],
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            ctx.Projects.Add(project);
            await ctx.SaveChangesAsync();
            projectId = project.Id;

            var evidence = new RegistrationEvidence(
                new TeamAcademicScopeDto("SINGLE_MAJOR", s.SeMajorId, deptAId, [new MajorRequirementDto(s.SeMajorId, 1, 3, "All")], Guid.NewGuid()),
                new RegistrationPolicyDto(1, 3, 1, "v1"),
                (await ctx.Organizations.FirstAsync()).Id,
                DateTime.UtcNow.AddDays(-1),
                DateTime.UtcNow.AddDays(7),
                [new RegisteredMemberDto(s.Students[0], "Leader", s.SeMajorId, true)],
                [deptAId], MajorDepartmentIds: new Dictionary<long, long> { [s.SeMajorId] = deptAId }
            );

            var snapshot = new ProjectRegistrationSnapshot
            {
                ProjectId = project.Id,
                ProjectPeriodId = s.PeriodId,
                SubmittedBy = s.Students[0],
                SubmittedAt = DateTime.UtcNow,
                LeadDepartmentId = deptAId,
                SnapshotJson = JsonSerializer.Serialize(evidence),
                Decisions = []
            };
            ctx.Add(snapshot);
            await ctx.SaveChangesAsync();
        }

        await clientLeader.PostAsync($"/api/v1/teams/{team.Id}/eligibility/check", null);

        // Transition project to ARCHIVED
        await using (var ctx = database.CreateContext())
        {
            var p = await ctx.Projects.SingleAsync(x => x.Id == projectId);
            p.Status = "ARCHIVED";
            await ctx.SaveChangesAsync();
        }

        // Mutate member's CURRENT major to Department B in DB
        await using (var ctx = database.CreateContext())
        {
            var leaderUser = await ctx.Users.SingleAsync(u => u.Id == s.Students[0]);
            leaderUser.MajorId = deptBMajorId;
            leaderUser.DepartmentId = deptBId;
            await ctx.SaveChangesAsync();
        }

        // Assert: ProjectRegistrationSnapshot in DB remains unchanged
        await using (var ctx = database.CreateContext())
        {
            var snapshot = await ctx.Set<ProjectRegistrationSnapshot>().SingleAsync(x => x.ProjectId == projectId);
            var parsed = JsonSerializer.Deserialize<RegistrationEvidence>(snapshot.SnapshotJson)!;
            Assert.Single(parsed.DepartmentIds);
            Assert.Equal(deptAId, parsed.DepartmentIds[0]);
        }

        // Assert BOTH endpoints: Staff A (200), Staff B (403)
        var getA = await clientStaffA.GetAsync($"/api/v1/teams/{team.Id}/eligibility");
        Assert.Equal(HttpStatusCode.OK, getA.StatusCode);
        var histA = await clientStaffA.GetAsync($"/api/v1/teams/{team.Id}/eligibility/history");
        Assert.Equal(HttpStatusCode.OK, histA.StatusCode);

        var getB = await clientStaffB.GetAsync($"/api/v1/teams/{team.Id}/eligibility");
        Assert.Equal(HttpStatusCode.Forbidden, getB.StatusCode);
        var histB = await clientStaffB.GetAsync($"/api/v1/teams/{team.Id}/eligibility/history");
        Assert.Equal(HttpStatusCode.Forbidden, histB.StatusCode);
    }

    [Fact]
    public async Task Auth_TerminalInterdisciplinary_MemberMajorChangeToForeignDepartment_DoesNotWidenReadScope()
    {
        var s = await database.SeedAsync();
        var staffRoleId = await EnsureRoleAsync(AppRoles.DepartmentStaff, "Department Staff");

        long deptAId;
        long staffAUserId;
        long deptBId;
        long deptBMajorId;
        long staffBUserId;
        long deptCId;
        long deptCMajorId;
        long staffCUserId;

        await using (var ctx = database.CreateContext())
        {
            var org = await ctx.Organizations.FirstAsync();
            var majorA = await ctx.Majors.Include(m => m.Department).SingleAsync(m => m.Id == s.SeMajorId);
            deptAId = majorA.DepartmentId;

            var staffA = new User
            {
                Email = $"staffA-term-int-{Guid.NewGuid():N}@example.test",
                FullName = "Staff Dept A",
                PasswordHash = "hash",
                Status = "ACTIVE",
                AcademicProfileStatus = "VERIFIED",
                DepartmentId = deptAId,
                UserRoleUsers = new List<UserRole> { new() { RoleId = staffRoleId } }
            };
            ctx.Users.Add(staffA);

            var deptB = new Department { Code = $"DB_{Guid.NewGuid():N}"[..10], Name = "Department B", OrganizationId = org.Id, IsActive = true };
            ctx.Departments.Add(deptB);
            await ctx.SaveChangesAsync();
            deptBId = deptB.Id;

            var majorB = new Major { Code = $"MB_{Guid.NewGuid():N}"[..10], Name = "Major B", DepartmentId = deptBId, IsActive = true };
            ctx.Majors.Add(majorB);
            await ctx.SaveChangesAsync();
            deptBMajorId = majorB.Id;

            var staffB = new User
            {
                Email = $"staffB-term-int-{Guid.NewGuid():N}@example.test",
                FullName = "Staff Dept B",
                PasswordHash = "hash",
                Status = "ACTIVE",
                AcademicProfileStatus = "VERIFIED",
                DepartmentId = deptBId,
                UserRoleUsers = new List<UserRole> { new() { RoleId = staffRoleId } }
            };
            ctx.Users.Add(staffB);

            var deptC = new Department { Code = $"DC_{Guid.NewGuid():N}"[..10], Name = "Department C", OrganizationId = org.Id, IsActive = true };
            ctx.Departments.Add(deptC);
            await ctx.SaveChangesAsync();
            deptCId = deptC.Id;

            var majorC = new Major { Code = $"MC_{Guid.NewGuid():N}"[..10], Name = "Major C", DepartmentId = deptCId, IsActive = true };
            ctx.Majors.Add(majorC);
            await ctx.SaveChangesAsync();
            deptCMajorId = majorC.Id;

            var staffC = new User
            {
                Email = $"staffC-term-int-{Guid.NewGuid():N}@example.test",
                FullName = "Staff Dept C",
                PasswordHash = "hash",
                Status = "ACTIVE",
                AcademicProfileStatus = "VERIFIED",
                DepartmentId = deptCId,
                UserRoleUsers = new List<UserRole> { new() { RoleId = staffRoleId } }
            };
            ctx.Users.Add(staffC);
            await ctx.SaveChangesAsync();

            staffAUserId = staffA.Id;
            staffBUserId = staffB.Id;
            staffCUserId = staffC.Id;
        }

        using var app = new TeamTestFactory(database, s, minMembers: 1, maxMembers: 3);
        using var clientLeader = app.CreateAuthenticatedClient(s.Students[0]);
        using var clientStaffA = app.CreateAuthenticatedClient(staffAUserId, roles: [AppRoles.DepartmentStaff]);
        using var clientStaffB = app.CreateAuthenticatedClient(staffBUserId, roles: [AppRoles.DepartmentStaff]);
        using var clientStaffC = app.CreateAuthenticatedClient(staffCUserId, roles: [AppRoles.DepartmentStaff]);

        var team = await BodyAsync<TeamDto>(await clientLeader.PostAsJsonAsync("/api/v1/teams",
            new { academicSemesterId = s.SemesterId, code = "TTERMINT1", name = "Terminal Inter Team", description = "Test" }));

        // Create an interdisciplinary submitted project with snapshot containing Dept A and Dept B
        long projectId;
        await using (var ctx = database.CreateContext())
        {
            var project = new Project
            {
                TeamId = team.Id,
                Code = "PRJ-TERM1",
                Title = "Terminal Inter Project",
                Status = "SUBMITTED",
                RegisteredAt = DateTime.UtcNow,
                CreatedBy = s.Students[0],
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            ctx.Projects.Add(project);
            await ctx.SaveChangesAsync();
            projectId = project.Id;

            var evidence = new RegistrationEvidence(
                new TeamAcademicScopeDto("INTERDISCIPLINARY", null, deptAId, [
                    new MajorRequirementDto(s.SeMajorId, 1, 2, "Backend"),
                    new MajorRequirementDto(deptBMajorId, 1, 2, "Frontend")
                ], Guid.NewGuid()),
                new RegistrationPolicyDto(2, 4, 2, "v1"),
                (await ctx.Organizations.FirstAsync()).Id,
                DateTime.UtcNow.AddDays(-1),
                DateTime.UtcNow.AddDays(7),
                [new RegisteredMemberDto(s.Students[0], "Leader", s.SeMajorId, true)],
                [deptAId, deptBId], MajorDepartmentIds: new Dictionary<long, long> { [s.SeMajorId] = deptAId, [deptBMajorId] = deptBId }
            );

            var snapshot = new ProjectRegistrationSnapshot
            {
                ProjectId = project.Id,
                ProjectPeriodId = s.PeriodId,
                SubmittedBy = s.Students[0],
                SubmittedAt = DateTime.UtcNow,
                LeadDepartmentId = deptAId,
                SnapshotJson = JsonSerializer.Serialize(evidence),
                Decisions =
                [
                    new ProjectDepartmentDecision { DepartmentId = deptAId },
                    new ProjectDepartmentDecision { DepartmentId = deptBId }
                ]
            };
            ctx.Add(snapshot);
            await ctx.SaveChangesAsync();
        }

        await clientLeader.PostAsync($"/api/v1/teams/{team.Id}/eligibility/check", null);

        // Transition project to REJECTED (terminal)
        await using (var ctx = database.CreateContext())
        {
            var p = await ctx.Projects.SingleAsync(x => x.Id == projectId);
            p.Status = "REJECTED";
            await ctx.SaveChangesAsync();
        }

        // Mutate member's CURRENT major to Department C in DB
        await using (var ctx = database.CreateContext())
        {
            var leaderUser = await ctx.Users.SingleAsync(u => u.Id == s.Students[0]);
            leaderUser.MajorId = deptCMajorId;
            leaderUser.DepartmentId = deptCId;
            await ctx.SaveChangesAsync();
        }

        // Assert: ProjectRegistrationSnapshot in DB remains unchanged (A + B, not C)
        await using (var ctx = database.CreateContext())
        {
            var snapshot = await ctx.Set<ProjectRegistrationSnapshot>().SingleAsync(x => x.ProjectId == projectId);
            var parsed = JsonSerializer.Deserialize<RegistrationEvidence>(snapshot.SnapshotJson)!;
            Assert.Equal(2, parsed.DepartmentIds.Count);
            Assert.Contains(deptAId, parsed.DepartmentIds);
            Assert.Contains(deptBId, parsed.DepartmentIds);
            Assert.DoesNotContain(deptCId, parsed.DepartmentIds);
        }

        // Assert BOTH endpoints: Staff A (200), Staff B (200), Staff C (403)
        var getA = await clientStaffA.GetAsync($"/api/v1/teams/{team.Id}/eligibility");
        Assert.Equal(HttpStatusCode.OK, getA.StatusCode);
        var histA = await clientStaffA.GetAsync($"/api/v1/teams/{team.Id}/eligibility/history");
        Assert.Equal(HttpStatusCode.OK, histA.StatusCode);

        var getB = await clientStaffB.GetAsync($"/api/v1/teams/{team.Id}/eligibility");
        Assert.Equal(HttpStatusCode.OK, getB.StatusCode);
        var histB = await clientStaffB.GetAsync($"/api/v1/teams/{team.Id}/eligibility/history");
        Assert.Equal(HttpStatusCode.OK, histB.StatusCode);

        var getC = await clientStaffC.GetAsync($"/api/v1/teams/{team.Id}/eligibility");
        Assert.Equal(HttpStatusCode.Forbidden, getC.StatusCode);
        var histC = await clientStaffC.GetAsync($"/api/v1/teams/{team.Id}/eligibility/history");
        Assert.Equal(HttpStatusCode.Forbidden, histC.StatusCode);
    }

    [Fact]
    public async Task Auth_Supervisor_AssignedToTeamProject_CanReadEligibilityAndHistory()
    {
        var s = await database.SeedAsync();
        var lecturerRoleId = await EnsureRoleAsync(AppRoles.Lecturer, "Lecturer");

        long supervisorUserId;
        long otherSupervisorUserId;

        await using (var ctx = database.CreateContext())
        {
            var major = await ctx.Majors.Include(m => m.Department).SingleAsync(m => m.Id == s.SeMajorId);

            var supervisorUser = new User
            {
                Email = $"supervisor-{Guid.NewGuid():N}@example.test",
                FullName = "Assigned Supervisor",
                PasswordHash = "hash",
                Status = "ACTIVE",
                AcademicProfileStatus = "VERIFIED",
                DepartmentId = major.DepartmentId,
                UserRoleUsers = new List<UserRole> { new() { RoleId = lecturerRoleId } }
            };
            ctx.Users.Add(supervisorUser);

            var otherSupervisorUser = new User
            {
                Email = $"supervisor-other-{Guid.NewGuid():N}@example.test",
                FullName = "Other Supervisor",
                PasswordHash = "hash",
                Status = "ACTIVE",
                AcademicProfileStatus = "VERIFIED",
                DepartmentId = major.DepartmentId,
                UserRoleUsers = new List<UserRole> { new() { RoleId = lecturerRoleId } }
            };
            ctx.Users.Add(otherSupervisorUser);
            await ctx.SaveChangesAsync();

            var profile = new SupervisorProfile
            {
                UserId = supervisorUser.Id,
                IsAvailable = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            ctx.SupervisorProfiles.Add(profile);

            var otherProfile = new SupervisorProfile
            {
                UserId = otherSupervisorUser.Id,
                IsAvailable = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            ctx.SupervisorProfiles.Add(otherProfile);
            await ctx.SaveChangesAsync();

            supervisorUserId = supervisorUser.Id;
            otherSupervisorUserId = otherSupervisorUser.Id;
        }

        using var app = new TeamTestFactory(database, s, minMembers: 1, maxMembers: 3);
        using var clientLeader = app.CreateAuthenticatedClient(s.Students[0]);
        using var clientSupervisor = app.CreateAuthenticatedClient(supervisorUserId, roles: [AppRoles.Lecturer]);
        using var clientOtherSupervisor = app.CreateAuthenticatedClient(otherSupervisorUserId, roles: [AppRoles.Lecturer]);

        var team = await BodyAsync<TeamDto>(await clientLeader.PostAsJsonAsync("/api/v1/teams",
            new { academicSemesterId = s.SemesterId, code = "TSUP1", name = "Supervisor Team", description = "Test" }));

        // Attach a project and assign the supervisor
        await using (var ctx = database.CreateContext())
        {
            var profile = await ctx.SupervisorProfiles.SingleAsync(p => p.UserId == supervisorUserId);
            var project = new Project
            {
                TeamId = team.Id,
                Code = "PRJ-SUP1",
                Title = "Supervisor Project",
                Status = "SUBMITTED",
                RegisteredAt = DateTime.UtcNow,
                CreatedBy = s.Students[0],
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            ctx.Projects.Add(project);
            await ctx.SaveChangesAsync();

            var supervisorRequest = new SupervisorRequest
            {
                Project = project,
                SupervisorProfile = profile,
                RequestedBy = s.Students[0],
                Status = "ACCEPTED",
                RequestedAt = DateTime.UtcNow,
                RespondedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            var assignment = new SupervisorAssignment
            {
                Project = project,
                SupervisorProfile = profile,
                SupervisorRequest = supervisorRequest,
                IsPrimary = true,
                AssignedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            ctx.SupervisorAssignments.Add(assignment);
            await ctx.SaveChangesAsync();
        }

        await clientLeader.PostAsync($"/api/v1/teams/{team.Id}/eligibility/check", null);

        // Assigned supervisor receives 200 OK on both endpoints
        var getSup = await clientSupervisor.GetAsync($"/api/v1/teams/{team.Id}/eligibility");
        Assert.Equal(HttpStatusCode.OK, getSup.StatusCode);

        var histSup = await clientSupervisor.GetAsync($"/api/v1/teams/{team.Id}/eligibility/history");
        Assert.Equal(HttpStatusCode.OK, histSup.StatusCode);
        var histData = await BodyAsync<IReadOnlyList<TeamEligibilityCheckDto>>(histSup);
        Assert.NotEmpty(histData);

        // Unassigned supervisor receives 403 Forbidden
        var getOther = await clientOtherSupervisor.GetAsync($"/api/v1/teams/{team.Id}/eligibility");
        Assert.Equal(HttpStatusCode.Forbidden, getOther.StatusCode);

        var histOther = await clientOtherSupervisor.GetAsync($"/api/v1/teams/{team.Id}/eligibility/history");
        Assert.Equal(HttpStatusCode.Forbidden, histOther.StatusCode);
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
    public async Task History_SnapshotRemainsCurrent_WhenRegistrationWindowClosesWithoutFactChanges()
    {
        var s = await database.SeedAsync();
        using var app = new TeamTestFactory(database, s, minMembers: 1, maxMembers: 3);
        using var clientLeader = app.CreateAuthenticatedClient(s.Students[0]);

        // Form team and check eligibility while window is open
        var team = await BodyAsync<TeamDto>(await clientLeader.PostAsJsonAsync("/api/v1/teams",
            new { academicSemesterId = s.SemesterId, code = "THIST1", name = "History Team 1", description = "Test" }));

        var check = await BodyAsync<TeamEligibilityCheckDto>(await clientLeader.PostAsync($"/api/v1/teams/{team.Id}/eligibility/check", null));
        Assert.Equal("PASS", check.Result);
        Assert.Equal("CURRENT", check.Freshness);

        // Close registration window in DB (without changing any eligibility facts)
        await using (var ctx = database.CreateContext())
        {
            await ctx.ProjectPeriods
                .Where(p => p.Id == s.PeriodId)
                .ExecuteUpdateAsync(p => p.SetProperty(x => x.EndAt, TeamDatabaseFixture.Now.AddDays(-1)));
        }

        // GET /eligibility/history succeeds and snapshot freshness remains CURRENT
        var histRes = await clientLeader.GetAsync($"/api/v1/teams/{team.Id}/eligibility/history");
        Assert.Equal(HttpStatusCode.OK, histRes.StatusCode);

        var history = await BodyAsync<IReadOnlyList<TeamEligibilityCheckDto>>(histRes);
        Assert.Single(history);
        Assert.Equal(check.CheckId, history[0].CheckId);
        Assert.Equal("PASS", history[0].Result);
        Assert.Equal("CURRENT", history[0].Freshness);
    }

    [Fact]
    public async Task History_SnapshotBecomesStale_WhenFactsChangeAfterWindowClosed()
    {
        var s = await database.SeedAsync();
        using var app = new TeamTestFactory(database, s, minMembers: 1, maxMembers: 3);
        using var clientLeader = app.CreateAuthenticatedClient(s.Students[0]);

        var team = await BodyAsync<TeamDto>(await clientLeader.PostAsJsonAsync("/api/v1/teams",
            new { academicSemesterId = s.SemesterId, code = "THIST_FACT", name = "History Facts Team", description = "Test" }));

        var check = await BodyAsync<TeamEligibilityCheckDto>(await clientLeader.PostAsync($"/api/v1/teams/{team.Id}/eligibility/check", null));
        Assert.Equal("PASS", check.Result);
        Assert.Equal("CURRENT", check.Freshness);

        // Close registration window
        await using (var ctx = database.CreateContext())
        {
            await ctx.ProjectPeriods
                .Where(p => p.Id == s.PeriodId)
                .ExecuteUpdateAsync(p => p.SetProperty(x => x.EndAt, TeamDatabaseFixture.Now.AddDays(-1)));
        }

        // Invalidate an eligibility fact after window is closed (e.g. mutate academic profile status)
        await using (var ctx = database.CreateContext())
        {
            await ctx.Users
                .Where(u => u.Id == s.Students[0])
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.AcademicProfileStatus, "REJECTED"));
        }

        // GET history succeeds, and snapshot is STALE because facts changed, NOT because window closed
        var histRes = await clientLeader.GetAsync($"/api/v1/teams/{team.Id}/eligibility/history");
        Assert.Equal(HttpStatusCode.OK, histRes.StatusCode);

        var history = await BodyAsync<IReadOnlyList<TeamEligibilityCheckDto>>(histRes);
        Assert.Single(history);
        Assert.Equal(check.CheckId, history[0].CheckId);
        Assert.Equal("STALE", history[0].Freshness);
    }

    [Fact]
    public async Task History_SnapshotBecomesStale_WhenTemporalQualificationExpiresAfterWindowClosed()
    {
        var s = await database.SeedAsync();

        // Seed qualification policy requiring student qualification with expiration check
        await using (var ctx = database.CreateContext())
        {
            var orgId = await ctx.AcademicSemesters.Where(x => x.Id == s.SemesterId)
                .Select(x => x.OrganizationId).SingleAsync();

            ctx.Set<ProjectPeriodQualificationPolicy>().Add(new ProjectPeriodQualificationPolicy
            {
                ProjectPeriodId = s.PeriodId,
                RequireStudentQualification = true,
                QualificationType = "CAPSTONE_READINESS",
                RequireCertificate = false,
                CheckExpiration = true,
                UpdatedAt = TeamDatabaseFixture.Now
            });

            // Student 0 qualification expires in 2 hours
            ctx.Set<StudentQualification>().Add(new StudentQualification
            {
                UserId = s.Students[0],
                OrganizationId = orgId,
                QualificationType = "CAPSTONE_READINESS",
                TrainingStatus = "TRAINING_COMPLETED",
                VerificationStatus = "VERIFIED",
                CertificateNumber = "CERT-" + s.Students[0],
                IssuedAt = TeamDatabaseFixture.Now.AddDays(-1),
                ExpiresAt = TeamDatabaseFixture.Now.AddHours(2),
                VerifiedBy = s.Students[0],
                VerifiedAt = TeamDatabaseFixture.Now,
                ConcurrencyToken = Guid.NewGuid(),
                CreatedAt = TeamDatabaseFixture.Now,
                UpdatedAt = TeamDatabaseFixture.Now
            });

            await ctx.SaveChangesAsync();
        }

        var clock = new ManualClock(TeamDatabaseFixture.Now);
        using var app = new TeamTestFactory(database, s, minMembers: 1, maxMembers: 3, customizeServices: services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(clock);
        });
        using var clientLeader = app.CreateAuthenticatedClient(s.Students[0]);

        var team = await BodyAsync<TeamDto>(await clientLeader.PostAsJsonAsync("/api/v1/teams",
            new { academicSemesterId = s.SemesterId, code = "THIST_EXP", name = "History Exp Team", description = "Test" }));

        // Check eligibility at T0: qualification valid until Now + 2h -> PASS, CURRENT
        var check = await BodyAsync<TeamEligibilityCheckDto>(await clientLeader.PostAsync($"/api/v1/teams/{team.Id}/eligibility/check", null));
        Assert.Equal("PASS", check.Result);
        Assert.Equal("CURRENT", check.Freshness);
        Assert.NotNull(check.ValidUntilAt);

        // Close registration window
        await using (var ctx = database.CreateContext())
        {
            await ctx.ProjectPeriods
                .Where(p => p.Id == s.PeriodId)
                .ExecuteUpdateAsync(p => p.SetProperty(x => x.EndAt, TeamDatabaseFixture.Now.AddDays(-1)));
        }

        // Advance time to 3 hours later (past qualification expiry)
        clock.CurrentTime = TeamDatabaseFixture.Now.AddHours(3);

        // GET history succeeds and reports STALE due to actual temporal expiry
        var histRes = await clientLeader.GetAsync($"/api/v1/teams/{team.Id}/eligibility/history");
        Assert.Equal(HttpStatusCode.OK, histRes.StatusCode);

        var history = await BodyAsync<IReadOnlyList<TeamEligibilityCheckDto>>(histRes);
        Assert.Single(history);
        Assert.Equal(check.CheckId, history[0].CheckId);
        Assert.Equal("STALE", history[0].Freshness);
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

        // GET history must remain readable with CURRENT freshness when facts unchanged
        var histRes = await clientLeader.GetAsync($"/api/v1/teams/{team.Id}/eligibility/history");
        Assert.Equal(HttpStatusCode.OK, histRes.StatusCode);

        var history = await BodyAsync<IReadOnlyList<TeamEligibilityCheckDto>>(histRes);
        Assert.Single(history);
        Assert.Equal(check.CheckId, history[0].CheckId);
        Assert.Equal("CURRENT", history[0].Freshness);
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

    private sealed class ManualClock(DateTime now) : TimeProvider
    {
        public DateTime CurrentTime { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => new(CurrentTime);
    }
}
