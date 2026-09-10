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
        string? expectedStatus,
        DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        var useLocalTx = context.Database.CurrentTransaction == null;
        var transaction = useLocalTx
            ? await context.Database.BeginTransactionAsync(cancellationToken)
            : null;

        try
        {
            var lockKey = $"sem_lock_{semesterId}";
            await context.Database.ExecuteSqlRawAsync(
                "EXEC sp_getapplock @Resource = @p0, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 10000",
                new object[] { lockKey },
                cancellationToken);

            if (!string.IsNullOrEmpty(expectedStatus))
            {
                var affected = await context.AcademicSemesters
                    .Where(s => s.Id == semesterId && s.Status == expectedStatus)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(b => b.Status, status)
                        .SetProperty(b => b.UpdatedAt, utcNow), cancellationToken);

                if (affected == 0)
                {
                    var currentStatus = await context.AcademicSemesters
                        .Where(s => s.Id == semesterId)
                        .Select(s => s.Status)
                        .FirstOrDefaultAsync(cancellationToken);

                    if (currentStatus == null)
                    {
                        throw new NotFoundException("AcademicSemester", semesterId);
                    }

                    throw new ConflictException(
                        $"Academic Semester status was modified concurrently (expected '{expectedStatus}', actual '{currentStatus}').");
                }
            }
            else
            {
                var entity = await context.AcademicSemesters
                    .SingleOrDefaultAsync(s => s.Id == semesterId, cancellationToken)
                    ?? throw new NotFoundException("AcademicSemester", semesterId);

                entity.Status = status;
                entity.UpdatedAt = utcNow;
                await SaveChangesAsync(cancellationToken);
            }

            if (transaction != null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return (await GetSemesterAsync(semesterId, cancellationToken))!;
        }
        catch
        {
            if (transaction != null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }
            throw;
        }
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
        int? minTeamSize,
        int? maxTeamSize,
        int? minDistinctMajors,
        int? maxProjectsPerSupervisor,
        long? milestoneTemplateId,
        long? rubricId,
        DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        var useLocalTx = context.Database.CurrentTransaction == null;
        var transaction = useLocalTx
            ? await context.Database.BeginTransactionAsync(cancellationToken)
            : null;

        try
        {
            var lockKey = $"pp_lock_{semesterId}_{periodType}";
            await context.Database.ExecuteSqlRawAsync(
                "EXEC sp_getapplock @Resource = @p0, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 10000",
                new object[] { lockKey },
                cancellationToken);

            bool hasOverlap = await context.ProjectPeriods.AnyAsync(
                p => p.AcademicSemesterId == semesterId
                     && p.PeriodType == periodType
                     && p.Status != "ARCHIVED"
                     && p.StartAt < endAt
                     && startAt < p.EndAt,
                cancellationToken);

            if (hasOverlap)
            {
                throw new ConflictException(
                    $"Project Period window overlaps with an existing '{periodType}' period in this semester.");
            }

            var entity = new ProjectPeriodEntity
            {
                AcademicSemesterId = semesterId,
                Code = code,
                Name = name,
                PeriodType = periodType,
                StartAt = startAt,
                EndAt = endAt,
                MinTeamSize = minTeamSize,
                MaxTeamSize = maxTeamSize,
                MinDistinctMajors = minDistinctMajors,
                MaxProjectsPerSupervisor = maxProjectsPerSupervisor,
                MilestoneTemplateId = milestoneTemplateId,
                RubricId = rubricId,
                Status = "DRAFT",
                CreatedAt = utcNow,
                UpdatedAt = utcNow
            };

            context.ProjectPeriods.Add(entity);
            await context.SaveChangesAsync(cancellationToken);

            if (transaction != null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return (await GetProjectPeriodAsync(entity.Id, cancellationToken))!;
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            if (transaction != null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }
            throw new ConflictException(
                "A project period with the same code already exists in this semester.");
        }
        catch
        {
            if (transaction != null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }
            throw;
        }
    }

    public async Task<ProjectPeriodModel> UpdateProjectPeriodAsync(
        long periodId,
        string code,
        string name,
        string periodType,
        DateTime startAt,
        DateTime endAt,
        int? minTeamSize,
        int? maxTeamSize,
        int? minDistinctMajors,
        int? maxProjectsPerSupervisor,
        long? milestoneTemplateId,
        long? rubricId,
        DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        var useLocalTx = context.Database.CurrentTransaction == null;
        var transaction = useLocalTx
            ? await context.Database.BeginTransactionAsync(cancellationToken)
            : null;

        try
        {
            var entity = await context.ProjectPeriods
                .Include(p => p.AcademicSemester)
                .SingleOrDefaultAsync(p => p.Id == periodId, cancellationToken)
                ?? throw new NotFoundException("ProjectPeriod", periodId);

            var oldPeriodType = entity.PeriodType;
            var newPeriodType = periodType;

            var lockTypes = new List<string> { oldPeriodType };
            if (!string.Equals(oldPeriodType, newPeriodType, StringComparison.OrdinalIgnoreCase))
            {
                lockTypes.Add(newPeriodType);
            }
            lockTypes.Sort(StringComparer.Ordinal);

            foreach (var lockType in lockTypes)
            {
                var lockKey = $"pp_lock_{entity.AcademicSemesterId}_{lockType}";
                await context.Database.ExecuteSqlRawAsync(
                    "EXEC sp_getapplock @Resource = @p0, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 10000",
                    new object[] { lockKey },
                    cancellationToken);
            }

            if (entity.Status is "CLOSED" or "ARCHIVED")
            {
                throw new ConflictException(
                    "A closed or archived project period cannot be modified.");
            }

            if (entity.AcademicSemester.Status is "CLOSED" or "ARCHIVED")
            {
                throw new ConflictException(
                    "Cannot modify a project period belonging to a closed or archived semester.");
            }

            bool hasOverlap = await context.ProjectPeriods.AnyAsync(
                p => p.AcademicSemesterId == entity.AcademicSemesterId
                     && p.Id != periodId
                     && p.PeriodType == periodType
                     && p.Status != "ARCHIVED"
                     && p.StartAt < endAt
                     && startAt < p.EndAt,
                cancellationToken);

            if (hasOverlap)
            {
                throw new ConflictException(
                    $"Project Period window overlaps with an existing '{periodType}' period in this semester.");
            }

            entity.Code = code;
            entity.Name = name;
            entity.PeriodType = periodType;
            entity.StartAt = startAt;
            entity.EndAt = endAt;
            entity.MinTeamSize = minTeamSize;
            entity.MaxTeamSize = maxTeamSize;
            entity.MinDistinctMajors = minDistinctMajors;
            entity.MaxProjectsPerSupervisor = maxProjectsPerSupervisor;
            entity.MilestoneTemplateId = milestoneTemplateId;
            entity.RubricId = rubricId;
            entity.UpdatedAt = utcNow;

            await context.SaveChangesAsync(cancellationToken);

            if (transaction != null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return (await GetProjectPeriodAsync(entity.Id, cancellationToken))!;
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            if (transaction != null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }
            throw new ConflictException(
                "A project period with the same code already exists in this semester.");
        }
        catch
        {
            if (transaction != null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }
            throw;
        }
    }

    public async Task<ProjectPeriodModel> SetProjectPeriodStatusAsync(
        long periodId,
        string status,
        string? expectedStatus,
        DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        var entity = await context.ProjectPeriods
            .AsNoTracking()
            .SingleOrDefaultAsync(p => p.Id == periodId, cancellationToken)
            ?? throw new NotFoundException("ProjectPeriod", periodId);

        var useLocalTx = context.Database.CurrentTransaction == null;
        var transaction = useLocalTx
            ? await context.Database.BeginTransactionAsync(cancellationToken)
            : null;

        try
        {
            var lockKey = $"pp_lock_{entity.AcademicSemesterId}_{entity.PeriodType}";
            await context.Database.ExecuteSqlRawAsync(
                "EXEC sp_getapplock @Resource = @p0, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 10000",
                new object[] { lockKey },
                cancellationToken);

            if (status == "ACTIVE")
            {
                bool hasActiveOverlap = await context.ProjectPeriods.AnyAsync(
                    p => p.AcademicSemesterId == entity.AcademicSemesterId
                         && p.Id != periodId
                         && p.PeriodType == entity.PeriodType
                         && p.Status == "ACTIVE"
                         && p.StartAt < entity.EndAt
                         && entity.StartAt < p.EndAt,
                    cancellationToken);

                if (hasActiveOverlap)
                {
                    throw new ConflictException(
                        $"Cannot activate project period because it overlaps with an existing ACTIVE '{entity.PeriodType}' period in this semester.");
                }
            }

            if (!string.IsNullOrEmpty(expectedStatus))
            {
                var affected = await context.ProjectPeriods
                    .Where(p => p.Id == periodId && p.Status == expectedStatus)
                    .ExecuteUpdateAsync(p => p
                        .SetProperty(b => b.Status, status)
                        .SetProperty(b => b.UpdatedAt, utcNow), cancellationToken);

                if (affected == 0)
                {
                    var currentStatus = await context.ProjectPeriods
                        .Where(p => p.Id == periodId)
                        .Select(p => p.Status)
                        .FirstOrDefaultAsync(cancellationToken);

                    if (currentStatus == null)
                    {
                        throw new NotFoundException("ProjectPeriod", periodId);
                    }

                    throw new ConflictException(
                        $"Project Period status was modified concurrently (expected '{expectedStatus}', actual '{currentStatus}').");
                }
            }
            else
            {
                var trackedEntity = await context.ProjectPeriods
                    .SingleOrDefaultAsync(p => p.Id == periodId, cancellationToken)
                    ?? throw new NotFoundException("ProjectPeriod", periodId);

                trackedEntity.Status = status;
                trackedEntity.UpdatedAt = utcNow;
                await context.SaveChangesAsync(cancellationToken);
            }

            if (transaction != null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return (await GetProjectPeriodAsync(periodId, cancellationToken))!;
        }
        catch
        {
            if (transaction != null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }
            throw;
        }
    }

    public async Task<bool> HasActiveProjectsAsync(
        long semesterId,
        CancellationToken cancellationToken = default)
    {
        var activeStatuses = new[]
        {
            "DRAFT", "SUBMITTED", "UNDER_REVIEW", "REVISION_REQUIRED",
            "APPROVED", "SUPERVISOR_PENDING", "ACTIVE", "FINAL_SUBMISSION"
        };

        return await context.Projects
            .AsNoTracking()
            .AnyAsync(p => p.Team.AcademicSemesterId == semesterId && activeStatuses.Contains(p.Status), cancellationToken);
    }

    public async Task<bool> ValidateRubricUsableAsync(
        long rubricId,
        long semesterId,
        CancellationToken cancellationToken = default)
    {
        return await context.Rubrics
            .AsNoTracking()
            .AnyAsync(r => r.Id == rubricId
                           && r.IsActive
                           && (r.AcademicSemesterId == null || r.AcademicSemesterId == semesterId),
                cancellationToken);
    }

    public async Task<T> ExecuteInTransactionAsync<T>(
        Func<Task<T>> action,
        CancellationToken cancellationToken = default)
    {
        var useLocalTx = context.Database.CurrentTransaction == null;
        var transaction = useLocalTx
            ? await context.Database.BeginTransactionAsync(cancellationToken)
            : null;

        try
        {
            var result = await action();
            if (transaction != null)
            {
                await transaction.CommitAsync(cancellationToken);
            }
            return result;
        }
        catch
        {
            if (transaction != null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }
            throw;
        }
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
