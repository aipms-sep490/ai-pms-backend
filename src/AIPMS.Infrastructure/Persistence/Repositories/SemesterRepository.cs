using System.Threading.Tasks;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Semesters.Abstractions;
using AIPMS.Application.Features.Semesters.Models;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Mappers;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using AcademicSemesterEntity = AIPMS.Infrastructure.Persistence.Generated.Models.AcademicSemester;
using ProjectPeriodEntity = AIPMS.Infrastructure.Persistence.Generated.Models.ProjectPeriod;

namespace AIPMS.Infrastructure.Persistence.Repositories;

internal sealed class SemesterRepository(AipmsDbContext context)
    : ISemesterRepository
{
    // ── Semester ──────────────────────────────────────────────────────────────

    public async Task<PagedResult<AcademicSemesterModel>> GetSemestersAsync(
        long? organizationId,
        string? search,
        string? status,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = context.AcademicSemesters
            .AsNoTracking()
            .Include(static s => s.Organization)
            .AsQueryable();

        if (organizationId.HasValue)
        {
            query = query.Where(s => s.OrganizationId == organizationId.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(s =>
                s.Code.Contains(search)
                || s.Name.Contains(search)
                || s.Organization.Code.Contains(search)
                || s.Organization.Name.Contains(search));
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(s => s.Status == status);
        }

        var totalCount = await query.LongCountAsync(cancellationToken);
        var entities = await query
            .OrderBy(static s => s.Organization.Code)
            .ThenByDescending(static s => s.StartDate)
            .ThenBy(static s => s.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<AcademicSemesterModel>(
            entities.Select(static s =>
                s.ToApplication(s.Organization.Code, s.Organization.Name)).ToArray(),
            page,
            pageSize,
            totalCount);
    }

    public async Task<AcademicSemesterModel?> GetSemesterAsync(
        long semesterId,
        CancellationToken cancellationToken = default)
    {
        var entity = await context.AcademicSemesters
            .AsNoTracking()
            .Include(static s => s.Organization)
            .SingleOrDefaultAsync(s => s.Id == semesterId, cancellationToken);

        return entity?.ToApplication(entity.Organization.Code, entity.Organization.Name);
    }

    public Task<bool> SemesterCodeExistsAsync(
        long organizationId,
        string code,
        long? excludedSemesterId,
        CancellationToken cancellationToken = default) =>
        context.AcademicSemesters.AsNoTracking().AnyAsync(
            s =>
                s.OrganizationId == organizationId
                && (!excludedSemesterId.HasValue || s.Id != excludedSemesterId.Value)
                && s.Code == code,
            cancellationToken);

    public async Task<AcademicSemesterModel> CreateSemesterAsync(
        long organizationId,
        string code,
        string name,
        DateOnly startDate,
        DateOnly endDate,
        DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        var entity = new AcademicSemesterEntity
        {
            OrganizationId = organizationId,
            Code = code,
            Name = name,
            StartDate = startDate,
            EndDate = endDate,
            Status = "DRAFT",
            CreatedAt = utcNow,
            UpdatedAt = utcNow
        };

        context.AcademicSemesters.Add(entity);
        await SaveChangesAsync(cancellationToken);
        return (await GetSemesterAsync(entity.Id, cancellationToken))!;
    }

    public async Task<AcademicSemesterModel> UpdateSemesterAsync(
        long semesterId,
        string code,
        string name,
        DateOnly startDate,
        DateOnly endDate,
        DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        var entity = await context.AcademicSemesters
            .SingleAsync(s => s.Id == semesterId, cancellationToken);

        entity.Code = code;
        entity.Name = name;
        entity.StartDate = startDate;
        entity.EndDate = endDate;
        entity.UpdatedAt = utcNow;

        await SaveChangesAsync(cancellationToken);
        return (await GetSemesterAsync(entity.Id, cancellationToken))!;
    }

    public async Task<AcademicSemesterModel> SetSemesterStatusAsync(
        long semesterId,
        string status,
        DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        var entity = await context.AcademicSemesters
            .SingleAsync(s => s.Id == semesterId, cancellationToken);

        entity.Status = status;
        entity.UpdatedAt = utcNow;

        await SaveChangesAsync(cancellationToken);
        return (await GetSemesterAsync(entity.Id, cancellationToken))!;
    }

    // ── ProjectPeriod ─────────────────────────────────────────────────────────

    public async Task<PagedResult<ProjectPeriodModel>> GetProjectPeriodsAsync(
        long? semesterId,
        string? search,
        string? status,
        string? periodType,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = context.ProjectPeriods
            .AsNoTracking()
            .Include(static p => p.AcademicSemester)
            .AsQueryable();

        if (semesterId.HasValue)
        {
            query = query.Where(p => p.AcademicSemesterId == semesterId.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(p =>
                p.Code.Contains(search)
                || p.Name.Contains(search)
                || p.AcademicSemester.Code.Contains(search)
                || p.AcademicSemester.Name.Contains(search));
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(p => p.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(periodType))
        {
            query = query.Where(p => p.PeriodType == periodType);
        }

        var totalCount = await query.LongCountAsync(cancellationToken);
        var entities = await query
            .OrderBy(static p => p.AcademicSemesterId)
            .ThenBy(static p => p.StartAt)
            .ThenBy(static p => p.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<ProjectPeriodModel>(
            entities.Select(static p => p.ToApplication()).ToArray(),
            page,
            pageSize,
            totalCount);
    }

    public async Task<ProjectPeriodModel?> GetProjectPeriodAsync(
        long periodId,
        CancellationToken cancellationToken = default)
    {
        var entity = await context.ProjectPeriods
            .AsNoTracking()
            .Include(static p => p.AcademicSemester)
            .SingleOrDefaultAsync(p => p.Id == periodId, cancellationToken);

        return entity?.ToApplication();
    }

    public Task<bool> PeriodCodeExistsAsync(
        long semesterId,
        string code,
        long? excludedPeriodId,
        CancellationToken cancellationToken = default) =>
        context.ProjectPeriods.AsNoTracking().AnyAsync(
            p =>
                p.AcademicSemesterId == semesterId
                && (!excludedPeriodId.HasValue || p.Id != excludedPeriodId.Value)
                && p.Code == code,
            cancellationToken);

    public async Task<ProjectPeriodModel> CreateProjectPeriodAsync(
        long semesterId,
        string code,
        string name,
        string periodType,
        DateTime startAt,
        DateTime endAt,
        DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        var entity = new ProjectPeriodEntity
        {
            AcademicSemesterId = semesterId,
            Code = code,
            Name = name,
            PeriodType = periodType,
            StartAt = startAt,
            EndAt = endAt,
            Status = "DRAFT",
            CreatedAt = utcNow,
            UpdatedAt = utcNow
        };

        context.ProjectPeriods.Add(entity);
        await SaveChangesAsync(cancellationToken);
        return (await GetProjectPeriodAsync(entity.Id, cancellationToken))!;
    }

    public async Task<ProjectPeriodModel> UpdateProjectPeriodAsync(
        long periodId,
        string code,
        string name,
        string periodType,
        DateTime startAt,
        DateTime endAt,
        DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        var entity = await context.ProjectPeriods
            .SingleAsync(p => p.Id == periodId, cancellationToken);

        entity.Code = code;
        entity.Name = name;
        entity.PeriodType = periodType;
        entity.StartAt = startAt;
        entity.EndAt = endAt;
        entity.UpdatedAt = utcNow;

        await SaveChangesAsync(cancellationToken);
        return (await GetProjectPeriodAsync(entity.Id, cancellationToken))!;
    }

    public async Task<ProjectPeriodModel> SetProjectPeriodStatusAsync(
        long periodId,
        string status,
        DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        var entity = await context.ProjectPeriods
            .SingleAsync(p => p.Id == periodId, cancellationToken);

        entity.Status = status;
        entity.UpdatedAt = utcNow;

        await SaveChangesAsync(cancellationToken);
        return (await GetProjectPeriodAsync(entity.Id, cancellationToken))!;
    }

    private async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            throw new ConflictException(
                "A record with the same scoped code already exists.");
        }
    }
}
