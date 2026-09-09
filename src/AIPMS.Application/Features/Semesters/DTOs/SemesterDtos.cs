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

public sealed record SetSemesterStatusRequest(string Status);

public sealed record CreateProjectPeriodRequest(
    long AcademicSemesterId,
    string Code,
    string Name,
    string PeriodType,
    DateTime StartAt,
    DateTime EndAt);

public sealed record UpdateProjectPeriodRequest(
    string Code,
    string Name,
    string PeriodType,
    DateTime StartAt,
    DateTime EndAt);

public sealed record SetProjectPeriodStatusRequest(string Status);
