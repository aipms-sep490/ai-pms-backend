using AIPMS.Application.Features.Academic.Models;
using DepartmentEntity = AIPMS.Infrastructure.Persistence.Generated.Models.Department;
using MajorEntity = AIPMS.Infrastructure.Persistence.Generated.Models.Major;
using OrganizationEntity = AIPMS.Infrastructure.Persistence.Generated.Models.Organization;

namespace AIPMS.Infrastructure.Persistence.Mappers;

internal static class AcademicStructureMapper
{
    public static AcademicOrganization ToApplication(this OrganizationEntity organization) =>
        new(
            organization.Id,
            organization.Code,
            organization.Name,
            organization.Description,
            organization.IsActive,
            organization.CreatedAt,
            organization.UpdatedAt);

    public static AcademicDepartment ToApplication(this DepartmentEntity department) =>
        new(
            department.Id,
            department.OrganizationId,
            department.Organization.Code,
            department.Organization.Name,
            department.Code,
            department.Name,
            department.Description,
            department.IsActive,
            department.CreatedAt,
            department.UpdatedAt);

    public static AcademicMajor ToApplication(this MajorEntity major) =>
        new(
            major.Id,
            major.DepartmentId,
            major.Department.Code,
            major.Department.Name,
            major.Department.OrganizationId,
            major.Department.Organization.Code,
            major.Code,
            major.Name,
            major.Description,
            major.IsActive,
            major.CreatedAt,
            major.UpdatedAt);

    public static AcademicHierarchyOrganization ToHierarchy(
        this OrganizationEntity organization,
        bool includeInactive)
    {
        var departments = organization.Departments
            .Where(department => includeInactive || department.IsActive)
            .OrderBy(static department => department.Code, StringComparer.Ordinal)
            .ThenBy(static department => department.Id)
            .Select(department => new AcademicHierarchyDepartment(
                department.ToApplication(),
                department.Majors
                    .Where(major => includeInactive || major.IsActive)
                    .OrderBy(static major => major.Code, StringComparer.Ordinal)
                    .ThenBy(static major => major.Id)
                    .Select(static major => major.ToApplication())
                    .ToArray()))
            .ToArray();

        return new AcademicHierarchyOrganization(
            organization.ToApplication(),
            departments);
    }
}
