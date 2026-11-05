using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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

public sealed class ReviewGateTemplateAuthorityFixture : IAsyncLifetime
{
    private readonly SupervisorDatabaseFixture database = new();
    public string ConnectionString => database.ConnectionString;
    public AipmsDbContext CreateContext() => database.CreateContext();
    public AuthorityScenario Scenario { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await database.InitializeAsync();
        Scenario = await SeedBaseAsync();
    }

    public Task DisposeAsync() => database.DisposeAsync();

    private async Task<AuthorityScenario> SeedBaseAsync()
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
            Name = "Academic Semester",
            Status = "ACTIVE",
            StartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-10)),
            EndDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(90))
        };

        var rubricIt = new M.Rubric { Name = "IT Rubric", Code = "R-IT-" + Guid.NewGuid().ToString("N")[..8], DepartmentId = itDept.Id, AcademicSemester = semester, CreatedBy = users.Admin, IsActive = true };
        var rubricBus = new M.Rubric { Name = "BUS Rubric", Code = "R-BUS-" + Guid.NewGuid().ToString("N")[..8], DepartmentId = busDept.Id, AcademicSemester = semester, CreatedBy = users.Admin, IsActive = true };

        var periodIt = new M.ProjectPeriod
        {
            AcademicSemester = semester,
            Rubric = rubricIt,
            Code = "PER-IT-" + Guid.NewGuid().ToString("N")[..8],
            Name = "IT Capstone Period",
            PeriodType = "REGISTRATION",
            Status = "ACTIVE",
            StartAt = DateTime.UtcNow.AddDays(-1),
            EndAt = DateTime.UtcNow.AddDays(30)
        };

        var periodBus = new M.ProjectPeriod
        {
            AcademicSemester = semester,
            Rubric = rubricBus,
            Code = "PER-BUS-" + Guid.NewGuid().ToString("N")[..8],
            Name = "Marketing Capstone Period",
            PeriodType = "REGISTRATION",
            Status = "ACTIVE",
            StartAt = DateTime.UtcNow.AddDays(-1),
            EndAt = DateTime.UtcNow.AddDays(30)
        };

        var periodInter = new M.ProjectPeriod
        {
            AcademicSemester = semester,
            Rubric = rubricIt, // Lead department is IT
            Code = "PER-INTER-" + Guid.NewGuid().ToString("N")[..8],
            Name = "Interdisciplinary Period",
            PeriodType = "REGISTRATION",
            Status = "ACTIVE",
            StartAt = DateTime.UtcNow.AddDays(-1),
            EndAt = DateTime.UtcNow.AddDays(30)
        };

        db.AddRange(majorSe, majorMkt, semester, rubricIt, rubricBus, periodIt, periodBus, periodInter);
        await db.SaveChangesAsync();

        // Topic with LeadDepartmentId for periodInter
        db.Set<ProjectTopic>().Add(new ProjectTopic
        {
            ProjectPeriodId = periodInter.Id,
            LeadDepartmentId = itDept.Id,
            ProjectMode = "INTERDISCIPLINARY",
            PrimaryMajorId = null,
            Code = "TOPIC-INTER-" + Guid.NewGuid().ToString("N")[..8],
            Title = "Interdisciplinary E-Commerce",
            Status = "DRAFT",
            CreatedBy = users.Staff,
            UpdatedBy = users.Staff,
            ConcurrencyToken = Guid.NewGuid()
        });
        await db.SaveChangesAsync();

        return new AuthorityScenario(
            users.Admin,
            users.Staff,
            users.OutsideStaff,
            itDept.Id,
            busDept.Id,
            majorSe.Id,
            majorMkt.Id,
            periodIt.Id,
            periodBus.Id,
            periodInter.Id);
    }

    public async Task<(long Id, Guid ConcurrencyToken)> CreateDraftTemplateAsync(string mode, string major, long periodId, long createdBy)
    {
        await using var db = CreateContext();
        var template = new ReviewGateTemplate
        {
            ProjectPeriodId = periodId,
            Code = "T-" + Guid.NewGuid().ToString("N")[..8],
            Name = $"{major} Template",
            Mode = mode,
            Major = major,
            Status = "DRAFT",
            Version = 1,
            ConcurrencyToken = Guid.NewGuid(),
            CreatedBy = createdBy,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Gates =
            [
                new ReviewGateDefinition
                {
                    Code = "G1", Sequence = 1, Title = "Gate 1", Objective = "Check 1", Required = true,
                    BlockNextGate = true, BlocksDefense = false, Weight = 100m, ReviewerPolicy = "ASSIGNED_REVIEWER", ReviewScope = "COMMON",
                    Requirements =
                    [
                        new ReviewGateRequirement { Code = "R1", Title = "Doc 1", Type = "DOCUMENT", Scope = "COMMON", Verification = "HUMAN", Required = true }
                    ]
                }
            ]
        };
        db.ReviewGateTemplates.Add(template);
        await db.SaveChangesAsync();
        return (template.Id, template.ConcurrencyToken);
    }
}

