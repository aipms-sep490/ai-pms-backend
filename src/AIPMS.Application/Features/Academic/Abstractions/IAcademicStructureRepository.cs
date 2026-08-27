using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Academic.Models;

namespace AIPMS.Application.Features.Academic.Abstractions;

public interface IAcademicStructureRepository
{
    Task<PagedResult<AcademicOrganization>> GetOrganizationsAsync(
        string? search,
        bool? isActive,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<AcademicOrganization?> GetOrganizationAsync(
        long organizationId,
        CancellationToken cancellationToken = default);

    Task<bool> OrganizationCodeOrNameExistsAsync(
        string code,
        string name,
        long? excludedOrganizationId,
        CancellationToken cancellationToken = default);

    Task<AcademicOrganization> CreateOrganizationAsync(
        string code,
        string name,
        string? description,
        DateTime utcNow,
        CancellationToken cancellationToken = default);

    Task<AcademicOrganization> UpdateOrganizationAsync(
        long organizationId,
        string code,
        string name,
        string? description,
        DateTime utcNow,
        CancellationToken cancellationToken = default);

    Task<AcademicOrganization> SetOrganizationActiveAsync(
        long organizationId,
        bool isActive,
        DateTime utcNow,
        CancellationToken cancellationToken = default);

    Task<PagedResult<AcademicDepartment>> GetDepartmentsAsync(
        long? organizationId,
        string? search,
        bool? isActive,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<AcademicDepartment?> GetDepartmentAsync(
        long departmentId,
        CancellationToken cancellationToken = default);

    Task<bool> DepartmentCodeOrNameExistsAsync(
        long organizationId,
        string code,
        string name,
        long? excludedDepartmentId,
        CancellationToken cancellationToken = default);

    Task<AcademicDepartment> CreateDepartmentAsync(
        long organizationId,
        string code,
        string name,
        string? description,
        DateTime utcNow,
        CancellationToken cancellationToken = default);

    Task<AcademicDepartment> UpdateDepartmentAsync(
        long departmentId,
        string code,
        string name,
        string? description,
        DateTime utcNow,
        CancellationToken cancellationToken = default);

    Task<AcademicDepartment> SetDepartmentActiveAsync(
        long departmentId,
        bool isActive,
        DateTime utcNow,
        CancellationToken cancellationToken = default);

    Task<PagedResult<AcademicMajor>> GetMajorsAsync(
        long? organizationId,
        long? departmentId,
        string? search,
        bool? isActive,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<AcademicMajor?> GetMajorAsync(
        long majorId,
        CancellationToken cancellationToken = default);

    Task<bool> MajorCodeOrNameExistsAsync(
        long departmentId,
        string code,
        string name,
        long? excludedMajorId,
        CancellationToken cancellationToken = default);

    Task<AcademicMajor> CreateMajorAsync(
        long departmentId,
        string code,
        string name,
        string? description,
        DateTime utcNow,
        CancellationToken cancellationToken = default);

    Task<AcademicMajor> UpdateMajorAsync(
        long majorId,
        long departmentId,
        string code,
        string name,
        string? description,
        DateTime utcNow,
        CancellationToken cancellationToken = default);

    Task<AcademicMajor> SetMajorActiveAsync(
        long majorId,
        bool isActive,
        DateTime utcNow,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AcademicHierarchyOrganization>> GetHierarchyAsync(
        long? organizationId,
        string? search,
        bool includeInactive,
        CancellationToken cancellationToken = default);

    Task<AcademicUserScope?> GetUserScopeAsync(
        long userId,
        CancellationToken cancellationToken = default);
}
