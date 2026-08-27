namespace AIPMS.Application.Features.Academic.DTOs;

public sealed record OrganizationDto(
    long Id,
    string Code,
    string Name,
    string? Description,
    bool IsActive,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record DepartmentDto(
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

public sealed record MajorDto(
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

public sealed record AcademicHierarchyOrganizationDto(
    OrganizationDto Organization,
    IReadOnlyList<AcademicHierarchyDepartmentDto> Departments);

public sealed record AcademicHierarchyDepartmentDto(
    DepartmentDto Department,
    IReadOnlyList<MajorDto> Majors);

public sealed record CreateOrganizationRequest(
    string Code,
    string Name,
    string? Description);

public sealed record UpdateOrganizationRequest(
    string Code,
    string Name,
    string? Description);

public sealed record CreateDepartmentRequest(
    long OrganizationId,
    string Code,
    string Name,
    string? Description);

public sealed record UpdateDepartmentRequest(
    string Code,
    string Name,
    string? Description);

public sealed record CreateMajorRequest(
    long DepartmentId,
    string Code,
    string Name,
    string? Description);

public sealed record UpdateMajorRequest(
    long DepartmentId,
    string Code,
    string Name,
    string? Description);

public sealed record SetAcademicRecordStatusRequest(bool IsActive);