public sealed record AuthorityScenario(
    long AdminId,
    long ItStaffId,
    long BusStaffId,
    long ItDeptId,
    long BusDeptId,
    long SeMajorId,
    long MktMajorId,
    long PeriodItId,
    long PeriodBusId,
    long PeriodInterId);

public sealed class ReviewGateTemplateAuthorityTests(ReviewGateTemplateAuthorityFixture fixture) : IClassFixture<ReviewGateTemplateAuthorityFixture>
{
    private sealed class TestFactory(ReviewGateTemplateAuthorityFixture db) : AipmsWebApplicationFactory
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

    private static readonly List<ReviewGateDefinitionInput> Gates =
    [
        new(
            "G1",
            1,
            "Initial Gate",
            "Initial objective",
            true,
            true,
            false,
            DateTime.UtcNow.AddDays(1),
            DateTime.UtcNow.AddDays(5),
            "ASSIGNED_REVIEWER",
            "COMMON",
            [
                new("R1", "Report Doc", "Initial report", "DOCUMENT", "COMMON", null, null, "HUMAN", true)
            ],
            100m)
    ];

    [Fact]
    public async Task Staff_marketing_cannot_save_template_for_se_major_returns_403_scope_forbidden()
    {
        var s = fixture.Scenario;
        using var app = new TestFactory(fixture);
        using var busStaff = app.CreateAuthenticatedClient(s.BusStaffId, roles: [AppRoles.DepartmentStaff]);

        var request = new SaveReviewGateTemplateRequest(
            s.PeriodItId,
            "T-SE-NEW-" + Guid.NewGuid().ToString("N")[..6],
            "New SE Template",
            "SINGLE_MAJOR",
            "SE",
            "1.0",
            null,
            Gates);

        var response = await busStaff.PostAsJsonAsync("/api/v1/academic/gate-templates", request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var doc = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(doc.TryGetProperty("code", out var code) && code.GetString() == "TEMPLATE_SCOPE_FORBIDDEN");
    }

    [Fact]
    public async Task Staff_marketing_cannot_publish_template_for_se_major_returns_403_scope_forbidden()
    {
        var s = fixture.Scenario;
        var (templateId, token) = await fixture.CreateDraftTemplateAsync("SINGLE_MAJOR", "SE", s.PeriodItId, s.ItStaffId);

        using var app = new TestFactory(fixture);
        using var busStaff = app.CreateAuthenticatedClient(s.BusStaffId, roles: [AppRoles.DepartmentStaff]);

        var publishRequest = new PublishReviewGateTemplateRequest(token.ToString("N"));
        var response = await busStaff.PostAsJsonAsync($"/api/v1/academic/gate-templates/{templateId}/publish", publishRequest);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var doc = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(doc.TryGetProperty("code", out var code) && code.GetString() == "TEMPLATE_SCOPE_FORBIDDEN");
    }

    [Fact]
    public async Task Staff_marketing_can_save_template_for_mkt_major_in_own_period_returns_200()
    {
        var s = fixture.Scenario;
        using var app = new TestFactory(fixture);
        using var busStaff = app.CreateAuthenticatedClient(s.BusStaffId, roles: [AppRoles.DepartmentStaff]);

        var request = new SaveReviewGateTemplateRequest(
            s.PeriodBusId,
            "T-MKT-NEW-" + Guid.NewGuid().ToString("N")[..6],
            "New MKT Template",
            "SINGLE_MAJOR",
            "MKT",
            "1.0",
            null,
            Gates);

        var response = await busStaff.PostAsJsonAsync("/api/v1/academic/gate-templates", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var dto = await response.Content.ReadFromJsonAsync<ReviewGateTemplateDto>();
        Assert.NotNull(dto);
        Assert.Equal("MKT", dto.Major);
        Assert.Equal("DRAFT", dto.Status);
    }

    [Fact]
    public async Task Admin_without_academic_staff_cannot_publish_template_returns_403_admin_not_academic()
    {
        var s = fixture.Scenario;
        var (templateId, token) = await fixture.CreateDraftTemplateAsync("SINGLE_MAJOR", "SE", s.PeriodItId, s.ItStaffId);

        using var app = new TestFactory(fixture);
        using var pureAdmin = app.CreateAuthenticatedClient(s.AdminId, roles: [AppRoles.Admin]);

        var publishRequest = new PublishReviewGateTemplateRequest(token.ToString("N"));
        var response = await pureAdmin.PostAsJsonAsync($"/api/v1/academic/gate-templates/{templateId}/publish", publishRequest);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var doc = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(doc.TryGetProperty("code", out var code) && code.GetString() == "TEMPLATE_AUTHORITY_ADMIN_NOT_ACADEMIC");
    }

    [Fact]
    public async Task Interdisciplinary_template_participating_department_staff_cannot_publish_returns_403_scope_forbidden()
    {
        var s = fixture.Scenario;
        var (templateId, token) = await fixture.CreateDraftTemplateAsync("INTERDISCIPLINARY", "SE+MKT", s.PeriodInterId, s.ItStaffId);

        using var app = new TestFactory(fixture);
        using var busStaff = app.CreateAuthenticatedClient(s.BusStaffId, roles: [AppRoles.DepartmentStaff]);

        var publishRequest = new PublishReviewGateTemplateRequest(token.ToString("N"));
        var response = await busStaff.PostAsJsonAsync($"/api/v1/academic/gate-templates/{templateId}/publish", publishRequest);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var doc = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(doc.TryGetProperty("code", out var code) && code.GetString() == "TEMPLATE_SCOPE_FORBIDDEN");
    }

    [Fact]
    public async Task Interdisciplinary_template_lead_department_staff_can_publish_returns_200()
    {
        var s = fixture.Scenario;
        var (templateId, token) = await fixture.CreateDraftTemplateAsync("INTERDISCIPLINARY", "SE+MKT", s.PeriodInterId, s.ItStaffId);

        using var app = new TestFactory(fixture);
        using var itStaff = app.CreateAuthenticatedClient(s.ItStaffId, roles: [AppRoles.DepartmentStaff]);

        var publishRequest = new PublishReviewGateTemplateRequest(token.ToString("N"));
        var response = await itStaff.PostAsJsonAsync($"/api/v1/academic/gate-templates/{templateId}/publish", publishRequest);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var dto = await response.Content.ReadFromJsonAsync<ReviewGateTemplateDto>();
        Assert.NotNull(dto);
        Assert.Equal("PUBLISHED", dto.Status);
    }

    [Fact]
    public async Task Other_require_staff_endpoints_remain_accessible_to_staff()
    {
        var s = fixture.Scenario;
        using var app = new TestFactory(fixture);
        using var busStaff = app.CreateAuthenticatedClient(s.BusStaffId, roles: [AppRoles.DepartmentStaff]);

        var response = await busStaff.GetAsync("/api/v1/academic/gate-templates");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var templates = await response.Content.ReadFromJsonAsync<List<ReviewGateTemplateDto>>();
        Assert.NotNull(templates);
    }
}
