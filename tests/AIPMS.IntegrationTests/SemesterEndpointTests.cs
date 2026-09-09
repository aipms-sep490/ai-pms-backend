using System.Net;
using System.Net.Http.Json;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.Semesters.Abstractions;
using AIPMS.Application.Features.Semesters.DTOs;
using AIPMS.Application.Features.Semesters.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AIPMS.IntegrationTests;

public sealed class SemesterEndpointTests
    : IClassFixture<SemesterEndpointTests.SemesterWebApplicationFactory>
{
    private static readonly DateTime Ts = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
    private readonly SemesterWebApplicationFactory _factory;

    public SemesterEndpointTests(SemesterWebApplicationFactory factory)
    {
        _factory = factory;
    }

    // ── Auth ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetSemesters_Anonymous_ReturnsUnauthorized()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/academic/semesters");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetSemesters_AuthenticatedStudent_ReturnsPagedResult()
    {
        using var client = _factory.CreateAuthenticatedClient(4001, roles: AppRoles.Student);

        var response = await client.GetAsync("/api/v1/academic/semesters?page=1&pageSize=20");
        var result = await response.Content.ReadFromJsonAsync<PagedResult<SemesterDto>>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        Assert.NotEmpty(result.Items);
        Assert.Contains(result.Items, s => s.Code == "SEM-2026");
    }

    [Fact]
    public async Task GetSemesterById_Exists_ReturnsSemester()
    {
        using var client = _factory.CreateAuthenticatedClient(4001, roles: AppRoles.Student);

        var response = await client.GetAsync("/api/v1/academic/semesters/1");
        var result = await response.Content.ReadFromJsonAsync<SemesterDto>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal(1, result.Id);
        Assert.Equal("SEM-2026", result.Code);
    }

    [Fact]
    public async Task GetSemesterById_NotFound_Returns404()
    {
        using var client = _factory.CreateAuthenticatedClient(4001, roles: AppRoles.Student);

        var response = await client.GetAsync("/api/v1/academic/semesters/9999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── Create Semester ───────────────────────────────────────────────────────

    [Fact]
    public async Task CreateSemester_Admin_ReturnsCreated()
    {
        using var client = _factory.CreateAuthenticatedClient(1001, roles: AppRoles.Admin);

        var response = await client.PostAsJsonAsync(
            "/api/v1/academic/semesters",
            new CreateSemesterRequest(
                1, "SEM-2027", "Spring 2027",
                new DateOnly(2027, 1, 1), new DateOnly(2027, 6, 30)));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);

        var result = await response.Content.ReadFromJsonAsync<SemesterDto>();
        Assert.NotNull(result);
        Assert.Equal("SEM-2027", result.Code);
        Assert.Equal("DRAFT", result.Status);
    }

    [Fact]
    public async Task CreateSemester_DepartmentStaff_ReturnsForbidden()
    {
        using var client = _factory.CreateAuthenticatedClient(
            3001, roles: AppRoles.DepartmentStaff);

        var response = await client.PostAsJsonAsync(
            "/api/v1/academic/semesters",
            new CreateSemesterRequest(
                1, "SEM-X", "Blocked",
                new DateOnly(2027, 1, 1), new DateOnly(2027, 6, 30)));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CreateSemester_InvalidDates_ReturnsBadRequest()
    {
        using var client = _factory.CreateAuthenticatedClient(1001, roles: AppRoles.Admin);

        var response = await client.PostAsJsonAsync(
            "/api/v1/academic/semesters",
            new CreateSemesterRequest(
                1, "SEM-BAD", "Bad Dates",
                new DateOnly(2027, 6, 30), new DateOnly(2027, 1, 1)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateSemester_DuplicateCode_ReturnsConflict()
    {
        using var client = _factory.CreateAuthenticatedClient(1001, roles: AppRoles.Admin);

        var response = await client.PostAsJsonAsync(
            "/api/v1/academic/semesters",
            new CreateSemesterRequest(
                1, "SEM-DUP", "Duplicate",
                new DateOnly(2027, 1, 1), new DateOnly(2027, 6, 30)));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    // ── Update Semester ───────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateSemester_Admin_ReturnsOk()
    {
        using var client = _factory.CreateAuthenticatedClient(1001, roles: AppRoles.Admin);

        var response = await client.PutAsJsonAsync(
            "/api/v1/academic/semesters/1",
            new UpdateSemesterRequest(
                "SEM-2026", "Updated Name",
                new DateOnly(2026, 9, 1), new DateOnly(2027, 1, 31)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<SemesterDto>();
        Assert.NotNull(result);
        Assert.Equal("Updated Name", result.Name);
    }

    [Fact]
    public async Task UpdateSemester_Student_ReturnsForbidden()
    {
        using var client = _factory.CreateAuthenticatedClient(4001, roles: AppRoles.Student);

        var response = await client.PutAsJsonAsync(
            "/api/v1/academic/semesters/1",
            new UpdateSemesterRequest(
                "SEM-2026", "Blocked Update",
                new DateOnly(2026, 9, 1), new DateOnly(2027, 1, 31)));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── Set Status ────────────────────────────────────────────────────────────

    [Fact]
    public async Task SetSemesterStatus_ValidTransition_ReturnsOk()
    {
        using var client = _factory.CreateAuthenticatedClient(1001, roles: AppRoles.Admin);

        var response = await client.PatchAsJsonAsync(
            "/api/v1/academic/semesters/1/status",
            new SetSemesterStatusRequest("UPCOMING"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<SemesterDto>();
        Assert.NotNull(result);
        Assert.Equal("UPCOMING", result.Status);
    }

    [Fact]
    public async Task SetSemesterStatus_InvalidTransition_ReturnsConflict()
    {
        using var client = _factory.CreateAuthenticatedClient(1001, roles: AppRoles.Admin);

        // Semester 1 starts as DRAFT in factory; ARCHIVED is not valid from DRAFT → ACTIVE
        var response = await client.PatchAsJsonAsync(
            "/api/v1/academic/semesters/1/status",
            new SetSemesterStatusRequest("CLOSED"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task SetSemesterStatus_InvalidStatusValue_ReturnsBadRequest()
    {
        using var client = _factory.CreateAuthenticatedClient(1001, roles: AppRoles.Admin);

        var response = await client.PatchAsJsonAsync(
            "/api/v1/academic/semesters/1/status",
            new SetSemesterStatusRequest("OPEN"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── Project Periods ───────────────────────────────────────────────────────

    [Fact]
    public async Task GetProjectPeriods_Anonymous_ReturnsUnauthorized()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/academic/project-periods");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetProjectPeriods_AuthenticatedStudent_ReturnsPagedResult()
    {
        using var client = _factory.CreateAuthenticatedClient(4001, roles: AppRoles.Student);

        var response = await client.GetAsync(
            "/api/v1/academic/project-periods?semesterId=1&page=1&pageSize=20");
        var result = await response.Content.ReadFromJsonAsync<PagedResult<ProjectPeriodDto>>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        Assert.NotEmpty(result.Items);
        Assert.Contains(result.Items, p => p.Code == "REG-01");
    }

    [Fact]
    public async Task GetProjectPeriodById_Exists_ReturnsPeriod()
    {
        using var client = _factory.CreateAuthenticatedClient(4001, roles: AppRoles.Student);

        var response = await client.GetAsync("/api/v1/academic/project-periods/1");
        var result = await response.Content.ReadFromJsonAsync<ProjectPeriodDto>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal("REG-01", result.Code);
        Assert.Equal("REGISTRATION", result.PeriodType);
    }

    [Fact]
    public async Task CreateProjectPeriod_Admin_ReturnsCreated()
    {
        using var client = _factory.CreateAuthenticatedClient(1001, roles: AppRoles.Admin);

        var response = await client.PostAsJsonAsync(
            "/api/v1/academic/project-periods",
            new CreateProjectPeriodRequest(
                1, "EXE-01", "Execution Period",
                "EXECUTION",
                new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc)));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<ProjectPeriodDto>();
        Assert.NotNull(result);
        Assert.Equal("EXE-01", result.Code);
        Assert.Equal("EXECUTION", result.PeriodType);
        Assert.Equal("DRAFT", result.Status);
    }

    [Fact]
    public async Task CreateProjectPeriod_Student_ReturnsForbidden()
    {
        using var client = _factory.CreateAuthenticatedClient(4001, roles: AppRoles.Student);

        var response = await client.PostAsJsonAsync(
            "/api/v1/academic/project-periods",
            new CreateProjectPeriodRequest(
                1, "EXE-02", "Blocked",
                "EXECUTION",
                new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc)));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CreateProjectPeriod_InvalidPeriodType_ReturnsBadRequest()
    {
        using var client = _factory.CreateAuthenticatedClient(1001, roles: AppRoles.Admin);

        var response = await client.PostAsJsonAsync(
            "/api/v1/academic/project-periods",
            new CreateProjectPeriodRequest(
                1, "BAD-01", "Bad Period",
                "INVALID_TYPE",
                new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SetProjectPeriodStatus_ValidTransition_ReturnsOk()
    {
        using var client = _factory.CreateAuthenticatedClient(1001, roles: AppRoles.Admin);

        var response = await client.PatchAsJsonAsync(
            "/api/v1/academic/project-periods/1/status",
            new SetProjectPeriodStatusRequest("UPCOMING"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<ProjectPeriodDto>();
        Assert.NotNull(result);
        Assert.Equal("UPCOMING", result.Status);
    }

    // ── Factories and Test Doubles ────────────────────────────────────────────

    public sealed class SemesterWebApplicationFactory : AipmsWebApplicationFactory
    {
        public TestSemesterRepository Repository { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ISemesterRepository>();
                services.AddSingleton<ISemesterRepository>(Repository);
                services.RemoveAll<IAuditTrail>();
                services.AddSingleton<IAuditTrail, NoOpAuditTrail>();
            });
        }
    }

    public sealed class NoOpAuditTrail : IAuditTrail
    {
        public Task RecordAsync(
            AuditEntry entry,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    /// <summary>
    /// In-memory semester repository pre-loaded with fixture data for endpoint tests.
    /// </summary>
    public sealed class TestSemesterRepository : ISemesterRepository
    {
        private long _nextSemesterId = 10;
        private long _nextPeriodId = 10;

        private readonly Dictionary<long, AcademicSemesterModel> _semesters;
        private readonly Dictionary<long, ProjectPeriodModel> _periods;

        public TestSemesterRepository()
        {
            _semesters = new Dictionary<long, AcademicSemesterModel>
            {
                [1] = new(
                    1, 1, "FPTU", "FPT University",
                    "SEM-2026", "Academic Year 2026",
                    new DateOnly(2026, 9, 1), new DateOnly(2027, 1, 31),
                    "DRAFT", Ts, Ts)
            };

            _periods = new Dictionary<long, ProjectPeriodModel>
            {
                [1] = new(
                    1, 1, "SEM-2026", "Academic Year 2026",
                    "REG-01", "Registration Period",
                    "REGISTRATION",
                    new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
                    new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
                    "DRAFT", Ts, Ts)
            };
        }

        public Task<PagedResult<AcademicSemesterModel>> GetSemestersAsync(
            long? organizationId, string? search, string? status, int page, int pageSize,
            CancellationToken cancellationToken = default)
        {
            var items = _semesters.Values
                .Where(s =>
                    (!organizationId.HasValue || s.OrganizationId == organizationId.Value)
                    && (string.IsNullOrWhiteSpace(status) || s.Status == status)
                    && (string.IsNullOrWhiteSpace(search)
                        || s.Code.Contains(search, StringComparison.OrdinalIgnoreCase)
                        || s.Name.Contains(search, StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(static s => s.StartDate)
                .ToArray();

            return Task.FromResult(new PagedResult<AcademicSemesterModel>(
                items.Skip((page - 1) * pageSize).Take(pageSize).ToArray(),
                page, pageSize, items.Length));
        }

        public Task<AcademicSemesterModel?> GetSemesterAsync(
            long semesterId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_semesters.GetValueOrDefault(semesterId));

        public Task<bool> SemesterCodeExistsAsync(
            long organizationId, string code, long? excludedSemesterId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(code == "SEM-DUP");

        public Task<AcademicSemesterModel> CreateSemesterAsync(
            long organizationId, string code, string name,
            DateOnly startDate, DateOnly endDate, DateTime utcNow,
            CancellationToken cancellationToken = default)
        {
            var s = new AcademicSemesterModel(
                _nextSemesterId++, organizationId, "FPTU", "FPT University",
                code, name, startDate, endDate, "DRAFT", utcNow, utcNow);
            _semesters[s.Id] = s;
            return Task.FromResult(s);
        }

        public Task<AcademicSemesterModel> UpdateSemesterAsync(
            long semesterId, string code, string name,
            DateOnly startDate, DateOnly endDate, DateTime utcNow,
            CancellationToken cancellationToken = default)
        {
            var existing = _semesters[semesterId];
            var updated = existing with
            {
                Code = code, Name = name, StartDate = startDate,
                EndDate = endDate, UpdatedAt = utcNow
            };
            _semesters[semesterId] = updated;
            return Task.FromResult(updated);
        }

        public Task<AcademicSemesterModel> SetSemesterStatusAsync(
            long semesterId, string status, DateTime utcNow,
            CancellationToken cancellationToken = default)
        {
            var existing = _semesters[semesterId];
            var updated = existing with { Status = status, UpdatedAt = utcNow };
            _semesters[semesterId] = updated;
            return Task.FromResult(updated);
        }

        public Task<PagedResult<ProjectPeriodModel>> GetProjectPeriodsAsync(
            long? semesterId, string? search, string? status, string? periodType,
            int page, int pageSize, CancellationToken cancellationToken = default)
        {
            var items = _periods.Values
                .Where(p =>
                    (!semesterId.HasValue || p.AcademicSemesterId == semesterId.Value)
                    && (string.IsNullOrWhiteSpace(status) || p.Status == status)
                    && (string.IsNullOrWhiteSpace(periodType) || p.PeriodType == periodType)
                    && (string.IsNullOrWhiteSpace(search)
                        || p.Code.Contains(search, StringComparison.OrdinalIgnoreCase)
                        || p.Name.Contains(search, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(static p => p.StartAt)
                .ToArray();

            return Task.FromResult(new PagedResult<ProjectPeriodModel>(
                items.Skip((page - 1) * pageSize).Take(pageSize).ToArray(),
                page, pageSize, items.Length));
        }

        public Task<ProjectPeriodModel?> GetProjectPeriodAsync(
            long periodId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_periods.GetValueOrDefault(periodId));

        public Task<bool> PeriodCodeExistsAsync(
            long semesterId, string code, long? excludedPeriodId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<ProjectPeriodModel> CreateProjectPeriodAsync(
            long semesterId, string code, string name, string periodType,
            DateTime startAt, DateTime endAt, DateTime utcNow,
            CancellationToken cancellationToken = default)
        {
            var semester = _semesters[semesterId];
            var p = new ProjectPeriodModel(
                _nextPeriodId++, semesterId, semester.Code, semester.Name,
                code, name, periodType, startAt, endAt, "DRAFT", utcNow, utcNow);
            _periods[p.Id] = p;
            return Task.FromResult(p);
        }

        public Task<ProjectPeriodModel> UpdateProjectPeriodAsync(
            long periodId, string code, string name, string periodType,
            DateTime startAt, DateTime endAt, DateTime utcNow,
            CancellationToken cancellationToken = default)
        {
            var existing = _periods[periodId];
            var updated = existing with
            {
                Code = code, Name = name, PeriodType = periodType,
                StartAt = startAt, EndAt = endAt, UpdatedAt = utcNow
            };
            _periods[periodId] = updated;
            return Task.FromResult(updated);
        }

        public Task<ProjectPeriodModel> SetProjectPeriodStatusAsync(
            long periodId, string status, DateTime utcNow,
            CancellationToken cancellationToken = default)
        {
            var existing = _periods[periodId];
            var updated = existing with { Status = status, UpdatedAt = utcNow };
            _periods[periodId] = updated;
            return Task.FromResult(updated);
        }
    }
}
