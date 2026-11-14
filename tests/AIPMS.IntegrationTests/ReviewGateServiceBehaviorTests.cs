using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.IO.Compression;
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

public sealed class ReviewGateBehaviorFixture : IAsyncLifetime
{
    private readonly SupervisorDatabaseFixture database = new();
    public string ConnectionString => database.ConnectionString;
    public AipmsDbContext CreateContext() => database.CreateContext();
    public BehaviorScenario Scenario { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await database.InitializeAsync();
        Scenario = await SeedBaseAsync();
    }

    public Task DisposeAsync() => database.DisposeAsync();

    private async Task<BehaviorScenario> SeedBaseAsync()
    {
        var users = await database.SeedAsync();
        await using var db = CreateContext();

        var itDept = (await db.Departments.FindAsync(users.DepartmentId))!;
        var busStaffUser = await db.Users.FindAsync(users.OutsideStaff);
        var busDept = (await db.Departments.FindAsync(busStaffUser!.DepartmentId!.Value))!;

        var majorSe = new M.Major { Code = "SE", Name = "Software Engineering", DepartmentId = itDept.Id, IsActive = true };
        var majorMkt = new M.Major { Code = "MKT", Name = "Marketing", DepartmentId = busDept.Id, IsActive = true };

        var semester = new M.AcademicSemester
        {
            OrganizationId = itDept.OrganizationId,
            Code = "SEM-" + Guid.NewGuid().ToString("N")[..8],
            Name = "Behavior Semester",
            Status = "ACTIVE",
            StartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-10)),
            EndDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(90))
        };

        var rubric = new M.Rubric { Name = "Main Rubric", Code = "R-MAIN-" + Guid.NewGuid().ToString("N")[..8], DepartmentId = itDept.Id, AcademicSemester = semester, CreatedBy = users.Admin, IsActive = true };

        var period = new M.ProjectPeriod
        {
            AcademicSemester = semester,
            Rubric = rubric,
            Code = "PER-BHV-" + Guid.NewGuid().ToString("N")[..8],
            Name = "Behavior Project Period",
            PeriodType = "REGISTRATION",
            Status = "ACTIVE",
            StartAt = DateTime.UtcNow.AddDays(-1),
            EndAt = DateTime.UtcNow.AddDays(30)
        };

        var lecturerRole = await db.Roles.SingleAsync(r => r.Code == "LECTURER");
        var lecturer2 = new M.User
        {
            Email = "lecturer2-" + Guid.NewGuid().ToString("N")[..6] + "@example.test",
            FullName = "Second Lecturer",
            Status = "ACTIVE",
            AcademicProfileStatus = "VERIFIED",
            PasswordHash = "unused",
            DepartmentId = itDept.Id,
            UserRoleUsers = [new() { RoleId = lecturerRole.Id }]
        };
        var profile2 = new M.SupervisorProfile
        {
            User = lecturer2,
            Bio = "Second",
            IsAvailable = true,
            MaxActiveProjects = 3
        };

        db.AddRange(majorSe, majorMkt, semester, rubric, period, lecturer2, profile2);
        await db.SaveChangesAsync();

        var supervisorProfile1 = await db.SupervisorProfiles.SingleAsync(p => p.UserId == users.Lecturer);

        var defaultTemplate = new ReviewGateTemplate
        {
            ProjectPeriodId = period.Id,
            Code = "T-DEFAULT-" + Guid.NewGuid().ToString("N")[..8],
            Name = "Default Behavior Template",
            Mode = "SINGLE_MAJOR",
            Major = "SE",
            Status = "PUBLISHED",
            Version = 1,
            ConcurrencyToken = Guid.NewGuid(),
            CreatedBy = users.Staff,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Gates =
            [
                new ReviewGateDefinition
                {
                    Code = "G-DEF", Sequence = 1, Title = "Default Gate", Objective = "Default", Required = true,
                    BlockNextGate = true, BlocksDefense = false, Weight = 100m, ReviewerPolicy = "ASSIGNED_REVIEWER", ReviewScope = "COMMON",
                    Requirements = [new ReviewGateRequirement { Code = "R-DEF", Title = "Def", Type = "DOCUMENT", Scope = "COMMON", Verification = "HUMAN", Required = true }]
                }
            ]
        };
        db.ReviewGateTemplates.Add(defaultTemplate);
        await db.SaveChangesAsync();

        var studentRole = await db.Roles.SingleAsync(r => r.Code == "STUDENT");

        return new BehaviorScenario(
            users.Admin,
            users.Staff,
            users.OutsideStaff,
            users.Lecturer,
            lecturer2.Id,
            users.Student,
            itDept.Id,
            busDept.Id,
            majorSe.Id,
            majorMkt.Id,
            semester.Id,
            period.Id,
            supervisorProfile1.Id,
            profile2.Id,
            studentRole.Id,
            defaultTemplate.Id);
    }
}

public sealed record BehaviorScenario(
    long AdminId,
    long StaffId,
    long OutsideStaffId,
    long Lecturer1Id,
    long Lecturer2Id,
    long StudentId,
    long ItDeptId,
    long BusDeptId,
    long SeMajorId,
    long MktMajorId,
    long SemesterId,
    long PeriodId,
    long Lecturer1ProfileId,
    long Lecturer2ProfileId,
    long StudentRoleId,
    long DefaultTemplateId);

