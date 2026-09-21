using System.Net;
using System.Net.Http.Json;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.Academic.DTOs;
using AIPMS.Infrastructure.Persistence.Repositories;
using AIPMS.IntegrationTests.Supervisors;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.IntegrationTests;

public sealed class AcademicProfileEndpointTests(SupervisorDatabaseFixture database) : IClassFixture<SupervisorDatabaseFixture>
{
    private async Task<SupervisorScenario> SeedAsync()
    {
        var s = await database.SeedAsync();
        await using var db = database.CreateContext();
        var student = await db.Users.SingleAsync(u => u.Id == s.Student);
        student.Major = new() { DepartmentId = s.DepartmentId, Code = Guid.NewGuid().ToString("N"), Name = "Software", IsActive = true };
        student.AcademicProfileStatus = "PENDING";
        await db.SaveChangesAsync();
        return s;
    }

    [Fact]
    public async Task Student_pending_then_verified_then_rejected_with_audit_and_eligibility()
    {
        var s = await SeedAsync();
        using var app = new SupervisorFactory(database);
        using var student = app.CreateAuthenticatedClient(s.Student);
        using var staff = app.CreateAuthenticatedClient(s.Staff, roles: AppRoles.DepartmentStaff);
        Assert.Equal("PENDING", (await student.GetFromJsonAsync<AcademicProfileDto>("/api/v1/users/me/academic-profile"))!.Status);
        await using (var db = database.CreateContext())
            Assert.Null(await new TeamRepository(db).GetStudentAsync(s.Student, default));
        var result = await staff.PostAsync($"/api/v1/users/{s.Student}/academic-profile/verify", null);
        Assert.True(result.IsSuccessStatusCode, await result.Content.ReadAsStringAsync());
        Assert.Equal("VERIFIED", (await result.Content.ReadFromJsonAsync<AcademicProfileDto>())!.Status);
        await using (var db = database.CreateContext())
            Assert.NotNull(await new TeamRepository(db).GetStudentAsync(s.Student, default));
        result = await staff.PostAsJsonAsync($"/api/v1/users/{s.Student}/academic-profile/reject", new { reason = " Incorrect record " });
        Assert.True(result.IsSuccessStatusCode, await result.Content.ReadAsStringAsync());
        Assert.Equal("Incorrect record", (await result.Content.ReadFromJsonAsync<AcademicProfileDto>())!.RejectionReason);
        await using var finalDb = database.CreateContext();
        Assert.Equal(2, await finalDb.AcademicProfileVerifications.CountAsync(p => p.UserId == s.Student));
        Assert.Equal(2, await finalDb.AuditLogs.CountAsync(a => a.EntityId == s.Student.ToString() && a.Action.StartsWith("ACADEMIC_PROFILE_")));
        Assert.Null(await new TeamRepository(finalDb).GetStudentAsync(s.Student, default));
    }

    [Fact]
    public async Task Reviewer_scope_list_validation_and_stale_roles_are_enforced()
    {
        var s = await SeedAsync();
        using var app = new SupervisorFactory(database);
        using var other = app.CreateAuthenticatedClient(s.OutsideStaff, roles: AppRoles.DepartmentStaff);
        using var studentAsAdmin = app.CreateAuthenticatedClient(s.Student, roles: AppRoles.Admin);
        using var staff = app.CreateAuthenticatedClient(s.Staff, roles: AppRoles.DepartmentStaff);
        using var admin = app.CreateAuthenticatedClient(s.Admin, roles: AppRoles.Admin);
        foreach (var client in new[] { other, studentAsAdmin })
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync($"/api/v1/users/{s.Student}/academic-profile/verify", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await other.GetAsync($"/api/v1/academic/profile-verifications?departmentId={s.DepartmentId}")).StatusCode);
        var list = await staff.GetFromJsonAsync<PagedResult<AcademicProfileDto>>("/api/v1/academic/profile-verifications");
        Assert.All(list!.Items, p => Assert.Equal(s.DepartmentId, p.DepartmentId));
        Assert.Equal(HttpStatusCode.BadRequest, (await staff.GetAsync("/api/v1/academic/profile-verifications?pageSize=101")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await staff.PostAsJsonAsync($"/api/v1/users/{s.Student}/academic-profile/reject", new { reason = " " })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await staff.PostAsJsonAsync($"/api/v1/users/{s.Student}/academic-profile/reject", new { reason = new string('x', 2001) })).StatusCode);
        Assert.True((await admin.PostAsync($"/api/v1/users/{s.Student}/academic-profile/verify", null)).IsSuccessStatusCode);
    }

    [Theory]
    [InlineData("major")]
    [InlineData("department")]
    [InlineData("organization")]
    [InlineData("user")]
    [InlineData("mismatch")]
    public async Task Invalid_or_inactive_academic_records_cannot_be_verified(string invalid)
    {
        var s = await SeedAsync();
        await using (var db = database.CreateContext())
        {
            var user = await db.Users.Include(u => u.Major).Include(u => u.Department).ThenInclude(d => d!.Organization).SingleAsync(u => u.Id == s.Student);
            switch (invalid)
            {
                case "major": user.Major!.IsActive = false; break;
                case "department": user.Department!.IsActive = false; break;
                case "organization": user.Department!.Organization.IsActive = false; break;
                case "user": user.Status = "INACTIVE"; break;
                case "mismatch": user.DepartmentId = (await db.Users.FindAsync(s.OutsideStaff))!.DepartmentId; break;
            }
            await db.SaveChangesAsync();
        }
        using var app = new SupervisorFactory(database);
        using var admin = app.CreateAuthenticatedClient(s.Admin, roles: AppRoles.Admin);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsync($"/api/v1/users/{s.Student}/academic-profile/verify", null)).StatusCode);
    }

    [Fact]
    public async Task Audit_failure_rolls_back_both_status_and_review_history()
    {
        var s = await SeedAsync();
        using var app = new SupervisorFactory(database, failAudit: true);
        using var admin = app.CreateAuthenticatedClient(s.Admin, roles: AppRoles.Admin);
        Assert.Equal(HttpStatusCode.InternalServerError, (await admin.PostAsync($"/api/v1/users/{s.Student}/academic-profile/verify", null)).StatusCode);
        await using var db = database.CreateContext();
        Assert.Equal("PENDING", (await db.Users.FindAsync(s.Student))!.AcademicProfileStatus);
        Assert.False(await db.AcademicProfileVerifications.AnyAsync(p => p.UserId == s.Student));
    }
}
