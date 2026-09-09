using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Semesters.Models;

namespace AIPMS.Application.Features.Semesters.Abstractions;

public interface ISemesterRepository
{
    // ── Semester ─────────────────────────────────────────────────────────────

    Task<PagedResult<AcademicSemesterModel>> GetSemestersAsync(
        long? organizationId,
        string? search,
        string? status,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<AcademicSemesterModel?> GetSemesterAsync(
        long semesterId,
        CancellationToken cancellationToken = default);

    Task<bool> SemesterCodeExistsAsync(
        long organizationId,
        string code,
        long? excludedSemesterId,
        CancellationToken cancellationToken = default);

    Task<AcademicSemesterModel> CreateSemesterAsync(
        long organizationId,
        string code,
        string name,
        DateOnly startDate,
        DateOnly endDate,
        DateTime utcNow,
        CancellationToken cancellationToken = default);

    Task<AcademicSemesterModel> UpdateSemesterAsync(
        long semesterId,
        string code,
        string name,
        DateOnly startDate,
        DateOnly endDate,
        DateTime utcNow,
        CancellationToken cancellationToken = default);

    Task<AcademicSemesterModel> SetSemesterStatusAsync(
        long semesterId,
        string status,
        DateTime utcNow,
        CancellationToken cancellationToken = default);

    // ── ProjectPeriod ─────────────────────────────────────────────────────────

    Task<PagedResult<ProjectPeriodModel>> GetProjectPeriodsAsync(
        long? semesterId,
        string? search,
        string? status,
        string? periodType,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<ProjectPeriodModel?> GetProjectPeriodAsync(
        long periodId,
        CancellationToken cancellationToken = default);

    Task<bool> PeriodCodeExistsAsync(
        long semesterId,
        string code,
        long? excludedPeriodId,
        CancellationToken cancellationToken = default);

    Task<ProjectPeriodModel> CreateProjectPeriodAsync(
        long semesterId,
        string code,
        string name,
        string periodType,
        DateTime startAt,
        DateTime endAt,
        DateTime utcNow,
        CancellationToken cancellationToken = default);

    Task<ProjectPeriodModel> UpdateProjectPeriodAsync(
        long periodId,
        string code,
        string name,
        string periodType,
        DateTime startAt,
        DateTime endAt,
        DateTime utcNow,
        CancellationToken cancellationToken = default);

    Task<ProjectPeriodModel> SetProjectPeriodStatusAsync(
        long periodId,
        string status,
        DateTime utcNow,
        CancellationToken cancellationToken = default);
}
