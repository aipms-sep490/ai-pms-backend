using System.Net;
using System.Net.Http.Json;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.Academic.Abstractions;
using AIPMS.Application.Features.Academic.DTOs;
using AIPMS.Application.Features.Academic.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AIPMS.IntegrationTests;

public sealed class AcademicStructureEndpointTests
    : IClassFixture<AcademicStructureEndpointTests.AcademicWebApplicationFactory>
{
    private readonly AcademicWebApplicationFactory _factory;

    public AcademicStructureEndpointTests(AcademicWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetOrganizations_Anonymous_ReturnsUnauthorized()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/academic/organizations");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetOrganizations_AuthenticatedStudent_ReturnsPagedResult()
    {
        using var client = _factory.CreateAuthenticatedClient(4001, roles: AppRoles.Student);

        var response = await client.GetAsync(
            "/api/v1/academic/organizations?page=1&pageSize=20");
        var result = await response.Content
            .ReadFromJsonAsync<PagedResult<OrganizationDto>>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        Assert.Single(result.Items);
        Assert.Equal("FPTU", result.Items[0].Code);
    }

    [Fact]
    public async Task CreateOrganization_DepartmentStaff_ReturnsForbidden()
    {
        using var client = _factory.CreateAuthenticatedClient(
            3001,
            roles: AppRoles.DepartmentStaff);

        var response = await client.PostAsJsonAsync(
            "/api/v1/academic/organizations",
            new CreateOrganizationRequest("OTHER", "Other University", null));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CreateOrganization_Admin_ReturnsCreated()
    {
        using var client = _factory.CreateAuthenticatedClient(
            1001,
            roles: AppRoles.Admin);

        var response = await client.PostAsJsonAsync(
            "/api/v1/academic/organizations",
            new CreateOrganizationRequest("DUT", "Da Nang University of Technology", null));
        var result = await response.Content.ReadFromJsonAsync<OrganizationDto>();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal("DUT", result.Code);
        Assert.NotNull(response.Headers.Location);
    }

    [Fact]
    public async Task CreateMajor_DepartmentStaffOwnScope_ReturnsCreated()
    {
        using var client = _factory.CreateAuthenticatedClient(
            3001,
            roles: AppRoles.DepartmentStaff);

        var response = await client.PostAsJsonAsync(
            "/api/v1/academic/majors",
            new CreateMajorRequest(10, "SE_AI", "Software Engineering AI", null));
        var result = await response.Content.ReadFromJsonAsync<MajorDto>();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal(10, result.DepartmentId);
        Assert.Equal("SE_AI", result.Code);
    }

    [Fact]
    public async Task CreateMajor_DepartmentStaffOutsideScope_ReturnsForbiddenProblemDetails()
    {
        using var client = _factory.CreateAuthenticatedClient(
            3001,
            roles: AppRoles.DepartmentStaff);

        var response = await client.PostAsJsonAsync(
            "/api/v1/academic/majors",
            new CreateMajorRequest(20, "FIN", "Finance", null));
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Equal("Access is forbidden.", problem.Title);
        Assert.True(problem.Extensions.ContainsKey("traceId"));
    }

    public sealed class AcademicWebApplicationFactory : AipmsWebApplicationFactory
    {
        public TestAcademicRepository Repository { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IAcademicStructureRepository>();
                services.AddSingleton<IAcademicStructureRepository>(Repository);
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

    public sealed class TestAcademicRepository : IAcademicStructureRepository
    {
        private static readonly DateTime Timestamp =
            new(2026, 8, 25, 0, 0, 0, DateTimeKind.Utc);

        private readonly Dictionary<long, AcademicOrganization> _organizations = new()
        {
            [1] = new(
                1,
                "FPTU",
                "FPT University",
                null,
                true,
                Timestamp,
                Timestamp)
        };

        private readonly Dictionary<long, AcademicDepartment> _departments = new()
        {
            [10] = new(
                10,
                1,
                "FPTU",
                "FPT University",
                "IT",
                "Information Technology",
                null,
                true,
                Timestamp,
                Timestamp),
            [20] = new(
                20,
                1,
                "FPTU",
                "FPT University",
                "BUS",
                "Business",
                null,
                true,
                Timestamp,
                Timestamp)
        };

        private readonly Dictionary<long, AcademicMajor> _majors = [];
        private long _nextOrganizationId = 100;
        private long _nextMajorId = 300;

        public Task<PagedResult<AcademicOrganization>> GetOrganizationsAsync(
            string? search,
            bool? isActive,
            int page,
            int pageSize,
            CancellationToken cancellationToken = default)
        {
            var items = _organizations.Values
                .Where(organization =>
                    (!isActive.HasValue || organization.IsActive == isActive.Value)
                    && (string.IsNullOrWhiteSpace(search)
                        || organization.Code.Contains(search, StringComparison.OrdinalIgnoreCase)
                        || organization.Name.Contains(search, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(static organization => organization.Code, StringComparer.Ordinal)
                .ToArray();

            return Task.FromResult(new PagedResult<AcademicOrganization>(
                items.Skip((page - 1) * pageSize).Take(pageSize).ToArray(),
                page,
                pageSize,
                items.Length));
        }

        public Task<AcademicOrganization?> GetOrganizationAsync(
            long organizationId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_organizations.GetValueOrDefault(organizationId));

        public Task<bool> OrganizationCodeOrNameExistsAsync(
            string code,
            string name,
            long? excludedOrganizationId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_organizations.Values.Any(organization =>
                organization.Id != excludedOrganizationId
                && (string.Equals(organization.Code, code, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(organization.Name, name, StringComparison.OrdinalIgnoreCase))));

        public Task<AcademicOrganization> CreateOrganizationAsync(
            string code,
            string name,
            string? description,
            DateTime utcNow,
            CancellationToken cancellationToken = default)
        {
            var organization = new AcademicOrganization(
                _nextOrganizationId++,
                code,
                name,
                description,
                true,
                utcNow,
                utcNow);
            _organizations[organization.Id] = organization;
            return Task.FromResult(organization);
        }

        public Task<AcademicOrganization> UpdateOrganizationAsync(
            long organizationId,
            string code,
            string name,
            string? description,
            DateTime utcNow,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AcademicOrganization> SetOrganizationActiveAsync(
            long organizationId,
            bool isActive,
            DateTime utcNow,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PagedResult<AcademicDepartment>> GetDepartmentsAsync(
            long? organizationId,
            string? search,
            bool? isActive,
            int page,
            int pageSize,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AcademicDepartment?> GetDepartmentAsync(
            long departmentId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_departments.GetValueOrDefault(departmentId));

        public Task<bool> DepartmentCodeOrNameExistsAsync(
            long organizationId,
            string code,
            string name,
            long? excludedDepartmentId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AcademicDepartment> CreateDepartmentAsync(
            long organizationId,
            string code,
            string name,
            string? description,
            DateTime utcNow,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AcademicDepartment> UpdateDepartmentAsync(
            long departmentId,
            string code,
            string name,
            string? description,
            DateTime utcNow,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AcademicDepartment> SetDepartmentActiveAsync(
            long departmentId,
            bool isActive,
            DateTime utcNow,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PagedResult<AcademicMajor>> GetMajorsAsync(
            long? organizationId,
            long? departmentId,
            string? search,
            bool? isActive,
            int page,
            int pageSize,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AcademicMajor?> GetMajorAsync(
            long majorId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_majors.GetValueOrDefault(majorId));

        public Task<bool> MajorCodeOrNameExistsAsync(
            long departmentId,
            string code,
            string name,
            long? excludedMajorId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_majors.Values.Any(major =>
                major.DepartmentId == departmentId
                && major.Id != excludedMajorId
                && (string.Equals(major.Code, code, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(major.Name, name, StringComparison.OrdinalIgnoreCase))));

        public Task<AcademicMajor> CreateMajorAsync(
            long departmentId,
            string code,
            string name,
            string? description,
            DateTime utcNow,
            CancellationToken cancellationToken = default)
        {
            var department = _departments[departmentId];
            var major = new AcademicMajor(
                _nextMajorId++,
                department.Id,
                department.Code,
                department.Name,
                department.OrganizationId,
                department.OrganizationCode,
                code,
                name,
                description,
                true,
                utcNow,
                utcNow);
            _majors[major.Id] = major;
            return Task.FromResult(major);
        }

        public Task<AcademicMajor> UpdateMajorAsync(
            long majorId,
            long departmentId,
            string code,
            string name,
            string? description,
            DateTime utcNow,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AcademicMajor> SetMajorActiveAsync(
            long majorId,
            bool isActive,
            DateTime utcNow,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<AcademicHierarchyOrganization>> GetHierarchyAsync(
            long? organizationId,
            string? search,
            bool includeInactive,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AcademicHierarchyOrganization>>([]);

        public Task<AcademicUserScope?> GetUserScopeAsync(
            long userId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<AcademicUserScope?>(userId == 3001
                ? new AcademicUserScope(1, 10)
                : null);
    }
}
