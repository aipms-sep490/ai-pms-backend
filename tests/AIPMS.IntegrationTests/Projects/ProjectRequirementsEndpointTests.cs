using System.Net;
using System.Net.Http.Json;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Features.Projects.DTOs;

using AIPMS.Application.Features.Projects.Abstractions;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Models;
using AIPMS.IntegrationTests.Supervisors;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.IntegrationTests.Projects;

public sealed class ProjectRequirementsEndpointTests(SupervisorDatabaseFixture database) : IClassFixture<SupervisorDatabaseFixture>
{
    private sealed class Factory(SupervisorDatabaseFixture database, bool failAudit = false) : AipmsWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = database.ConnectionString,
                ["NotificationEmail:Enabled"] = "false", ["ScheduledNotifications:Enabled"] = "false"
            }));
            if (failAudit) builder.ConfigureServices(s => { s.RemoveAll<IAuditTrail>(); s.AddSingleton<IAuditTrail, FailingAudit>(); });
        }
    }

    private sealed class FailingAudit : IAuditTrail
    {
        public Task RecordAsync(AuditEntry entry, CancellationToken ct = default) => throw new InvalidOperationException("Injected audit failure.");
    }

    private sealed record Scenario(SupervisorScenario Users, long Project, long Major, long OtherMajor, long Team, string Token);

    private async Task<Scenario> Seed()
    {
        var users = await database.SeedAsync();
        await using var db = database.CreateContext();
        var now = DateTime.UtcNow;
        var organization = await db.Departments.Where(x => x.Id == users.DepartmentId).Select(x => x.OrganizationId).SingleAsync();
        var semester = new M.AcademicSemester { OrganizationId = organization, Code = Guid.NewGuid().ToString("N"), Name = "Requirements",
            StartDate = DateOnly.FromDateTime(now.AddDays(-10)), EndDate = DateOnly.FromDateTime(now.AddDays(90)), Status = "ACTIVE" };
        var major = new M.Major { DepartmentId = users.DepartmentId, Code = "SE", Name = "Software", IsActive = true };
        var other = new M.Major { DepartmentId = users.DepartmentId, Code = "AI", Name = "AI", IsActive = true };
        db.AcademicSemesters.Add(semester); db.Majors.AddRange(major, other);
        await db.SaveChangesAsync();
        var student = await db.Users.FindAsync(users.Student);
        student!.MajorId = major.Id;
        var project = new M.Project { Code = Guid.NewGuid().ToString("N"), Title = "Proposal", Status = "DRAFT", CreatedBy = users.Student,
            Team = new() { AcademicSemesterId = semester.Id, Code = Guid.NewGuid().ToString("N"), Name = "Team", Status = "FORMING", CreatedBy = users.Student,
                TeamMembers = [new() { AcademicSemesterId = semester.Id, UserId = users.Student, IsLeader = true }] } };
        db.Projects.Add(project);
        db.ProjectPeriods.Add(new() { AcademicSemesterId = semester.Id, Code = "REG", Name = "Registration", PeriodType = "REGISTRATION",
            Status = "ACTIVE", StartAt = now.AddDays(-1), EndAt = now.AddDays(10), MinTeamSize = 1, MaxTeamSize = 5, MinDistinctMajors = 1 });
        await db.SaveChangesAsync();
        db.ProjectMajors.AddRange(new() { ProjectId = project.Id, MajorId = major.Id }, new() { ProjectId = project.Id, MajorId = other.Id });
        await db.SaveChangesAsync();
        return new(users, project.Id, major.Id, other.Id, project.TeamId, Convert.ToBase64String(project.RowVersion));
    }

    private static ReplaceProjectRequirementsRequest Input(Scenario s) => new([new(s.Major, 1, 3, "Engineering")], s.Token);
    private static string Url(Scenario s) => $"/api/v1/projects/{s.Project}/major-requirements";

    [Theory]
    [InlineData("leader", HttpStatusCode.OK)]
    [InlineData("staff", HttpStatusCode.OK)]
    [InlineData("admin", HttpStatusCode.OK)]
    [InlineData("outside", HttpStatusCode.Forbidden)]
    [InlineData("forged-admin", HttpStatusCode.Forbidden)]
    [InlineData("inactive", HttpStatusCode.Forbidden)]
    public async Task Writes_use_persisted_role_scope_and_account_status(string kind, HttpStatusCode expected)
    {
        var s = await Seed(); using var factory = new Factory(database);
        var actor = kind switch { "staff" => s.Users.Staff, "admin" => s.Users.Admin, "outside" or "forged-admin" => s.Users.OutsideStaff, _ => s.Users.Student };
        if (kind == "inactive")
        {
            await using var db = database.CreateContext(); var row = await db.Users.FindAsync(actor); row!.Status = "INACTIVE"; await db.SaveChangesAsync();
        }
        using var client = factory.CreateAuthenticatedClient(actor, roles: kind is "admin" or "forged-admin" ? "ADMIN" : kind is "staff" or "outside" ? "DEPARTMENT_STAFF" : "STUDENT");
        Assert.Equal(expected, (await client.PutAsJsonAsync(Url(s), Input(s))).StatusCode);
        await using var check = database.CreateContext();
        Assert.Equal(expected == HttpStatusCode.OK ? 1 : 0, await check.ProjectMajorRequirements.CountAsync(x => x.ProjectId == s.Project));
    }

    [Theory]
    [InlineData("SUBMITTED", "FORMING")]
    [InlineData("REJECTED", "FORMING")]
    [InlineData("REVISION_REQUIRED", "LOCKED")]
    [InlineData("DRAFT", "LOCKED")]
    public async Task Submitted_or_locked_requirements_are_read_only(string status, string teamStatus)
    {
        var s = await Seed(); await using var db = database.CreateContext();
        var project = await db.Projects.FindAsync(s.Project); project!.Status = status;
        var team = await db.Teams.FindAsync(s.Team); team!.Status = teamStatus;
        await db.SaveChangesAsync(); s = s with { Token = Convert.ToBase64String(project.RowVersion) };
        using var factory = new Factory(database); using var client = factory.CreateAuthenticatedClient(s.Users.Student);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync(Url(s), Input(s))).StatusCode);
        Assert.False(await db.ProjectMajorRequirements.AnyAsync(x => x.ProjectId == s.Project));
    }

    [Theory]
    [InlineData("remove-member-major")]
    [InlineData("over-capacity")]
    [InlineData("foreign-major")]
    [InlineData("empty-responsibility")]
    [InlineData("missing-token")]
    public async Task Invalid_quota_scope_or_token_has_no_side_effect(string kind)
    {
        var s = await Seed(); var input = Input(s);
        input = kind switch
        {
            "remove-member-major" => input with { Requirements = [new(s.OtherMajor, 1, 3, "AI")] },
            "over-capacity" => input with { Requirements = [new(s.Major, 3, 4, "Engineering"), new(s.OtherMajor, 3, 4, "AI")] },
            "foreign-major" => input with { Requirements = [new(long.MaxValue, 1, 3, "Unknown")] },
            "empty-responsibility" => input with { Requirements = [new(s.Major, 1, 3, " ")] },
            _ => input with { ConcurrencyToken = null! }
        };
        using var factory = new Factory(database); using var client = factory.CreateAuthenticatedClient(s.Users.Student);
        Assert.Equal(kind is "empty-responsibility" or "missing-token" ? HttpStatusCode.BadRequest : HttpStatusCode.Conflict, (await client.PutAsJsonAsync(Url(s), input)).StatusCode);
        await using var db = database.CreateContext();
        Assert.False(await db.ProjectMajorRequirements.AnyAsync(x => x.ProjectId == s.Project));
        Assert.Equal(s.Token, Convert.ToBase64String((await db.Projects.FindAsync(s.Project))!.RowVersion));
    }

    [Fact]
    public async Task Leader_and_staff_writing_same_token_have_exactly_one_winner()
    {
        var s = await Seed(); using var factory = new Factory(database);
        using var leader = factory.CreateAuthenticatedClient(s.Users.Student);
        using var staff = factory.CreateAuthenticatedClient(s.Users.Staff, roles: "DEPARTMENT_STAFF");
        var replies = await Task.WhenAll(leader.PutAsJsonAsync(Url(s), Input(s)), staff.PutAsJsonAsync(Url(s), Input(s)));
        Assert.Single(replies.Where(x => x.StatusCode == HttpStatusCode.OK));
        Assert.Single(replies.Where(x => x.StatusCode == HttpStatusCode.Conflict));
        await using var db = database.CreateContext();
        Assert.Equal(1, await db.ProjectMajorRequirements.CountAsync(x => x.ProjectId == s.Project));
        Assert.Equal(1, await db.AuditLogs.CountAsync(x => x.Action == "PROJECT_MAJOR_REQUIREMENTS_REPLACED" && x.EntityId == s.Project.ToString()));
    }

    [Fact]
    public async Task Failed_audit_rolls_back_requirements_and_project_token()
    {
        var s = await Seed(); using var factory = new Factory(database, failAudit: true);
        using var client = factory.CreateAuthenticatedClient(s.Users.Student);
        Assert.Equal(HttpStatusCode.InternalServerError, (await client.PutAsJsonAsync(Url(s), Input(s))).StatusCode);
        await using var db = database.CreateContext();
        Assert.False(await db.ProjectMajorRequirements.AnyAsync(x => x.ProjectId == s.Project));
        Assert.Equal(s.Token, Convert.ToBase64String((await db.Projects.FindAsync(s.Project))!.RowVersion));
    }

    [Fact]
    public async Task Reads_require_authentication_and_persisted_project_access()
    {
        var s = await Seed(); using var factory = new Factory(database);
        using var anonymous = factory.CreateClient();
        using var outsider = factory.CreateAuthenticatedClient(s.Users.OutsideStaff, roles: "ADMIN");
        using var leader = factory.CreateAuthenticatedClient(s.Users.Student);
        foreach (var suffix in new[] { "major-requirements", "review-snapshots" })
        {
            var url = $"/api/v1/projects/{s.Project}/{suffix}";
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(url)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await outsider.GetAsync(url)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await leader.GetAsync(url)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await leader.GetAsync($"/api/v1/projects/{long.MaxValue}/{suffix}")).StatusCode);
        }
        Assert.Equal(HttpStatusCode.BadRequest, (await leader.GetAsync($"/api/v1/projects/{s.Project}/review-snapshots?pageSize=101")).StatusCode);
    }

    [Fact]
    public async Task Legacy_snapshot_reads_do_not_fabricate_proposal_or_requirements()
    {
        var s = await Seed(); using var factory = new Factory(database);
        await using var db = database.CreateContext();
        var project = await db.Projects.Include(x => x.Team).SingleAsync(x => x.Id == s.Project);
        var period = await db.ProjectPeriods.SingleAsync(x => x.AcademicSemesterId == project.Team.AcademicSemesterId);
        var evidence = new RegistrationEvidence(new("SINGLE_MAJOR", s.Major, s.Users.DepartmentId, [new(s.Major, 1, 3, "Historical")], Guid.Empty),
            new(1, 5, 1, "historical-v1"), 1, DateTime.UtcNow.AddDays(-2), DateTime.UtcNow.AddDays(10), [], [s.Users.DepartmentId]);
        var json = System.Text.Json.JsonSerializer.Serialize(evidence);
        db.Add(new ProjectRegistrationSnapshot { ProjectId = s.Project, ProjectPeriodId = period.Id, LeadDepartmentId = s.Users.DepartmentId,
            SubmittedBy = s.Users.Student, SubmittedAt = DateTime.UtcNow, SnapshotJson = json });
        await db.SaveChangesAsync();
        using var leader = factory.CreateAuthenticatedClient(s.Users.Student);
        var response = await leader.GetFromJsonAsync<ProjectReviewHistoryDto>($"/api/v1/projects/{s.Project}/review-snapshots");
        var item = Assert.Single(response!.Items);
        Assert.False(item.ProposalAvailable); Assert.Null(item.Evidence!.Proposal); Assert.Null(item.Evidence!.ProjectRequirements);
        Assert.Equal("historical-v1", item.Evidence!.Policy.Version);
        Assert.Equal("UNKNOWN", item.AcademicScopeProvenance);
        Assert.Equal(json, await db.Set<ProjectRegistrationSnapshot>().Where(x => x.ProjectId == s.Project).Select(x => x.SnapshotJson).SingleAsync());
    }

    [Fact]
    public async Task Swagger_exposes_requirements_history_and_retains_academic_review()
    {
        using var factory = new Factory(database); using var client = factory.CreateClient();
        using var document = System.Text.Json.JsonDocument.Parse(await client.GetStringAsync("/swagger/v1/swagger.json"));
        var paths = document.RootElement.GetProperty("paths");
        Assert.True(paths.GetProperty("/api/v1/projects/{projectId}/major-requirements").TryGetProperty("put", out _));
        Assert.True(paths.GetProperty("/api/v1/projects/{projectId}/major-requirements").TryGetProperty("get", out _));
        Assert.True(paths.GetProperty("/api/v1/projects/{projectId}/review-snapshots").TryGetProperty("get", out _));
        Assert.True(paths.GetProperty("/api/v1/projects/{id}/academic-review").TryGetProperty("get", out _));
    }

}
