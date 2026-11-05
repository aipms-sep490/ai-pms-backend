using System.Net;
using System.Net.Http.Json;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.ReviewGates;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Models;
using AIPMS.IntegrationTests.Supervisors;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;
using Task = System.Threading.Tasks.Task;

namespace AIPMS.IntegrationTests;

public sealed class ReviewGateCompositeMajorTests(ReviewGateBehaviorFixture fixture) : IClassFixture<ReviewGateBehaviorFixture>
{
    private sealed class TestFactory(ReviewGateBehaviorFixture db) : AipmsWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] = db.ConnectionString
                }));
        }
    }

    [Fact]
    public async Task Gate02_Interdisciplinary_With_Composite_Major_Superset_Resolves_Gates()
    {
        var s = fixture.Scenario;
        await using var db = fixture.CreateContext();
        var studentId = await CreateStudentAsync(db, s);
        var periodId = await CreateUniquePeriodAsync(db, s);

        var template = new ReviewGateTemplate
        {
            ProjectPeriodId = periodId,
            Code = "T-INTER-SUPER-" + Guid.NewGuid().ToString("N")[..6],
            Name = "Interdisciplinary Superset Template",
            Mode = "INTERDISCIPLINARY",
            Major = "SE+MKT+GD",
            Status = "PUBLISHED",
            Version = 1,
            ConcurrencyToken = Guid.NewGuid(),
            CreatedBy = s.StaffId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Gates =
            [
                new ReviewGateDefinition
                {
                    Code = "G_COMP_1", Sequence = 1, Title = "Gate 1", Objective = "Obj", Required = true,
                    BlockNextGate = true, BlocksDefense = true, Weight = 100m, ReviewerPolicy = "ASSIGNED_REVIEWER", ReviewScope = "COMMON",
                    Requirements = [new ReviewGateRequirement { Code = "R1", Title = "Doc", Type = "DOCUMENT", Scope = "COMMON", Verification = "HUMAN", Required = true }]
                }
            ]
        };
        db.ReviewGateTemplates.Add(template);

        var (project, _) = await CreateInterdisciplinaryProjectAsync(db, s, studentId, periodId);

        using var app = new TestFactory(fixture);
        using var student = app.CreateAuthenticatedClient(studentId, roles: [AppRoles.Student]);

        var response = await student.GetAsync($"/api/v1/projects/{project.Id}/review-gates/timeline");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var timeline = await response.Content.ReadFromJsonAsync<StudentReviewTimelineDto>();
        Assert.NotNull(timeline);
        Assert.Single(timeline.Gates);
        Assert.Equal("G_COMP_1", timeline.Gates[0].Code);
    }

    [Fact]
    public async Task Gate02_Interdisciplinary_With_Composite_Major_Exact_Resolves_Gates()
    {
        var s = fixture.Scenario;
        await using var db = fixture.CreateContext();
        var studentId = await CreateStudentAsync(db, s);
        var periodId = await CreateUniquePeriodAsync(db, s);

        var template = new ReviewGateTemplate
        {
            ProjectPeriodId = periodId,
            Code = "T-INTER-EXACT-" + Guid.NewGuid().ToString("N")[..6],
            Name = "Interdisciplinary Exact Template",
            Mode = "INTERDISCIPLINARY",
            Major = "SE+MKT",
            Status = "PUBLISHED",
            Version = 1,
            ConcurrencyToken = Guid.NewGuid(),
            CreatedBy = s.StaffId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Gates =
            [
                new ReviewGateDefinition
                {
                    Code = "G_EXACT_1", Sequence = 1, Title = "Gate 1", Objective = "Obj", Required = true,
                    BlockNextGate = true, BlocksDefense = true, Weight = 100m, ReviewerPolicy = "ASSIGNED_REVIEWER", ReviewScope = "COMMON",
                    Requirements = [new ReviewGateRequirement { Code = "R1", Title = "Doc", Type = "DOCUMENT", Scope = "COMMON", Verification = "HUMAN", Required = true }]
                }
            ]
        };
        db.ReviewGateTemplates.Add(template);

        var (project, _) = await CreateInterdisciplinaryProjectAsync(db, s, studentId, periodId);

        using var app = new TestFactory(fixture);
        using var student = app.CreateAuthenticatedClient(studentId, roles: [AppRoles.Student]);

        var response = await student.GetAsync($"/api/v1/projects/{project.Id}/review-gates/timeline");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var timeline = await response.Content.ReadFromJsonAsync<StudentReviewTimelineDto>();
        Assert.NotNull(timeline);
        Assert.Single(timeline.Gates);
        Assert.Equal("G_EXACT_1", timeline.Gates[0].Code);
    }

    [Fact]
    public async Task Gate02_Interdisciplinary_With_3_Gates_Configuration_Resolves()
    {
        var s = fixture.Scenario;
        await using var db = fixture.CreateContext();
        var studentId = await CreateStudentAsync(db, s);
        var periodId = await CreateUniquePeriodAsync(db, s);

        var template = new ReviewGateTemplate
        {
            ProjectPeriodId = periodId,
            Code = "T-INTER-3G-" + Guid.NewGuid().ToString("N")[..6],
            Name = "Interdisciplinary 3 Gates Template",
            Mode = "INTERDISCIPLINARY",
            Major = "SE+MKT+GD",
            Status = "PUBLISHED",
            Version = 1,
            ConcurrencyToken = Guid.NewGuid(),
            CreatedBy = s.StaffId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Gates =
            [
                new ReviewGateDefinition { Code = "G1", Sequence = 1, Title = "Gate 1", Objective = "O1", Required = true, Weight = 30m, ReviewerPolicy = "ASSIGNED_REVIEWER", ReviewScope = "COMMON", Requirements = [new() { Code = "R1", Title = "D1", Type = "DOCUMENT", Scope = "COMMON", Verification = "HUMAN", Required = true }] },
                new ReviewGateDefinition { Code = "G2", Sequence = 2, Title = "Gate 2", Objective = "O2", Required = true, Weight = 30m, ReviewerPolicy = "ASSIGNED_REVIEWER", ReviewScope = "COMMON", Requirements = [new() { Code = "R2", Title = "D2", Type = "DOCUMENT", Scope = "COMMON", Verification = "HUMAN", Required = true }] },
                new ReviewGateDefinition { Code = "G3", Sequence = 3, Title = "Gate 3", Objective = "O3", Required = true, Weight = 40m, ReviewerPolicy = "ASSIGNED_REVIEWER", ReviewScope = "COMMON", Requirements = [new() { Code = "R3", Title = "D3", Type = "DOCUMENT", Scope = "COMMON", Verification = "HUMAN", Required = true }] }
            ]
        };
        db.ReviewGateTemplates.Add(template);

        var (project, _) = await CreateInterdisciplinaryProjectAsync(db, s, studentId, periodId);

        using var app = new TestFactory(fixture);
        using var student = app.CreateAuthenticatedClient(studentId, roles: [AppRoles.Student]);

        var response = await student.GetAsync($"/api/v1/projects/{project.Id}/review-gates/timeline");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var timeline = await response.Content.ReadFromJsonAsync<StudentReviewTimelineDto>();
        Assert.NotNull(timeline);
        Assert.Equal(3, timeline.Gates.Count);
        Assert.Equal("G1", timeline.Gates[0].Code);
        Assert.Equal("G2", timeline.Gates[1].Code);
        Assert.Equal("G3", timeline.Gates[2].Code);
    }

    [Fact]
    public async Task Gate02_Interdisciplinary_With_5_Gates_Configuration_Resolves()
    {
        var s = fixture.Scenario;
        await using var db = fixture.CreateContext();
        var studentId = await CreateStudentAsync(db, s);
        var periodId = await CreateUniquePeriodAsync(db, s);

        var template = new ReviewGateTemplate
        {
            ProjectPeriodId = periodId,
            Code = "T-INTER-5G-" + Guid.NewGuid().ToString("N")[..6],
            Name = "Interdisciplinary 5 Gates Template",
            Mode = "INTERDISCIPLINARY",
            Major = "SE+MKT",
            Status = "PUBLISHED",
            Version = 1,
            ConcurrencyToken = Guid.NewGuid(),
            CreatedBy = s.StaffId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Gates =
            [
                new ReviewGateDefinition { Code = "G1", Sequence = 1, Title = "Gate 1", Objective = "O1", Required = true, Weight = 20m, ReviewerPolicy = "ASSIGNED_REVIEWER", ReviewScope = "COMMON", Requirements = [new() { Code = "R1", Title = "D1", Type = "DOCUMENT", Scope = "COMMON", Verification = "HUMAN", Required = true }] },
                new ReviewGateDefinition { Code = "G2", Sequence = 2, Title = "Gate 2", Objective = "O2", Required = true, Weight = 20m, ReviewerPolicy = "ASSIGNED_REVIEWER", ReviewScope = "COMMON", Requirements = [new() { Code = "R2", Title = "D2", Type = "DOCUMENT", Scope = "COMMON", Verification = "HUMAN", Required = true }] },
                new ReviewGateDefinition { Code = "G3", Sequence = 3, Title = "Gate 3", Objective = "O3", Required = true, Weight = 20m, ReviewerPolicy = "ASSIGNED_REVIEWER", ReviewScope = "COMMON", Requirements = [new() { Code = "R3", Title = "D3", Type = "DOCUMENT", Scope = "COMMON", Verification = "HUMAN", Required = true }] },
                new ReviewGateDefinition { Code = "G4", Sequence = 4, Title = "Gate 4", Objective = "O4", Required = true, Weight = 20m, ReviewerPolicy = "ASSIGNED_REVIEWER", ReviewScope = "COMMON", Requirements = [new() { Code = "R4", Title = "D4", Type = "DOCUMENT", Scope = "COMMON", Verification = "HUMAN", Required = true }] },
                new ReviewGateDefinition { Code = "G5", Sequence = 5, Title = "Gate 5", Objective = "O5", Required = true, Weight = 20m, ReviewerPolicy = "ASSIGNED_REVIEWER", ReviewScope = "COMMON", Requirements = [new() { Code = "R5", Title = "D5", Type = "DOCUMENT", Scope = "COMMON", Verification = "HUMAN", Required = true }] }
            ]
        };
        db.ReviewGateTemplates.Add(template);

        var (project, _) = await CreateInterdisciplinaryProjectAsync(db, s, studentId, periodId);

        using var app = new TestFactory(fixture);
        using var student = app.CreateAuthenticatedClient(studentId, roles: [AppRoles.Student]);

        var response = await student.GetAsync($"/api/v1/projects/{project.Id}/review-gates/timeline");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var timeline = await response.Content.ReadFromJsonAsync<StudentReviewTimelineDto>();
        Assert.NotNull(timeline);
        Assert.Equal(5, timeline.Gates.Count);
    }

    [Fact]
    public async Task Gate02_Interdisciplinary_Fallback_To_Generic_Interdisciplinary()
    {
        var s = fixture.Scenario;
        await using var db = fixture.CreateContext();
        var studentId = await CreateStudentAsync(db, s);
        var periodId = await CreateUniquePeriodAsync(db, s);

        var template = new ReviewGateTemplate
        {
            ProjectPeriodId = periodId,
            Code = "T-INTER-GEN-" + Guid.NewGuid().ToString("N")[..6],
            Name = "Interdisciplinary Generic Template",
            Mode = "INTERDISCIPLINARY",
            Major = "INTERDISCIPLINARY",
            Status = "PUBLISHED",
            Version = 1,
            ConcurrencyToken = Guid.NewGuid(),
            CreatedBy = s.StaffId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Gates =
            [
                new ReviewGateDefinition
                {
                    Code = "G_GEN_1", Sequence = 1, Title = "Gate Generic", Objective = "Obj", Required = true,
                    BlockNextGate = true, BlocksDefense = true, Weight = 100m, ReviewerPolicy = "ASSIGNED_REVIEWER", ReviewScope = "COMMON",
                    Requirements = [new ReviewGateRequirement { Code = "R1", Title = "Doc", Type = "DOCUMENT", Scope = "COMMON", Verification = "HUMAN", Required = true }]
                }
            ]
        };
        db.ReviewGateTemplates.Add(template);

        var (project, _) = await CreateInterdisciplinaryProjectAsync(db, s, studentId, periodId);

        using var app = new TestFactory(fixture);
        using var student = app.CreateAuthenticatedClient(studentId, roles: [AppRoles.Student]);

        var response = await student.GetAsync($"/api/v1/projects/{project.Id}/review-gates/timeline");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var timeline = await response.Content.ReadFromJsonAsync<StudentReviewTimelineDto>();
        Assert.NotNull(timeline);
        Assert.Single(timeline.Gates);
        Assert.Equal("G_GEN_1", timeline.Gates[0].Code);
    }

    private static async Task<long> CreateUniquePeriodAsync(AipmsDbContext db, BehaviorScenario s)
    {
        var period = new M.ProjectPeriod
        {
            AcademicSemesterId = s.SemesterId,
            Code = "PER-CM-" + Guid.NewGuid().ToString("N")[..8],
            Name = "Composite Major Period",
            PeriodType = "REGISTRATION",
            Status = "ACTIVE",
            StartAt = DateTime.UtcNow.AddDays(-1),
            EndAt = DateTime.UtcNow.AddDays(30)
        };
        db.ProjectPeriods.Add(period);
        await db.SaveChangesAsync();
        return period.Id;
    }

    private static async Task<(M.Project Project, M.Team Team)> CreateInterdisciplinaryProjectAsync(AipmsDbContext db, BehaviorScenario s, long studentId, long periodId)
    {
        var team = new M.Team
        {
            Code = "TEAM-M-" + Guid.NewGuid().ToString("N")[..6],
            Name = "Team Matched",
            Status = "LOCKED",
            CreatedBy = studentId,
            AcademicSemesterId = s.SemesterId
        };
        team.TeamMembers.Add(new M.TeamMember { UserId = studentId, AcademicSemesterId = s.SemesterId, IsLeader = true, JoinedAt = DateTime.UtcNow });

        var project = new M.Project
        {
            Code = "PRJ-M-" + Guid.NewGuid().ToString("N")[..6],
            Title = "Interdisciplinary Project Matched",
            Status = "ACTIVE",
            CreatedBy = studentId,
            Team = team
        };
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        db.ProjectMajors.AddRange(
            new M.ProjectMajor { ProjectId = project.Id, MajorId = s.SeMajorId },
            new M.ProjectMajor { ProjectId = project.Id, MajorId = s.MktMajorId }
        );
        await db.SaveChangesAsync();
        await AcademicSnapshotFixture.AddAsync(db, project.Id, periodId, s.ItDeptId, studentId, DateTime.UtcNow);
        await db.SaveChangesAsync();

        return (project, team);
    }

    private static async Task<long> CreateStudentAsync(AipmsDbContext db, BehaviorScenario s)
    {
        var user = new M.User
        {
            Email = "student-" + Guid.NewGuid().ToString("N")[..8] + "@example.test",
            FullName = "Test Student " + Guid.NewGuid().ToString("N")[..4],
            Status = "ACTIVE",
            AcademicProfileStatus = "VERIFIED",
            PasswordHash = "unused",
            DepartmentId = s.ItDeptId,
            MajorId = s.SeMajorId,
            UserRoleUsers = [new() { RoleId = s.StudentRoleId }]
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }
}
