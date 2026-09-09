using AIPMS.Application.Features.Semesters.Models;
using AcademicSemesterEntity = AIPMS.Infrastructure.Persistence.Generated.Models.AcademicSemester;
using ProjectPeriodEntity = AIPMS.Infrastructure.Persistence.Generated.Models.ProjectPeriod;

namespace AIPMS.Infrastructure.Persistence.Mappers;

internal static class SemesterMapper
{
    public static AcademicSemesterModel ToApplication(
        this AcademicSemesterEntity entity,
        string organizationCode,
        string organizationName) =>
        new(
            entity.Id,
            entity.OrganizationId,
            organizationCode,
            organizationName,
            entity.Code,
            entity.Name,
            entity.StartDate,
            entity.EndDate,
            entity.Status,
            entity.CreatedAt,
            entity.UpdatedAt);

    public static ProjectPeriodModel ToApplication(
        this ProjectPeriodEntity entity) =>
        new(
            entity.Id,
            entity.AcademicSemesterId,
            entity.AcademicSemester.Code,
            entity.AcademicSemester.Name,
            entity.Code,
            entity.Name,
            entity.PeriodType,
            entity.StartAt,
            entity.EndAt,
            entity.Status,
            entity.CreatedAt,
            entity.UpdatedAt);
}
