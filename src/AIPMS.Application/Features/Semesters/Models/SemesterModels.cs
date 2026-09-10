namespace AIPMS.Application.Features.Semesters.Models;

public sealed record AcademicSemesterModel(
    long Id,
    long OrganizationId,
    string OrganizationCode,
    string OrganizationName,
    string Code,
    string Name,
    DateOnly StartDate,
    DateOnly EndDate,
    string Status,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record ProjectPeriodModel(
    long Id,
    long AcademicSemesterId,
    string SemesterCode,
    string SemesterName,
    string Code,
    string Name,
    string PeriodType,
    DateTime StartAt,
    DateTime EndAt,
    string Status,
    int? MinTeamSize,
    int? MaxTeamSize,
    int? MinDistinctMajors,
    int? MaxProjectsPerSupervisor,
    long? MilestoneTemplateId,
    long? RubricId,
    DateTime CreatedAt,
    DateTime UpdatedAt);
