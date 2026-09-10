using AIPMS.Application.Features.Semesters.Models;

namespace AIPMS.Application.Features.Semesters.DTOs;

internal static class SemesterDtoMapper
{
    public static SemesterDto ToDto(this AcademicSemesterModel semester) =>
        new(
            semester.Id,
            semester.OrganizationId,
            semester.OrganizationCode,
            semester.OrganizationName,
            semester.Code,
            semester.Name,
            semester.StartDate,
            semester.EndDate,
            semester.Status,
            semester.CreatedAt,
            semester.UpdatedAt);

    public static ProjectPeriodDto ToDto(this ProjectPeriodModel period) =>
        new(
            period.Id,
            period.AcademicSemesterId,
            period.SemesterCode,
            period.SemesterName,
            period.Code,
            period.Name,
            period.PeriodType,
            period.StartAt,
            period.EndAt,
            period.Status,
            period.MinTeamSize,
            period.MaxTeamSize,
            period.MinDistinctMajors,
            period.MaxProjectsPerSupervisor,
            period.MilestoneTemplateId,
            period.RubricId,
            period.CreatedAt,
            period.UpdatedAt);
}