public sealed class ReviewGateServiceBehaviorTests(ReviewGateBehaviorFixture fixture) : IClassFixture<ReviewGateBehaviorFixture>
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

    private static ReviewGateDefinitionInput ValidGate(string code = "G1", int sequence = 1, decimal? weight = 100m, bool hasRequirement = true) =>
        new(
            code,
            sequence,
            $"Gate {code}",
            "Objective",
            true,
            true,
            true,
            DateTime.UtcNow.AddDays(1),
            DateTime.UtcNow.AddDays(5),
            "ASSIGNED_REVIEWER",
            "COMMON",
            hasRequirement
                ? [new ReviewGateRequirementInput($"R-{code}", "Report", "Submit report", "DOCUMENT", "COMMON", null, null, "HUMAN", true)]
                : [],
            weight);

    // ==========================================
    // 1. SaveTemplate với Gates rỗng => 400/422
    // ==========================================
    [Fact]
    public async Task Test01_SaveTemplate_With_Empty_Gates_Fails()
    {
        var s = fixture.Scenario;
        using var app = new TestFactory(fixture);
        using var staff = app.CreateAuthenticatedClient(s.StaffId, roles: [AppRoles.DepartmentStaff]);

        var request = new SaveReviewGateTemplateRequest(s.PeriodId, "T-EMPTY", "Empty Gates Template", "SINGLE_MAJOR", "SE", "1.0", null, []);
        var response = await staff.PostAsJsonAsync("/api/v1/academic/gate-templates", request);

        Assert.True(response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity);
    }

    // ==========================================
    // 2. SaveTemplate với Sequence trùng => 400/422
    // ==========================================
    [Fact]
    public async Task Test02_SaveTemplate_With_Duplicate_Sequence_Fails()
    {
        var s = fixture.Scenario;
        using var app = new TestFactory(fixture);
        using var staff = app.CreateAuthenticatedClient(s.StaffId, roles: [AppRoles.DepartmentStaff]);

        var gates = new List<ReviewGateDefinitionInput>
        {
            ValidGate("G1", 1, 50m),
            ValidGate("G2", 1, 50m) // duplicate sequence 1
        };
        var request = new SaveReviewGateTemplateRequest(s.PeriodId, "T-DUP-SEQ", "Duplicate Sequence", "SINGLE_MAJOR", "SE", "1.0", null, gates);
        var response = await staff.PostAsJsonAsync("/api/v1/academic/gate-templates", request);

        Assert.True(response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity);
    }

    // ==========================================
    // 3. SaveTemplate với Code gate trùng (khác hoa thường) => 400/422
    // ==========================================
    [Fact]
    public async Task Test03_SaveTemplate_With_Duplicate_Code_Different_Case_Fails()
    {
        var s = fixture.Scenario;
        using var app = new TestFactory(fixture);
        using var staff = app.CreateAuthenticatedClient(s.StaffId, roles: [AppRoles.DepartmentStaff]);

        var gates = new List<ReviewGateDefinitionInput>
        {
            ValidGate("G1", 1, 50m),
            ValidGate("g1", 2, 50m) // duplicate code with different case
        };
        var request = new SaveReviewGateTemplateRequest(s.PeriodId, "T-DUP-CODE", "Duplicate Code", "SINGLE_MAJOR", "SE", "1.0", null, gates);
        var response = await staff.PostAsJsonAsync("/api/v1/academic/gate-templates", request);

        Assert.True(response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity);
    }

    // ==========================================
    // 4. SaveTemplate với Mode lạ => 400/422
    // ==========================================
    [Fact]
    public async Task Test04_SaveTemplate_With_Unsupported_Mode_Fails()
    {
        var s = fixture.Scenario;
        using var app = new TestFactory(fixture);
        using var staff = app.CreateAuthenticatedClient(s.StaffId, roles: [AppRoles.DepartmentStaff]);

        var request = new SaveReviewGateTemplateRequest(s.PeriodId, "T-BAD-MODE", "Bad Mode", "UNKNOWN_MODE", "SE", "1.0", null, [ValidGate()]);
        var response = await staff.PostAsJsonAsync("/api/v1/academic/gate-templates", request);

        Assert.True(response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity);
    }

    // ==========================================
    // 5. PublishTemplate khi có gate không có requirement => 400/422
    // ==========================================
    [Fact]
    public async Task Test05_PublishTemplate_When_Gate_Has_No_Requirements_Fails()
    {
        var s = fixture.Scenario;
        using var app = new TestFactory(fixture);
        using var staff = app.CreateAuthenticatedClient(s.StaffId, roles: [AppRoles.DepartmentStaff]);

        // Save draft with a gate that has 0 requirements
        var saveReq = new SaveReviewGateTemplateRequest(s.PeriodId, "T-NOREQ-" + Guid.NewGuid().ToString("N")[..6], "No Req", "SINGLE_MAJOR", "SE", "1.0", null, [ValidGate(hasRequirement: false)]);
        var saveRes = await staff.PostAsJsonAsync("/api/v1/academic/gate-templates", saveReq);
        Assert.Equal(HttpStatusCode.OK, saveRes.StatusCode);
        var created = await saveRes.Content.ReadFromJsonAsync<ReviewGateTemplateDto>();
        Assert.NotNull(created);

        // Try to publish
        var pubReq = new PublishReviewGateTemplateRequest(created.ConcurrencyToken);
        var pubRes = await staff.PostAsJsonAsync($"/api/v1/academic/gate-templates/{created.Id}/publish", pubReq);

        Assert.True(pubRes.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity);
    }

    // ==========================================
    // 6. PublishTemplate không kèm ConcurrencyToken => 400/422
    // ==========================================
    [Fact]
    public async Task Test06_PublishTemplate_Without_ConcurrencyToken_Fails()
    {
        var s = fixture.Scenario;
        using var app = new TestFactory(fixture);
        using var staff = app.CreateAuthenticatedClient(s.StaffId, roles: [AppRoles.DepartmentStaff]);

        var saveReq = new SaveReviewGateTemplateRequest(s.PeriodId, "T-NOTOKEN-" + Guid.NewGuid().ToString("N")[..6], "No Token", "SINGLE_MAJOR", "SE", "1.0", null, [ValidGate()]);
        var saveRes = await staff.PostAsJsonAsync("/api/v1/academic/gate-templates", saveReq);
        var created = await saveRes.Content.ReadFromJsonAsync<ReviewGateTemplateDto>();
        Assert.NotNull(created);

        var pubReq = new PublishReviewGateTemplateRequest(null);
        var pubRes = await staff.PostAsJsonAsync($"/api/v1/academic/gate-templates/{created.Id}/publish", pubReq);

        Assert.True(pubRes.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity);
    }

    // ==========================================
    // 7. PublishTemplate với token cũ => 409
    // ==========================================
    [Fact]
    public async Task Test07_PublishTemplate_With_Stale_Token_Fails_409()
    {
        var s = fixture.Scenario;
        using var app = new TestFactory(fixture);
        using var staff = app.CreateAuthenticatedClient(s.StaffId, roles: [AppRoles.DepartmentStaff]);

        var saveReq = new SaveReviewGateTemplateRequest(s.PeriodId, "T-STALE-" + Guid.NewGuid().ToString("N")[..6], "Stale Token", "SINGLE_MAJOR", "SE", "1.0", null, [ValidGate()]);
        var saveRes = await staff.PostAsJsonAsync("/api/v1/academic/gate-templates", saveReq);
        var created = await saveRes.Content.ReadFromJsonAsync<ReviewGateTemplateDto>();
        Assert.NotNull(created);

        var pubReq = new PublishReviewGateTemplateRequest(Guid.NewGuid().ToString("N")); // stale token
        var pubRes = await staff.PostAsJsonAsync($"/api/v1/academic/gate-templates/{created.Id}/publish", pubReq);

        Assert.Equal(HttpStatusCode.Conflict, pubRes.StatusCode);
    }

    // ==========================================
    // 8. PublishTemplate hai lần => lần 2 trả về bình thường, không lỗi
    // ==========================================
    [Fact]
    public async Task Test08_PublishTemplate_Twice_Succeeds_Early_Return()
    {
        var s = fixture.Scenario;
        using var app = new TestFactory(fixture);
        using var staff = app.CreateAuthenticatedClient(s.StaffId, roles: [AppRoles.DepartmentStaff]);

        var saveReq = new SaveReviewGateTemplateRequest(s.PeriodId, "T-TWICE-" + Guid.NewGuid().ToString("N")[..6], "Twice", "SINGLE_MAJOR", "SE", "1.0", null, [ValidGate()]);
        var saveRes = await staff.PostAsJsonAsync("/api/v1/academic/gate-templates", saveReq);
        var created = await saveRes.Content.ReadFromJsonAsync<ReviewGateTemplateDto>();
        Assert.NotNull(created);

        // 1st publish
        var pubReq1 = new PublishReviewGateTemplateRequest(created.ConcurrencyToken);
        var pubRes1 = await staff.PostAsJsonAsync($"/api/v1/academic/gate-templates/{created.Id}/publish", pubReq1);
        Assert.Equal(HttpStatusCode.OK, pubRes1.StatusCode);
        var pubDto1 = await pubRes1.Content.ReadFromJsonAsync<ReviewGateTemplateDto>();
        Assert.Equal("PUBLISHED", pubDto1!.Status);

        // 2nd publish with updated token
        var pubReq2 = new PublishReviewGateTemplateRequest(pubDto1.ConcurrencyToken);
        var pubRes2 = await staff.PostAsJsonAsync($"/api/v1/academic/gate-templates/{created.Id}/publish", pubReq2);
        Assert.Equal(HttpStatusCode.OK, pubRes2.StatusCode);
        var pubDto2 = await pubRes2.Content.ReadFromJsonAsync<ReviewGateTemplateDto>();
        Assert.Equal("PUBLISHED", pubDto2!.Status);
    }

    // ==========================================
    // 9. Template 3 gate publish được => 200
    // ==========================================
    [Fact]
    public async Task Test09_PublishTemplate_With_3_Gates_Succeeds_200()
    {
        var s = fixture.Scenario;
        using var app = new TestFactory(fixture);
        using var staff = app.CreateAuthenticatedClient(s.StaffId, roles: [AppRoles.DepartmentStaff]);

        var gates = new List<ReviewGateDefinitionInput>
        {
            ValidGate("G1", 1, 30m),
            ValidGate("G2", 2, 30m),
            ValidGate("G3", 3, 40m)
        };
        var saveReq = new SaveReviewGateTemplateRequest(s.PeriodId, "T-3GATES-" + Guid.NewGuid().ToString("N")[..6], "3 Gates", "SINGLE_MAJOR", "SE", "1.0", null, gates);
        var saveRes = await staff.PostAsJsonAsync("/api/v1/academic/gate-templates", saveReq);
        var created = await saveRes.Content.ReadFromJsonAsync<ReviewGateTemplateDto>();
        Assert.NotNull(created);

        var pubReq = new PublishReviewGateTemplateRequest(created.ConcurrencyToken);
        var pubRes = await staff.PostAsJsonAsync($"/api/v1/academic/gate-templates/{created.Id}/publish", pubReq);

        Assert.Equal(HttpStatusCode.OK, pubRes.StatusCode);
        var published = await pubRes.Content.ReadFromJsonAsync<ReviewGateTemplateDto>();
        Assert.Equal(3, published!.GatesCount);
        Assert.Equal("PUBLISHED", published.Status);
    }

    // ==========================================
    // 10. Template 5 gate publish được => 200 (N12: số mốc là cấu hình)
    // ==========================================
    [Fact]
    public async Task Test10_PublishTemplate_With_5_Gates_Succeeds_200()
    {
        var s = fixture.Scenario;
        using var app = new TestFactory(fixture);
        using var staff = app.CreateAuthenticatedClient(s.StaffId, roles: [AppRoles.DepartmentStaff]);

        var gates = new List<ReviewGateDefinitionInput>
        {
            ValidGate("G1", 1, 20m),
            ValidGate("G2", 2, 20m),
            ValidGate("G3", 3, 20m),
            ValidGate("G4", 4, 20m),
            ValidGate("G5", 5, 20m)
        };
        var saveReq = new SaveReviewGateTemplateRequest(s.PeriodId, "T-5GATES-" + Guid.NewGuid().ToString("N")[..6], "5 Gates", "SINGLE_MAJOR", "SE", "1.0", null, gates);
        var saveRes = await staff.PostAsJsonAsync("/api/v1/academic/gate-templates", saveReq);
        var created = await saveRes.Content.ReadFromJsonAsync<ReviewGateTemplateDto>();
        Assert.NotNull(created);

        var pubReq = new PublishReviewGateTemplateRequest(created.ConcurrencyToken);
        var pubRes = await staff.PostAsJsonAsync($"/api/v1/academic/gate-templates/{created.Id}/publish", pubReq);

        Assert.Equal(HttpStatusCode.OK, pubRes.StatusCode);
        var published = await pubRes.Content.ReadFromJsonAsync<ReviewGateTemplateDto>();
        Assert.Equal(5, published!.GatesCount);
        Assert.Equal("PUBLISHED", published.Status);
    }

    // ==========================================
    // 11. Mode = "INTERDISCIPLINARY", Major = "SE+MKT+GD"
    // TODO(GATE-02): ReviewGateService.cs:572 string match for "INTERDISCIPLINARY" fails when Major is composite like "SE+MKT+GD", returning 0 gates without error
    // ==========================================
    [Fact]
    public async Task Test11_EnsureInstances_Interdisciplinary_With_Non_Interdisciplinary_Major_Returns_0_Gates()
    {
        var s = fixture.Scenario;
        await using var db = fixture.CreateContext();
        var studentId = await CreateStudentAsync(db, s);

        // Template published with Mode = "INTERDISCIPLINARY" and Major = "SE+MKT+GD"
        var template = new ReviewGateTemplate
        {
            ProjectPeriodId = s.PeriodId,
            Code = "T-INTER-TRAP-" + Guid.NewGuid().ToString("N")[..6],
            Name = "Interdisciplinary Trap Template",
            Mode = "INTERDISCIPLINARY",
            Major = "AI+DS",
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
                    Code = "G1", Sequence = 1, Title = "Gate 1", Objective = "Obj", Required = true,
                    BlockNextGate = true, BlocksDefense = true, Weight = 100m, ReviewerPolicy = "ASSIGNED_REVIEWER", ReviewScope = "COMMON",
                    Requirements = [new ReviewGateRequirement { Code = "R1", Title = "Doc", Type = "DOCUMENT", Scope = "COMMON", Verification = "HUMAN", Required = true }]
                }
            ]
        };
        db.ReviewGateTemplates.Add(template);

        // Project with 2 majors (SE and MKT) => interdisciplinary scope
        var team = new M.Team
        {
            Code = "TEAM-TRAP-" + Guid.NewGuid().ToString("N")[..6],
            Name = "Team Trap",
            Status = "LOCKED",
            CreatedBy = studentId,
            AcademicSemesterId = s.SemesterId
        };
        team.TeamMembers.Add(new M.TeamMember { UserId = studentId, AcademicSemesterId = s.SemesterId, IsLeader = true, JoinedAt = DateTime.UtcNow });

        var project = new M.Project
        {
            Code = "PRJ-TRAP-" + Guid.NewGuid().ToString("N")[..6],
            Title = "Interdisciplinary Project",
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
        await AcademicSnapshotFixture.AddAsync(db, project.Id, s.PeriodId, s.ItDeptId, studentId, DateTime.UtcNow);
        await db.SaveChangesAsync();

        using var app = new TestFactory(fixture);
        using var student = app.CreateAuthenticatedClient(studentId, roles: [AppRoles.Student]);

        // Timeline calls EnsureInstances. Since Major != "INTERDISCIPLINARY", it returns 0 gates without error!
        var response = await student.GetAsync($"/api/v1/projects/{project.Id}/review-gates/timeline");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var timeline = await response.Content.ReadFromJsonAsync<StudentReviewTimelineDto>();
        Assert.NotNull(timeline);
        Assert.Empty(timeline.Gates); // 0 gates, no error thrown
    }

    // ==========================================
    // 12. Timeline khi project có 0 requirement => AcademicProgress = null + Source = "NOT_CONFIGURED" (N6)
    // ==========================================
    [Fact]
    public async Task Test12_Timeline_With_0_Configured_Requirements_AcademicProgress_Is_Null_And_NotConfigured()
    {
        var s = fixture.Scenario;
        await using var db = fixture.CreateContext();
        var studentId = await CreateStudentAsync(db, s);

        var team = new M.Team
        {
            Code = "TEAM-NOCONF-" + Guid.NewGuid().ToString("N")[..6],
            Name = "Team Unconfigured",
            Status = "LOCKED",
            CreatedBy = studentId,
            AcademicSemesterId = s.SemesterId
        };
        team.TeamMembers.Add(new M.TeamMember { UserId = studentId, AcademicSemesterId = s.SemesterId, IsLeader = true, JoinedAt = DateTime.UtcNow });

        var project = new M.Project
        {
            Code = "PRJ-NOCONF-" + Guid.NewGuid().ToString("N")[..6],
            Title = "Unconfigured Project",
            Status = "ACTIVE",
            CreatedBy = studentId,
            Team = team
        };
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        db.ProjectMajors.Add(new M.ProjectMajor { ProjectId = project.Id, MajorId = s.SeMajorId });
        db.Set<ProjectRegistrationSnapshot>().Add(new ProjectRegistrationSnapshot
        {
            ProjectId = project.Id,
            ProjectPeriodId = s.PeriodId,
            LeadDepartmentId = s.ItDeptId,
            SubmittedBy = studentId,
            SubmittedAt = DateTime.UtcNow,
            SnapshotJson = "{}"
        });

        // Gate instances with no weights configured (Weight = null)
        db.ProjectGateInstances.Add(new ProjectGateInstance
        {
            ProjectId = project.Id,
            TemplateId = s.DefaultTemplateId,
            TemplateVersion = 1,
            Code = "G1",
            Sequence = 1,
            Title = "Gate 1",
            Status = "PLANNED",
            ReviewEligibility = "READY",
            Required = true,
            BlockNextGate = true,
            BlocksDefense = false,
            Weight = null, // unconfigured weight
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        using var app = new TestFactory(fixture);
        using var student = app.CreateAuthenticatedClient(studentId, roles: [AppRoles.Student]);

        var response = await student.GetAsync($"/api/v1/projects/{project.Id}/review-gates/timeline");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var timeline = await response.Content.ReadFromJsonAsync<StudentReviewTimelineDto>();
        Assert.NotNull(timeline);
        Assert.Null(timeline.AcademicProgress); // AcademicProgress = null, NOT 0% (N6)
        Assert.Equal("NOT_CONFIGURED", timeline.AcademicProgressSource);
    }

    // ==========================================
    // 13. Timeline khi mới qua gate 1/4 => AcademicProgress theo Weight, không theo số gate
    // ==========================================
    [Fact]
    public async Task Test13_Timeline_Progress_Reflects_Gate_Weights_Not_Gate_Count()
    {
        var s = fixture.Scenario;
        await using var db = fixture.CreateContext();
        var studentId = await CreateStudentAsync(db, s);

        var team = new M.Team
        {
            Code = "TEAM-WEIGHT-" + Guid.NewGuid().ToString("N")[..6],
            Name = "Team Weight",
            Status = "LOCKED",
            CreatedBy = studentId,
            AcademicSemesterId = s.SemesterId
        };
        team.TeamMembers.Add(new M.TeamMember { UserId = studentId, AcademicSemesterId = s.SemesterId, IsLeader = true, JoinedAt = DateTime.UtcNow });

        var project = new M.Project
        {
            Code = "PRJ-WEIGHT-" + Guid.NewGuid().ToString("N")[..6],
            Title = "Weighted Project",
            Status = "ACTIVE",
            CreatedBy = studentId,
            Team = team
        };
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        db.ProjectMajors.Add(new M.ProjectMajor { ProjectId = project.Id, MajorId = s.SeMajorId });
        db.Set<ProjectRegistrationSnapshot>().Add(new ProjectRegistrationSnapshot
        {
            ProjectId = project.Id,
            ProjectPeriodId = s.PeriodId,
            LeadDepartmentId = s.ItDeptId,
            SubmittedBy = studentId,
            SubmittedAt = DateTime.UtcNow,
            SnapshotJson = "{}"
        });

        // 4 gates: weights 40, 20, 20, 20. Gate 1 PASSED.
        db.ProjectGateInstances.AddRange(
            new ProjectGateInstance { ProjectId = project.Id, TemplateId = s.DefaultTemplateId, TemplateVersion = 1, Code = "G1", Sequence = 1, Title = "Gate 1", Status = "PASSED", ReviewEligibility = "READY", Required = true, BlockNextGate = true, BlocksDefense = false, Weight = 40m, CreatedAt = DateTime.UtcNow },
            new ProjectGateInstance { ProjectId = project.Id, TemplateId = s.DefaultTemplateId, TemplateVersion = 1, Code = "G2", Sequence = 2, Title = "Gate 2", Status = "PLANNED", ReviewEligibility = "READY", Required = true, BlockNextGate = true, BlocksDefense = false, Weight = 20m, CreatedAt = DateTime.UtcNow },
            new ProjectGateInstance { ProjectId = project.Id, TemplateId = s.DefaultTemplateId, TemplateVersion = 1, Code = "G3", Sequence = 3, Title = "Gate 3", Status = "LOCKED", ReviewEligibility = "BLOCKED", Required = true, BlockNextGate = true, BlocksDefense = false, Weight = 20m, CreatedAt = DateTime.UtcNow },
            new ProjectGateInstance { ProjectId = project.Id, TemplateId = s.DefaultTemplateId, TemplateVersion = 1, Code = "G4", Sequence = 4, Title = "Gate 4", Status = "LOCKED", ReviewEligibility = "BLOCKED", Required = true, BlockNextGate = true, BlocksDefense = true, Weight = 20m, CreatedAt = DateTime.UtcNow }
        );
        await db.SaveChangesAsync();

        using var app = new TestFactory(fixture);
        using var student = app.CreateAuthenticatedClient(studentId, roles: [AppRoles.Student]);

        var response = await student.GetAsync($"/api/v1/projects/{project.Id}/review-gates/timeline");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var timeline = await response.Content.ReadFromJsonAsync<StudentReviewTimelineDto>();
        Assert.NotNull(timeline);
        // Gate 1 (weight 40/100) passed => AcademicProgress should be 40.00%, NOT 25.00% (1/4 gates)!
        Assert.Equal(40.00m, timeline.AcademicProgress);
        Assert.Equal("FROZEN_GATE_WEIGHTS", timeline.AcademicProgressSource);
    }

    // ==========================================
    // 14. Decide khi chỉ 1/2 reviewer đã quyết => gate chưa PASSED (:348 đòi tất cả)
    // ==========================================
    [Fact]
    public async Task Test14_Decide_When_Only_One_Of_Two_Reviewers_Decided_Gate_Not_Passed()
    {
        var s = fixture.Scenario;
        await using var db = fixture.CreateContext();

        var (project, gate, _) = await CreateProjectWithGate(db, s);

        var assign1 = new GateReviewerAssignment { GateInstanceId = gate.Id, ReviewerId = s.Lecturer1Id, Scope = "COMMON", Status = "PENDING_REVIEW", ConcurrencyToken = Guid.NewGuid(), CreatedAt = DateTime.UtcNow };
        var assign2 = new GateReviewerAssignment { GateInstanceId = gate.Id, ReviewerId = s.Lecturer2Id, Scope = "COMMON", Status = "PENDING_REVIEW", ConcurrencyToken = Guid.NewGuid(), CreatedAt = DateTime.UtcNow };
        db.GateReviewerAssignments.AddRange(assign1, assign2);
        await db.SaveChangesAsync();

        using var app = new TestFactory(fixture);
        using var reviewer1 = app.CreateAuthenticatedClient(s.Lecturer1Id, roles: [AppRoles.Lecturer]);

        // Reviewer 1 decides PASSED
        var decisionReq = new SubmitReviewGateDecisionRequest("PASSED", "Good progress", assign1.ConcurrencyToken.ToString("N"));
        var response = await reviewer1.PostAsJsonAsync($"/api/v1/review-gates/assignments/{assign1.Id}/decision", decisionReq);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Verify gate status in DB is NOT PASSED (remains IN_REVIEW)
        await using var verifyDb = fixture.CreateContext();
        var refreshedGate = await verifyDb.ProjectGateInstances.FindAsync(gate.Id);
        Assert.NotNull(refreshedGate);
        Assert.NotEqual("PASSED", refreshedGate.Status);
        Assert.Equal("IN_REVIEW", refreshedGate.Status);
    }

    // ==========================================
    // 15. Decide khi cả 2 reviewer PASSED => gate PASSED, OpenNextGate mở đúng một gate kế (:691)
    // ==========================================
    [Fact]
    public async Task Test15_Decide_When_Both_Reviewers_Passed_Gate_Passes_And_Opens_Exactly_Next_Gate()
    {
        var s = fixture.Scenario;
        await using var db = fixture.CreateContext();

        var (project, gate1, _) = await CreateProjectWithGate(db, s);

        var gate2 = new ProjectGateInstance { ProjectId = project.Id, TemplateId = s.DefaultTemplateId, TemplateVersion = 1, Code = "G2", Sequence = 2, Title = "Gate 2", Status = "LOCKED", ReviewEligibility = "BLOCKED", Required = true, BlockNextGate = true, BlocksDefense = false, Weight = 50m, CreatedAt = DateTime.UtcNow };
        var gate3 = new ProjectGateInstance { ProjectId = project.Id, TemplateId = s.DefaultTemplateId, TemplateVersion = 1, Code = "G3", Sequence = 3, Title = "Gate 3", Status = "LOCKED", ReviewEligibility = "BLOCKED", Required = true, BlockNextGate = true, BlocksDefense = true, Weight = 0m, CreatedAt = DateTime.UtcNow };
        db.ProjectGateInstances.AddRange(gate2, gate3);

        var assign1 = new GateReviewerAssignment { GateInstanceId = gate1.Id, ReviewerId = s.Lecturer1Id, Scope = "COMMON", Status = "PENDING_REVIEW", ConcurrencyToken = Guid.NewGuid(), CreatedAt = DateTime.UtcNow };
        var assign2 = new GateReviewerAssignment { GateInstanceId = gate1.Id, ReviewerId = s.Lecturer2Id, Scope = "COMMON", Status = "PENDING_REVIEW", ConcurrencyToken = Guid.NewGuid(), CreatedAt = DateTime.UtcNow };
        db.GateReviewerAssignments.AddRange(assign1, assign2);
        await db.SaveChangesAsync();

        using var app = new TestFactory(fixture);
        using var reviewer1 = app.CreateAuthenticatedClient(s.Lecturer1Id, roles: [AppRoles.Lecturer]);
        using var reviewer2 = app.CreateAuthenticatedClient(s.Lecturer2Id, roles: [AppRoles.Lecturer]);

        // Reviewer 1 decides PASSED
        var res1 = await reviewer1.PostAsJsonAsync($"/api/v1/review-gates/assignments/{assign1.Id}/decision", new SubmitReviewGateDecisionRequest("PASSED", "Pass 1", assign1.ConcurrencyToken.ToString("N")));
        Assert.Equal(HttpStatusCode.OK, res1.StatusCode);

        // Reviewer 2 decides PASSED
        var res2 = await reviewer2.PostAsJsonAsync($"/api/v1/review-gates/assignments/{assign2.Id}/decision", new SubmitReviewGateDecisionRequest("PASSED", "Pass 2", assign2.ConcurrencyToken.ToString("N")));
        Assert.Equal(HttpStatusCode.OK, res2.StatusCode);

        // Check DB: Gate 1 is PASSED, Gate 2 is opened (PLANNED/READY), Gate 3 remains LOCKED
        await using var verifyDb = fixture.CreateContext();
        var g1 = await verifyDb.ProjectGateInstances.FindAsync(gate1.Id);
        var g2 = await verifyDb.ProjectGateInstances.FindAsync(gate2.Id);
        var g3 = await verifyDb.ProjectGateInstances.FindAsync(gate3.Id);

        Assert.Equal("PASSED", g1!.Status);
        Assert.Equal("PLANNED", g2!.Status);
        Assert.Equal("READY", g2.ReviewEligibility);
        Assert.Equal("LOCKED", g3!.Status);
        Assert.Equal("BLOCKED", g3.ReviewEligibility);
    }


    // ==========================================
    // 16. Decide với reviewer đã REVOKED => 403 hoặc 409 (:627-630)
    // TODO(GATE-02): RequireActiveAssignment throws ConflictException (409 REVIEW_ASSIGNMENT_INACTIVE) rather than 403
    // ==========================================
    [Fact]
    public async Task Test16_Decide_With_Revoked_Assignment_Fails()
    {
        var s = fixture.Scenario;
        await using var db = fixture.CreateContext();

        var (project, gate, _) = await CreateProjectWithGate(db, s);

        var revokedAssign = new GateReviewerAssignment
        {
            GateInstanceId = gate.Id,
            ReviewerId = s.Lecturer1Id,
            Scope = "COMMON",
            Status = "REVOKED",
            RevokedAt = DateTime.UtcNow,
            RevokedBy = s.StaffId,
            RevocationReason = "Replaced",
            ConcurrencyToken = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow
        };
        db.GateReviewerAssignments.Add(revokedAssign);
        await db.SaveChangesAsync();

        using var app = new TestFactory(fixture);
        using var reviewer = app.CreateAuthenticatedClient(s.Lecturer1Id, roles: [AppRoles.Lecturer]);

        var response = await reviewer.PostAsJsonAsync($"/api/v1/review-gates/assignments/{revokedAssign.Id}/decision",
            new SubmitReviewGateDecisionRequest("PASSED", "Trying to decide revoked", revokedAssign.ConcurrencyToken.ToString("N")));

        Assert.True(response.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.Forbidden);
    }

    // ==========================================
    // 17. Decide bởi Primary supervisor của chính project đó => 403 (:636-637 COI)
    // ==========================================
    [Fact]
    public async Task Test17_Decide_By_Primary_Supervisor_Of_Project_Fails_403_Conflict_Of_Interest()
    {
        var s = fixture.Scenario;
        await using var db = fixture.CreateContext();

        var (project, gate, studentId) = await CreateProjectWithGate(db, s);

        // Lecturer1 is active Primary Supervisor of this project
        var supReq1 = new M.SupervisorRequest
        {
            ProjectId = project.Id,
            SupervisorProfileId = s.Lecturer1ProfileId,
            RequestedBy = studentId,
            Status = "ACCEPTED",
            AssignmentType = "PRIMARY",
            RequestedAt = DateTime.UtcNow,
            RespondedAt = DateTime.UtcNow
        };
        db.SupervisorRequests.Add(supReq1);
        await db.SaveChangesAsync();

        db.SupervisorAssignments.Add(new M.SupervisorAssignment
        {
            ProjectId = project.Id,
            SupervisorProfileId = s.Lecturer1ProfileId,
            SupervisorRequestId = supReq1.Id,
            IsPrimary = true,
            AssignmentType = "PRIMARY",
            AssignedAt = DateTime.UtcNow,
            EndedAt = null
        });

        var assign = new GateReviewerAssignment
        {
            GateInstanceId = gate.Id,
            ReviewerId = s.Lecturer1Id,
            Scope = "COMMON",
            Status = "PENDING_REVIEW",
            ConcurrencyToken = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow
        };
        db.GateReviewerAssignments.Add(assign);
        await db.SaveChangesAsync();

        using var app = new TestFactory(fixture);
        using var reviewer = app.CreateAuthenticatedClient(s.Lecturer1Id, roles: [AppRoles.Lecturer]);

        var response = await reviewer.PostAsJsonAsync($"/api/v1/review-gates/assignments/{assign.Id}/decision",
            new SubmitReviewGateDecisionRequest("PASSED", "COI Decision", assign.ConcurrencyToken.ToString("N")));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ==========================================
    // 18. Decide bởi Mentor ngành của chính project đó => 403 (:636-637)
    // ==========================================
    [Fact]
    public async Task Test18_Decide_By_Mentor_Of_Project_Fails_403_Conflict_Of_Interest()
    {
        var s = fixture.Scenario;
        await using var db = fixture.CreateContext();

        var (project, gate, studentId) = await CreateProjectWithGate(db, s);

        // Lecturer2 is active Discipline Mentor of this project
        var supReq2 = new M.SupervisorRequest
        {
            ProjectId = project.Id,
            SupervisorProfileId = s.Lecturer2ProfileId,
            RequestedBy = studentId,
            Status = "ACCEPTED",
            AssignmentType = "DISCIPLINE_MENTOR",
            MajorId = s.SeMajorId,
            RequestedAt = DateTime.UtcNow,
            RespondedAt = DateTime.UtcNow
        };
        db.SupervisorRequests.Add(supReq2);
        await db.SaveChangesAsync();

        db.SupervisorAssignments.Add(new M.SupervisorAssignment
        {
            ProjectId = project.Id,
            SupervisorProfileId = s.Lecturer2ProfileId,
            SupervisorRequestId = supReq2.Id,
            IsPrimary = false,
            AssignmentType = "DISCIPLINE_MENTOR",
            MajorId = s.SeMajorId,
            AssignedAt = DateTime.UtcNow,
            EndedAt = null
        });

        var assign = new GateReviewerAssignment
        {
            GateInstanceId = gate.Id,
            ReviewerId = s.Lecturer2Id,
            Scope = "COMMON",
            Status = "PENDING_REVIEW",
            ConcurrencyToken = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow
        };
        db.GateReviewerAssignments.Add(assign);
        await db.SaveChangesAsync();

        using var app = new TestFactory(fixture);
        using var reviewer = app.CreateAuthenticatedClient(s.Lecturer2Id, roles: [AppRoles.Lecturer]);

        var response = await reviewer.PostAsJsonAsync($"/api/v1/review-gates/assignments/{assign.Id}/decision",
            new SubmitReviewGateDecisionRequest("PASSED", "COI Mentor Decision", assign.ConcurrencyToken.ToString("N")));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ==========================================
    // 19. VerifyEvidence cho requirement mà assignment không phủ => 403 (Covers, :324)
    // ==========================================
    [Fact]
    public async Task Test19_VerifyEvidence_When_Assignment_Does_Not_Cover_Requirement_Fails_403()
    {
        var s = fixture.Scenario;
        await using var db = fixture.CreateContext();

        var (project, gate, studentId) = await CreateProjectWithGate(db, s);

        // Requirement is scoped to SE Major
        var reqSe = new ProjectGateRequirement
        {
            GateInstanceId = gate.Id,
            Code = "R-SE",
            Title = "SE Report",
            Type = "DOCUMENT",
            Scope = "MAJOR_SPECIFIC",
            MajorId = s.SeMajorId,
            Required = true,
            VerificationStatus = "PENDING"
        };
        gate.Requirements.Add(reqSe);
        await db.SaveChangesAsync();

        // Add submitted evidence for reqSe
        reqSe.Evidence.Add(new GateEvidence
        {
            ProjectId = project.Id,
            Version = 1,
            ArtifactName = "report.pdf",
            StorageKey = "dummy/path",
            ContentType = "application/pdf",
            SubmittedBy = studentId,
            SubmittedAt = DateTime.UtcNow,
            Status = "SUBMITTED"
        });
        await db.SaveChangesAsync();

        // Assignment is scoped to MKT Major (different major!)
        var assignMkt = new GateReviewerAssignment
        {
            GateInstanceId = gate.Id,
            ReviewerId = s.Lecturer1Id,
            Scope = "MAJOR_SPECIFIC",
            MajorId = s.MktMajorId, // does NOT cover reqSe!
            Status = "PENDING_REVIEW",
            ConcurrencyToken = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow
        };
        db.GateReviewerAssignments.Add(assignMkt);
        await db.SaveChangesAsync();

        using var app = new TestFactory(fixture);
        using var reviewer = app.CreateAuthenticatedClient(s.Lecturer1Id, roles: [AppRoles.Lecturer]);

        var verifyReq = new VerifyReviewGateEvidenceRequest(reqSe.Id.ToString(), "ACCEPTED", "Looks good", assignMkt.ConcurrencyToken.ToString("N"));
        var response = await reviewer.PostAsJsonAsync($"/api/v1/review-gates/assignments/{assignMkt.Id}/evidence/{reqSe.Id}/verify", verifyReq);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ==========================================
    // 20. DefenseReadiness khi thiếu quyết định phạm vi liên ngành => chưa sẵn sàng (:517-541, decided >= majorCount + 1)
    // ==========================================
    [Fact]
    public async Task Test20_DefenseReadiness_When_Missing_Interdisciplinary_Scope_Decisions_Is_Not_Ready()
    {
        var s = fixture.Scenario;
        await using var db = fixture.CreateContext();
        var studentId = await CreateStudentAsync(db, s);

        // Interdisciplinary project (2 majors: SE and MKT)
        var team = new M.Team
        {
            Code = "TEAM-DEFENSE-" + Guid.NewGuid().ToString("N")[..6],
            Name = "Team Defense",
            Status = "LOCKED",
            CreatedBy = studentId,
            AcademicSemesterId = s.SemesterId
        };
        team.TeamMembers.Add(new M.TeamMember { UserId = studentId, AcademicSemesterId = s.SemesterId, IsLeader = true, JoinedAt = DateTime.UtcNow });

        var project = new M.Project
        {
            Code = "PRJ-DEFENSE-" + Guid.NewGuid().ToString("N")[..6],
            Title = "Defense Readiness Project",
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
        await AcademicSnapshotFixture.AddAsync(db, project.Id, s.PeriodId, s.ItDeptId, studentId, DateTime.UtcNow);

        // Add 1 passed gate that blocks defense
        db.ProjectGateInstances.Add(new ProjectGateInstance
        {
            ProjectId = project.Id,
            TemplateId = s.DefaultTemplateId,
            TemplateVersion = 1,
            Code = "G1",
            Sequence = 1,
            Title = "Gate 1",
            Status = "PASSED",
            ReviewEligibility = "READY",
            Required = true,
            BlockNextGate = false,
            BlocksDefense = true,
            Weight = 100m,
            CreatedAt = DateTime.UtcNow
        });

        var deliverable = new M.Deliverable
        {
            ProjectId = project.Id,
            Title = "Final Deliverable",
            Status = "SUBMITTED",
            CreatedBy = studentId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.Deliverables.Add(deliverable);
        await db.SaveChangesAsync();

        var deliverableVersion = new M.DeliverableVersion
        {
            DeliverableId = deliverable.Id,
            VersionNumber = 1,
            SubmittedBy = studentId,
            Status = "SUBMITTED",
            SubmittedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.DeliverableVersions.Add(deliverableVersion);
        await db.SaveChangesAsync();

        // Locked final package
        var sub = new FinalSubmission
        {
            ProjectId = project.Id,
            ProjectPeriodId = s.PeriodId,
            SubmittedBy = studentId,
            SubmittedAt = DateTime.UtcNow,
            Deadline = DateTime.UtcNow.AddDays(7),
            DraftConcurrencyToken = Guid.NewGuid(),
            RequirementsConcurrencyToken = Guid.NewGuid(),
            Items =
            [
                new FinalSubmissionItem
                {
                    DeliverableId = deliverable.Id,
                    DeliverableVersionId = deliverableVersion.Id,
                    Title = "doc.pdf",
                    VersionNumber = 1,
                    StatusAtSubmission = "SUBMITTED",
                    WasRequired = true,
                    FilesJson = "[{\"name\":\"doc.pdf\",\"url\":\"dummy\"}]"
                }
            ]
        };
        db.Set<FinalSubmission>().Add(sub);

        // Notice: majorCount = 2, so majorCount + 1 = 3 scope decisions are required for INTERDISCIPLINARY_SCOPE.
        // We only add 1 passed decision (COMMON), so decided = 1 < 3!
        db.ProjectGateScopeDecisions.Add(new ProjectGateScopeDecision
        {
            ProjectId = project.Id,
            Scope = "COMMON",
            Status = "PASSED",
            DecidedBy = s.StaffId,
            DecidedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        using var app = new TestFactory(fixture);
        using var student = app.CreateAuthenticatedClient(studentId, roles: [AppRoles.Student]);

        var response = await student.GetAsync($"/api/v1/projects/{project.Id}/defense-readiness");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var readiness = await response.Content.ReadFromJsonAsync<DefenseReadinessDto>();
        Assert.NotNull(readiness);
        Assert.False(readiness.IsReady);

        var scopeCondition = readiness.Conditions.FirstOrDefault(c => c.Id == "INTERDISCIPLINARY_SCOPE");
        Assert.NotNull(scopeCondition);
        Assert.False(scopeCondition.Satisfied);
    }


    [Fact]
    public async Task Evidence_archive_streams_latest_link_and_rejects_empty_or_out_of_scope_gate()
    {
        var s = fixture.Scenario;
        await using var db = fixture.CreateContext();
        var (project, gate, studentId) = await CreateProjectWithGate(db, s);
        await AcademicSnapshotFixture.AddAsync(db, project.Id, s.PeriodId, s.ItDeptId, studentId, DateTime.UtcNow);
        using var app = new TestFactory(fixture);
        using var staff = app.CreateAuthenticatedClient(s.StaffId, roles: [AppRoles.DepartmentStaff]);
        using var outsider = app.CreateAuthenticatedClient(s.OutsideStaffId, roles: [AppRoles.DepartmentStaff]);
        var url = $"/api/v1/projects/{project.Id}/review-gates/{gate.Code}/evidence/archive";
        Assert.Equal(HttpStatusCode.NotFound, (await staff.GetAsync(url)).StatusCode);
        var requirement = Assert.Single(gate.Requirements);
        requirement.Evidence.Add(new GateEvidence { ProjectId = project.Id, Version = 1,
            ArtifactName = "old.txt", LinkUrl = "https://example.org/old", SubmittedBy = studentId, SubmittedAt = DateTime.UtcNow });
        requirement.Evidence.Add(new GateEvidence { ProjectId = project.Id, Version = 2,
            ArtifactName = "latest.txt", LinkUrl = "https://example.org/latest", SubmittedBy = studentId, SubmittedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await outsider.GetAsync(url)).StatusCode);
        var response = await staff.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/zip", response.Content.Headers.ContentType?.MediaType);
        using var bytes = new MemoryStream(await response.Content.ReadAsByteArrayAsync());
        using var zip = new ZipArchive(bytes, ZipArchiveMode.Read);
        var entry = Assert.Single(zip.Entries);
        Assert.EndsWith("lien-ket-minh-chung.txt", entry.FullName);
        using var reader = new StreamReader(entry.Open());
        Assert.Equal("https://example.org/latest", (await reader.ReadToEndAsync()).Trim());
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

    private static async Task<(M.Project Project, ProjectGateInstance Gate, long StudentId)> CreateProjectWithGate(AipmsDbContext db, BehaviorScenario s)
    {
        var studentId = await CreateStudentAsync(db, s);
        var team = new M.Team
        {
            Code = "TEAM-BHV-" + Guid.NewGuid().ToString("N")[..6],
            Name = "Team Behavior",
            Status = "LOCKED",
            CreatedBy = studentId,
            AcademicSemesterId = s.SemesterId
        };
        team.TeamMembers.Add(new M.TeamMember { UserId = studentId, AcademicSemesterId = s.SemesterId, IsLeader = true, JoinedAt = DateTime.UtcNow });

        var project = new M.Project
        {
            Code = "PRJ-BHV-" + Guid.NewGuid().ToString("N")[..6],
            Title = "Behavior Project",
            Status = "ACTIVE",
            CreatedBy = studentId,
            Team = team
        };
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        db.ProjectMajors.Add(new M.ProjectMajor { ProjectId = project.Id, MajorId = s.SeMajorId });
        db.Set<ProjectRegistrationSnapshot>().Add(new ProjectRegistrationSnapshot
        {
            ProjectId = project.Id,
            ProjectPeriodId = s.PeriodId,
            LeadDepartmentId = s.ItDeptId,
            SubmittedBy = studentId,
            SubmittedAt = DateTime.UtcNow,
            SnapshotJson = "{}"
        });

        var gate = new ProjectGateInstance
        {
            ProjectId = project.Id,
            TemplateId = s.DefaultTemplateId,
            TemplateVersion = 1,
            Code = "G1",
            Sequence = 1,
            Title = "Gate 1",
            Status = "PLANNED",
            ReviewEligibility = "READY",
            Required = true,
            BlockNextGate = true,
            BlocksDefense = false,
            Weight = 100m,
            CreatedAt = DateTime.UtcNow,
            Requirements =
            [
                new ProjectGateRequirement
                {
                    Code = "R1",
                    Title = "Doc 1",
                    Type = "DOCUMENT",
                    Scope = "COMMON",
                    Required = true,
                    VerificationStatus = "ACCEPTED"
                }
            ]
        };
        db.ProjectGateInstances.Add(gate);
        await db.SaveChangesAsync();

        return (project, gate, studentId);
    }
}
