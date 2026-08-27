using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Academic.Abstractions;
using AIPMS.Application.Features.Academic.Models;

namespace AIPMS.UnitTests.Application;

internal sealed class TestCurrentUser(
    long userId,
    params string[] roles) : ICurrentUser
{
    public bool IsAuthenticated => true;

    public long? UserId => userId;

    public string? Email => "academic.staff@aipms.test";

    public string? FullName => "Academic Test Staff";

    public IReadOnlyCollection<string> Roles => roles;
}

internal sealed class RecordingAuditTrail : IAuditTrail
{
    public List<AuditEntry> Entries { get; } = [];

    public Task RecordAsync(
        AuditEntry entry,
        CancellationToken cancellationToken = default)
    {
        Entries.Add(entry);
        return Task.CompletedTask;
    }
}

internal class StubAcademicStructureRepository : IAcademicStructureRepository
{
    private long _nextOrganizationId = 100;
    private long _nextDepartmentId = 200;
    private long _nextMajorId = 300;

    public Dictionary<long, AcademicOrganization> Organizations { get; } = [];

    public Dictionary<long, AcademicDepartment> Departments { get; } = [];

    public Dictionary<long, AcademicMajor> Majors { get; } = [];

    public Dictionary<long, AcademicUserScope> UserScopes { get; } = [];

    public bool OrganizationDuplicate { get; set; }

    public bool DepartmentDuplicate { get; set; }

    public bool MajorDuplicate { get; set; }

    public Task<PagedResult<AcademicOrganization>> GetOrganizationsAsync(
        string? search,
        bool? isActive,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new PagedResult<AcademicOrganization>(
            Organizations.Values.ToArray(),
            page,
            pageSize,
            Organizations.Count));

    public Task<AcademicOrganization?> GetOrganizationAsync(
        long organizationId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Organizations.GetValueOrDefault(organizationId));

    public Task<bool> OrganizationCodeOrNameExistsAsync(
        string code,
        string name,
        long? excludedOrganizationId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(OrganizationDuplicate);

    public Task<AcademicOrganization> CreateOrganizationAsync(
        string code,
        string name,
        string? description,
        DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        var result = new AcademicOrganization(
            _nextOrganizationId++,
            code,
            name,
            description,
            true,
            utcNow,
            utcNow);
        Organizations[result.Id] = result;
        return Task.FromResult(result);
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
        Task.FromResult(new PagedResult<AcademicDepartment>(
            Departments.Values.ToArray(),
            page,
            pageSize,
            Departments.Count));

    public Task<AcademicDepartment?> GetDepartmentAsync(
        long departmentId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Departments.GetValueOrDefault(departmentId));

    public Task<bool> DepartmentCodeOrNameExistsAsync(
        long organizationId,
        string code,
        string name,
        long? excludedDepartmentId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(DepartmentDuplicate);

    public Task<AcademicDepartment> CreateDepartmentAsync(
        long organizationId,
        string code,
        string name,
        string? description,
        DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        var organization = Organizations[organizationId];
        var result = new AcademicDepartment(
            _nextDepartmentId++,
            organizationId,
            organization.Code,
            organization.Name,
            code,
            name,
            description,
            true,
            utcNow,
            utcNow);
        Departments[result.Id] = result;
        return Task.FromResult(result);
    }

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
        Task.FromResult(new PagedResult<AcademicMajor>(
            Majors.Values.ToArray(),
            page,
            pageSize,
            Majors.Count));

    public Task<AcademicMajor?> GetMajorAsync(
        long majorId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Majors.GetValueOrDefault(majorId));

    public Task<bool> MajorCodeOrNameExistsAsync(
        long departmentId,
        string code,
        string name,
        long? excludedMajorId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(MajorDuplicate);

    public Task<AcademicMajor> CreateMajorAsync(
        long departmentId,
        string code,
        string name,
        string? description,
        DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        var department = Departments[departmentId];
        var result = new AcademicMajor(
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
        Majors[result.Id] = result;
        return Task.FromResult(result);
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
        Task.FromResult(UserScopes.GetValueOrDefault(userId));
}
