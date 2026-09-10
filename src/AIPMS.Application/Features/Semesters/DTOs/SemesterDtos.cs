namespace AIPMS.Application.Features.Semesters.DTOs;

// ── Semester DTOs ─────────────────────────────────────────────────────────────

public sealed record SemesterDto(
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

// ── ProjectPeriod DTOs ────────────────────────────────────────────────────────

public sealed record ProjectPeriodDto(
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

// ── Request bodies ────────────────────────────────────────────────────────────

public sealed record CreateSemesterRequest(
    long OrganizationId,
    string Code,
    string Name,
    DateOnly StartDate,
    DateOnly EndDate);

public sealed record UpdateSemesterRequest(
    string Code,
    string Name,
    DateOnly StartDate,
    DateOnly EndDate);

public sealed record SetSemesterStatusRequest(
    string Status,
    string? ExpectedStatus = null);

public sealed record CreateProjectPeriodRequest(
    long AcademicSemesterId,
    string Code,
    string Name,
    string PeriodType,
    DateTime StartAt,
    DateTime EndAt,
    int? MinTeamSize = 3,
    int? MaxTeamSize = 5,
    int? MinDistinctMajors = 1,
    int? MaxProjectsPerSupervisor = 5,
    long? MilestoneTemplateId = null,
    long? RubricId = null);

public sealed record UpdateProjectPeriodRequest(
    string Code,
    string Name,
    string PeriodType,
    DateTime StartAt,
    DateTime EndAt,
    int? MinTeamSize = null,
    int? MaxTeamSize = null,
    int? MinDistinctMajors = null,
    int? MaxProjectsPerSupervisor = null,
    long? MilestoneTemplateId = null,
    long? RubricId = null);

public sealed record SetProjectPeriodStatusRequest(
    string Status,
    string? ExpectedStatus = null);
