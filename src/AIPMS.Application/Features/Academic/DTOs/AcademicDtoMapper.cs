using AIPMS.Application.Features.Academic.Models;

namespace AIPMS.Application.Features.Academic.DTOs;

internal static class AcademicDtoMapper
{
    public static OrganizationDto ToDto(this AcademicOrganization organization) =>
        new(
            organization.Id,
            organization.Code,
            organization.Name,
            organization.Description,
            organization.IsActive,
            organization.CreatedAt,
            organization.UpdatedAt);

    public static DepartmentDto ToDto(this AcademicDepartment department) =>
        new(
            department.Id,
            department.OrganizationId,
            department.OrganizationCode,
            department.OrganizationName,
            department.Code,
            department.Name,
            department.Description,
            department.IsActive,
            department.CreatedAt,
            department.UpdatedAt);

    public static MajorDto ToDto(this AcademicMajor major) =>
        new(
            major.Id,
            major.DepartmentId,
            major.DepartmentCode,
            major.DepartmentName,
            major.OrganizationId,
            major.OrganizationCode,
            major.Code,
            major.Name,
            major.Description,
            major.IsActive,
            major.CreatedAt,
            major.UpdatedAt);

    public static AcademicHierarchyOrganizationDto ToDto(
        this AcademicHierarchyOrganization organization) =>
        new(
            organization.Organization.ToDto(),
            organization.Departments
                .Select(static department => new AcademicHierarchyDepartmentDto(
                    department.Department.ToDto(),
                    department.Majors.Select(static major => major.ToDto()).ToArray()))
                .ToArray());
}
