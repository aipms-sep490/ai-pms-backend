using System.Net;
using System.Net.Http.Json;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.Supervisors.DTOs;
using AIPMS.Infrastructure.Persistence.Generated.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Task = System.Threading.Tasks.Task;

namespace AIPMS.IntegrationTests.Supervisors;

public sealed class SupervisorCandidateEndpointTests(SupervisorDatabaseFixture database)
    : IClassFixture<SupervisorDatabaseFixture>
{
    private static readonly DateTime Now = new(2026, 9, 10, 10, 0, 0, DateTimeKind.Utc);
    private sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(Now);
    }

    private static string Url(long projectId) => $"/api/v1/projects/{projectId}/supervisor-candidates";

    private static async System.Threading.Tasks.Task<PagedResult<SupervisorCandidateDto>> Read(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PagedResult<SupervisorCandidateDto>>())!;
    }

    [Fact]
    public async Task BR60_candidates_enforce_project_access_using_persisted_roles()
    {
        var s = await database.SeedAsync();
        var project = await SeedProject(s);
        using var app = new SupervisorFactory(database, clock: new Clock());
        using var anonymous = app.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(Url(project.Id))).StatusCode);
        foreach (var user in new[] { s.OutsideStaff, s.NewLecturer })
        {
            using var outsider = app.CreateAuthenticatedClient(user, roles: AppRoles.Admin);
            Assert.Equal(HttpStatusCode.Forbidden, (await outsider.GetAsync(Url(project.Id))).StatusCode);
        }
        foreach (var user in new[] { s.Student, s.Staff, s.Admin })
        {
            using var allowed = app.CreateAuthenticatedClient(user);
            Assert.Equal(s.ProfileId, Assert.Single((await Read(await allowed.GetAsync(Url(project.Id)))).Items).Id);
        }
        await using var db = database.CreateContext();
        (await db.Users.FindAsync(s.Student))!.Status = "INACTIVE";
        await db.SaveChangesAsync();
        using var inactive = app.CreateAuthenticatedClient(s.Student);
        Assert.Equal(HttpStatusCode.Forbidden, (await inactive.GetAsync(Url(project.Id))).StatusCode);
    }

    [Theory]
    [InlineData("DRAFT")]
    [InlineData("UNDER_REVIEW")]
    [InlineData("SUPERVISOR_PENDING")]
    [InlineData("ACTIVE")]
    [InlineData("COMPLETED")]
    [InlineData("ARCHIVED")]
    public async Task BR60_only_approved_projects_can_select_candidates(string status)
    {
        var s = await database.SeedAsync();
        var project = await SeedProject(s);
        await using var db = database.CreateContext();
        (await db.Projects.FindAsync(project.Id))!.Status = status;
        await db.SaveChangesAsync();
        using var app = new SupervisorFactory(database, clock: new Clock());
        using var client = app.CreateAuthenticatedClient(s.Admin);
        var response = await client.GetAsync(Url(project.Id));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(409, (await response.Content.ReadFromJsonAsync<ProblemDetails>())!.Status);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("ambiguous")]
    [InlineData("quota")]
    [InlineData("closed")]
    [InlineData("end")]
    [InlineData("future")]
    [InlineData("semester")]
    [InlineData("major")]
    public async Task Selection_fails_closed_without_unambiguous_active_policy_and_scope(string change)
    {
        var s = await database.SeedAsync();
        var project = await SeedProject(s);
        await using var db = database.CreateContext();
        var period = (await db.ProjectPeriods.FindAsync(project.PeriodId))!;
        if (change == "missing") db.ProjectPeriods.Remove(period);
        if (change == "ambiguous") db.ProjectPeriods.Add(new ProjectPeriod
        {
            AcademicSemesterId = project.SemesterId, Code = "SECOND", Name = "Duplicate selection",
            PeriodType = "SUPERVISOR_SELECTION", Status = "ACTIVE", StartAt = Now, EndAt = Now.AddHours(1),
            MaxProjectsPerSupervisor = 5
        });
        if (change == "quota") period.MaxProjectsPerSupervisor = null;
        if (change == "closed") period.Status = "CLOSED";
        if (change == "end") period.EndAt = Now;
        if (change == "future") period.StartAt = Now.AddSeconds(1);
        if (change == "semester") (await db.AcademicSemesters.FindAsync(project.SemesterId))!.Status = "CLOSED";
        if (change == "major") (await db.Majors.FindAsync(project.MajorId))!.IsActive = false;
        await db.SaveChangesAsync();
        using var app = new SupervisorFactory(database, clock: new Clock());
        using var client = app.CreateAuthenticatedClient(s.Admin);
        Assert.Equal(HttpStatusCode.Conflict, (await client.GetAsync(Url(project.Id))).StatusCode);
    }

    [Theory]
    [InlineData(3, 5, 1)]
    [InlineData(10, 2, 1)]
    [InlineData(null, 5, 4)]
    [InlineData(2, 5, 0)]
    [InlineData(10, 1, 0)]
    [InlineData(0, 5, 0)]
    public async Task BR61_capacity_counts_all_unended_projects_and_applies_both_limits(
        int? profileLimit, int semesterLimit, int remaining)
    {
        var s = await database.SeedAsync();
        var project = await SeedProject(s, semesterLimit);
        var other = await SeedProject(s);
        await using (var db = database.CreateContext())
        {
            (await db.SupervisorProfiles.FindAsync(s.ProfileId))!.MaxActiveProjects = profileLimit;
            await db.SaveChangesAsync();
        }
        await AddAssignment(s, project.SemesterId);
        await AddAssignment(s, other.SemesterId, status: "COMPLETED");
        await AddAssignment(s, project.SemesterId, ended: true);
        using var app = new SupervisorFactory(database, clock: new Clock());
        using var client = app.CreateAuthenticatedClient(s.Student);
        var result = await Read(await client.GetAsync(Url(project.Id)));
        if (remaining == 0)
        {
            Assert.Empty(result.Items);
            Assert.Equal(0, result.TotalCount);
            return;
        }
        var candidate = Assert.Single(result.Items);
        Assert.Equal(s.ProfileId, candidate.Id);
        Assert.Equal(2, candidate.ActiveProjects);
        Assert.Equal(1, candidate.SemesterActiveProjects);
        Assert.Equal(profileLimit, candidate.ProfileLimit);
        Assert.Equal(semesterLimit, candidate.SemesterLimit);
        Assert.Equal(remaining, candidate.RemainingSlots);
        Assert.Equal(project.PeriodId, candidate.SelectionPeriodId);
        Assert.Equal("Software Engineering", Assert.Single(candidate.Expertise).Name);
    }

    [Theory]
    [InlineData("unavailable")]
    [InlineData("account")]
    [InlineData("department")]
    [InlineData("role")]
    [InlineData("pending")]
    public async Task BR60_ineligible_and_already_requested_profiles_are_filtered_before_paging(string change)
    {
        var s = await database.SeedAsync();
        var project = await SeedProject(s);
        await using (var db = database.CreateContext())
        {
            if (change == "unavailable") (await db.SupervisorProfiles.FindAsync(s.ProfileId))!.IsAvailable = false;
            if (change == "account") (await db.Users.FindAsync(s.Lecturer))!.Status = "INACTIVE";
            if (change == "department") (await db.Users.FindAsync(s.Lecturer))!.DepartmentId =
                (await db.Users.FindAsync(s.OtherLecturer))!.DepartmentId;
            if (change == "role") db.UserRoles.RemoveRange(await db.UserRoles.Where(r => r.UserId == s.Lecturer).ToListAsync());
            if (change == "pending") db.SupervisorRequests.Add(new SupervisorRequest
            {
                ProjectId = project.Id, SupervisorProfileId = s.ProfileId, RequestedBy = s.Student,
                Status = "PENDING", RequestedAt = Now
            });
            await db.SaveChangesAsync();
        }
        using var app = new SupervisorFactory(database, clock: new Clock());
        using var client = app.CreateAuthenticatedClient(s.Admin);
        var result = await Read(await client.GetAsync(Url(project.Id) + "?pageSize=1"));
        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
    }

    [Fact]
    public async Task Filters_paging_validation_and_read_only_behavior_preserve_contract()
    {
        var s = await database.SeedAsync();
        var project = await SeedProject(s);
        await using var db = database.CreateContext();
        (await db.Users.FindAsync(s.NewLecturer))!.FullName = "Lecturer";
        var second = new SupervisorProfile
        {
            UserId = s.NewLecturer, IsAvailable = true,
            SupervisorExpertises = [new() { ExpertiseName = "Software Engineering" }]
        };
        db.SupervisorProfiles.Add(second);
        await db.SaveChangesAsync();
        var before = (await db.SupervisorRequests.CountAsync(), await db.SupervisorAssignments.CountAsync(), await db.AuditLogs.CountAsync());
        using var app = new SupervisorFactory(database, clock: new Clock());
        using var client = app.CreateAuthenticatedClient(s.Student);
        var first = await Read(await client.GetAsync(Url(project.Id) + "?pageSize=1&search=%20Lecturer%20&expertise=Engineering"));
        var page2 = await Read(await client.GetAsync(Url(project.Id) + "?pageSize=1&page=2"));
        Assert.Equal(s.ProfileId, Assert.Single(first.Items).Id);
        Assert.Equal(second.Id, Assert.Single(page2.Items).Id);
        Assert.Equal(2, first.TotalCount);
        Assert.Equal(2, page2.TotalPages);
        Assert.Equal(2, page2.Page);
        Assert.Empty((await Read(await client.GetAsync(Url(project.Id) + "?expertise=Unmatched"))).Items);
        Assert.Empty((await Read(await client.GetAsync(Url(project.Id) + "?page=3&pageSize=1"))).Items);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(Url(project.Id) + "?pageSize=101")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(Url(0))).StatusCode);
        using var admin = app.CreateAuthenticatedClient(s.Admin);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync(Url(long.MaxValue))).StatusCode);
        Assert.Equal(before, (await db.SupervisorRequests.CountAsync(), await db.SupervisorAssignments.CountAsync(), await db.AuditLogs.CountAsync()));
        Assert.Equal("APPROVED", (await db.Projects.AsNoTracking().SingleAsync(p => p.Id == project.Id)).Status);
    }

    [Fact]
    public async Task Existing_assignment_blocks_selection_even_if_project_status_is_stale()
    {
        var s = await database.SeedAsync();
        var project = await SeedProject(s);
        await AddAssignment(s, project.SemesterId, projectId: project.Id);
        using var app = new SupervisorFactory(database, clock: new Clock());
        using var client = app.CreateAuthenticatedClient(s.Student);
        Assert.Equal(HttpStatusCode.Conflict, (await client.GetAsync(Url(project.Id))).StatusCode);
    }

    [Fact]
    public async Task Pending_requests_on_other_projects_do_not_reserve_capacity_and_cancelled_requests_allow_selection()
    {
        var s = await database.SeedAsync();
        var project = await SeedProject(s, 1);
        var other = await SeedProject(s);
        await using var db = database.CreateContext();
        (await db.SupervisorProfiles.FindAsync(s.ProfileId))!.MaxActiveProjects = 1;
        db.SupervisorRequests.AddRange(
            new SupervisorRequest { ProjectId = other.Id, SupervisorProfileId = s.ProfileId,
                RequestedBy = s.Student, Status = "PENDING", RequestedAt = Now },
            new SupervisorRequest { ProjectId = project.Id, SupervisorProfileId = s.ProfileId,
                RequestedBy = s.Student, Status = "CANCELLED", RequestedAt = Now });
        await db.SaveChangesAsync();
        using var app = new SupervisorFactory(database, clock: new Clock());
        using var client = app.CreateAuthenticatedClient(s.Student);
        var candidate = Assert.Single((await Read(await client.GetAsync(Url(project.Id)))).Items);
        Assert.Equal(0, candidate.ActiveProjects);
        Assert.Equal(1, candidate.RemainingSlots);
    }

    private async System.Threading.Tasks.Task<CandidateProject> SeedProject(SupervisorScenario s, int limit = 5)
    {
        await using var db = database.CreateContext();
        var department = (await db.Departments.FindAsync(s.DepartmentId))!;
        var semester = new AcademicSemester
        {
            OrganizationId = department.OrganizationId, Code = Guid.NewGuid().ToString("N"), Name = "Semester",
            StartDate = DateOnly.FromDateTime(Now.AddDays(-30)), EndDate = DateOnly.FromDateTime(Now.AddDays(30)), Status = "ACTIVE"
        };
        var period = new ProjectPeriod
        {
            AcademicSemester = semester, Code = "SELECT", Name = "Selection", PeriodType = "SUPERVISOR_SELECTION",
            Status = "ACTIVE", StartAt = Now, EndAt = Now.AddDays(1), MaxProjectsPerSupervisor = limit
        };
        var major = new Major { DepartmentId = s.DepartmentId, Code = Guid.NewGuid().ToString("N"), Name = "SE", IsActive = true };
        var project = NewProject(s, semester);
        project.ProjectMajors.Add(new() { Major = major });
        db.ProjectPeriods.Add(period);
        await db.SaveChangesAsync();
        project.Team.TeamMembers.Add(new() { UserId = s.Student, AcademicSemesterId = semester.Id, IsLeader = true });
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        return new(project.Id, semester.Id, period.Id, major.Id);
    }

    private async Task AddAssignment(SupervisorScenario s, long semesterId, bool ended = false,
        string status = "ACTIVE", long? projectId = null)
    {
        await using var db = database.CreateContext();
        var project = projectId.HasValue ? (await db.Projects.FindAsync(projectId))!
            : NewProject(s, (await db.AcademicSemesters.FindAsync(semesterId))!, status);
        db.SupervisorAssignments.Add(new SupervisorAssignment
        {
            Project = project, SupervisorProfileId = s.ProfileId, IsPrimary = true,
            AssignedAt = Now.AddDays(-1), EndedAt = ended ? Now : null,
            SupervisorRequest = new()
            {
                Project = project, SupervisorProfileId = s.ProfileId, RequestedBy = s.Student,
                Status = "ACCEPTED", RequestedAt = Now.AddDays(-1), RespondedAt = Now.AddDays(-1)
            }
        });
        await db.SaveChangesAsync();
    }

    private static Project NewProject(SupervisorScenario s, AcademicSemester semester, string status = "APPROVED") => new()
    {
        Code = Guid.NewGuid().ToString("N"), Title = "Project", CreatedBy = s.Student, Status = status,
        RegisteredAt = Now, Team = new()
        {
            AcademicSemester = semester, Code = Guid.NewGuid().ToString("N"), Name = "Team", Status = "FORMING", CreatedBy = s.Student
        }
    };

    private sealed record CandidateProject(long Id, long SemesterId, long PeriodId, long MajorId);
}
