namespace AIPMS.Application.Features.Academic.Models;

public sealed record AcademicOrganization(
    long Id,
    string Code,
    string Name,
    string? Description,
    bool IsActive,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record AcademicDepartment(
    long Id,
    long OrganizationId,
    string OrganizationCode,
    string OrganizationName,
    string Code,
    string Name,
    string? Description,
    bool IsActive,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record AcademicMajor(
    long Id,
    long DepartmentId,
    string DepartmentCode,
    string DepartmentName,
    long OrganizationId,
    string OrganizationCode,
    string Code,
    string Name,
    string? Description,
    bool IsActive,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record AcademicUserScope(
    long OrganizationId,
    long DepartmentId);

public sealed record AcademicHierarchyOrganization(
    AcademicOrganization Organization,
    IReadOnlyList<AcademicHierarchyDepartment> Departments);

public sealed record AcademicHierarchyDepartment(
    AcademicDepartment Department,
    IReadOnlyList<AcademicMajor> Majors);
