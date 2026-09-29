using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.ProgressReports.Abstractions;
using AIPMS.Application.Features.ProgressReports.DTOs;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Persistence.Repositories;

public sealed class ReportingCycleRepository(AipmsDbContext context) : IReportingCycleRepository
{
    public async Task<ReportingCycleDto?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        var entity = await context.ProgressReportPeriods
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        return entity is null ? null : ToDto(entity);
    }

    public async Task<PagedResult<ReportingCycleDto>> ListAsync(
        long projectId,
        string? reportType,
        DateTime? from,
        DateTime? to,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = context.ProgressReportPeriods
            .AsNoTracking()
            .Where(p => p.ProjectId == projectId);

        if (!string.IsNullOrWhiteSpace(reportType))
        {
            query = query.Where(p => p.ReportType == reportType.Trim().ToUpperInvariant());
        }

        if (from.HasValue)
        {
            query = query.Where(p => p.PeriodStart >= from.Value);
        }

        if (to.HasValue)
        {
            query = query.Where(p => p.PeriodEnd <= to.Value);
        }

        var total = await query.LongCountAsync(cancellationToken);

        var items = await query
            .OrderBy(p => p.PeriodStart)
            .ThenBy(p => p.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<ReportingCycleDto>(items.Select(ToDto).ToList(), page, pageSize, total);
    }

    public async Task<bool> ExistsOverlapAsync(
        long projectId,
        string reportType,
        DateTime periodStart,
        DateTime periodEnd,
        long? excludeId = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedType = reportType.Trim().ToUpperInvariant();
        return await context.ProgressReportPeriods
            .AsNoTracking()
            .AnyAsync(p => p.ProjectId == projectId
                && p.ReportType == normalizedType
                && (!excludeId.HasValue || p.Id != excludeId.Value)
                && periodStart < p.PeriodEnd
                && p.PeriodStart < periodEnd,
                cancellationToken);
    }

    public async Task<ReportingCycleDto> CreateAsync(
        long projectId,
        long projectPeriodId,
        string reportType,
        DateTime periodStart,
        DateTime periodEnd,
        DateTime deadline,
        string latePolicy,
        long createdBy,
        DateTime now,
        Func<ReportingCycleDto, Task>? onCreated = null,
        CancellationToken cancellationToken = default)
    {
        // Open a SERIALIZABLE transaction so the overlap range-scan (ExistsOverlapAsync) and the
        // INSERT are atomic. Under SERIALIZABLE isolation SQL Server holds key-range locks on the
        // ix_progress_report_periods_lookup scan, preventing a concurrent CREATE for the same
        // project_id + report_type from slipping through the overlap check.
        var tx = context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, cancellationToken)
            : null;
        try
        {
            var lockKey = $"reporting-cycle:{projectId}";
            var rcParam = new Microsoft.Data.SqlClient.SqlParameter("@rc", System.Data.SqlDbType.Int) { Direction = System.Data.ParameterDirection.Output };
            var resourceParam = new Microsoft.Data.SqlClient.SqlParameter("@p0", System.Data.SqlDbType.NVarChar, 255) { Value = lockKey };
            await context.Database.ExecuteSqlRawAsync(
                "EXEC @rc = sp_getapplock @Resource = @p0, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 10000",
                new object[] { rcParam, resourceParam },
                cancellationToken);
            if (rcParam.Value is int rcValue && rcValue < 0)
                throw new ConflictException("Could not acquire lock to validate reporting cycle concurrency.");
            if (await ExistsOverlapAsync(projectId, reportType, periodStart, periodEnd, null, cancellationToken))
                throw new ConflictException("A reporting cycle of this type already overlaps with the requested interval.");

            var entity = new ProgressReportPeriod
            {
                ProjectId = projectId,
                ProjectPeriodId = projectPeriodId,
                ReportType = reportType.Trim().ToUpperInvariant(),
                PeriodStart = periodStart,
                PeriodEnd = periodEnd,
                Deadline = deadline,
                LatePolicy = latePolicy.Trim().ToUpperInvariant(),
                ConcurrencyToken = Guid.NewGuid(),
                CreatedBy = createdBy,
                CreatedAt = now,
                UpdatedAt = now
            };

            context.ProgressReportPeriods.Add(entity);
            await context.SaveChangesAsync(cancellationToken);
            var dto = ToDto(entity);
            if (onCreated != null)
            {
                await onCreated(dto);
            }
            if (tx != null) await tx.CommitAsync(cancellationToken);

            return dto;
        }
        catch
        {
            if (tx != null) await tx.RollbackAsync(cancellationToken);
            throw;
        }
        finally
        {
            if (tx != null) await tx.DisposeAsync();
        }
    }

    public async Task<ReportingCycleDto> UpdateAsync(
        long id,
        DateTime? periodStart,
        DateTime? periodEnd,
        DateTime? deadline,
        string? latePolicy,
        Guid? expectedToken,
        DateTime now,
        Func<ReportingCycleDto, Task>? onUpdated = null,
        CancellationToken cancellationToken = default)
    {
        var tx = context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, cancellationToken)
            : null;
        try
        {
            var projectId = await context.ProgressReportPeriods
                .AsNoTracking()
                .Where(x => x.Id == id)
                .Select(x => x.ProjectId)
                .FirstOrDefaultAsync(cancellationToken);

            if (projectId == 0)
                throw new NotFoundException("ProgressReportPeriod", id);

            var lockKey = $"reporting-cycle:{projectId}";
            var rcParam = new Microsoft.Data.SqlClient.SqlParameter("@rc", System.Data.SqlDbType.Int) { Direction = System.Data.ParameterDirection.Output };
            var resourceParam = new Microsoft.Data.SqlClient.SqlParameter("@p0", System.Data.SqlDbType.NVarChar, 255) { Value = lockKey };
            await context.Database.ExecuteSqlRawAsync(
                "EXEC @rc = sp_getapplock @Resource = @p0, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 10000",
                new object[] { rcParam, resourceParam },
                cancellationToken);

            if (rcParam.Value is int rcValue && rcValue < 0)
                throw new ConflictException("Could not acquire lock to validate reporting cycle concurrency.");

            var entity = await context.ProgressReportPeriods
                .FromSqlInterpolated($"SELECT * FROM dbo.progress_report_periods WITH (UPDLOCK, HOLDLOCK) WHERE id = {id}")
                .FirstOrDefaultAsync(cancellationToken);

            if (entity is null || entity.ProjectId != projectId)
                throw new NotFoundException("ProgressReportPeriod", id);

        if (expectedToken.HasValue && entity.ConcurrencyToken != expectedToken.Value)
            throw new ConflictException("The reporting cycle has been modified by another user. Please refresh and try again.", WorkflowErrorCodes.StaleConcurrencyToken);

        var newStart = periodStart ?? entity.PeriodStart;
        var newEnd = periodEnd ?? entity.PeriodEnd;

        if (newEnd <= newStart)
            throw new ValidationException(new System.Collections.Generic.Dictionary<string, string[]>
            {
                ["periodEnd"] = ["Period end must be strictly after period start."]
            });

        if (periodStart.HasValue || periodEnd.HasValue)
        {
            if (await HasLinkedReportAsync(id, cancellationToken))
                throw new ConflictException("Cannot change period dates of a reporting cycle with linked progress reports.");

            if (await ExistsOverlapAsync(entity.ProjectId, entity.ReportType, newStart, newEnd, id, cancellationToken))
                throw new ConflictException("Reporting cycle overlaps with an existing cycle of the same report type.");

            entity.PeriodStart = newStart;
            entity.PeriodEnd = newEnd;
        }

        if (deadline.HasValue || !string.IsNullOrWhiteSpace(latePolicy))
        {
            var hasSubmitted = await context.ProgressReports
                .AsNoTracking()
                .AnyAsync(r => r.ProgressReportPeriodId == id && r.Status != "DRAFT", cancellationToken);

            if (hasSubmitted)
                throw new ConflictException("Cannot change deadline or late policy of a reporting cycle with submitted progress reports.");
        }

        if (deadline.HasValue)
        {
            entity.Deadline = deadline.Value;
        }

        if (!string.IsNullOrWhiteSpace(latePolicy))
        {
            var policy = latePolicy.Trim().ToUpperInvariant();
            if (policy != "BLOCK" && policy != "FLAG")
                throw new ValidationException(new System.Collections.Generic.Dictionary<string, string[]>
                {
                    ["latePolicy"] = ["Late policy must be either BLOCK or FLAG."]
                });
            entity.LatePolicy = policy;
        }

        entity.UpdatedAt = now;
        entity.ConcurrencyToken = Guid.NewGuid();

        await context.SaveChangesAsync(cancellationToken);
        var dto = ToDto(entity);
        if (onUpdated != null)
        {
            await onUpdated(dto);
        }
        if (tx != null) await tx.CommitAsync(cancellationToken);
        return dto;
        }
        catch
        {
            if (tx != null) await tx.RollbackAsync(cancellationToken);
            throw;
        }
        finally
        {
            if (tx != null) await tx.DisposeAsync();
        }
    }

    public async Task<bool> HasLinkedReportAsync(long cycleId, CancellationToken cancellationToken = default)
    {
        return await context.ProgressReports
            .AsNoTracking()
            .AnyAsync(r => r.ProgressReportPeriodId == cycleId, cancellationToken);
    }

    public async Task<long?> GetDefaultProjectPeriodIdAsync(long projectId, CancellationToken cancellationToken = default)
    {
        var project = await context.Projects
            .AsNoTracking()
            .Include(p => p.Team)
            .FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken);

        if (project is null) return null;

        var semesterId = project.Team.AcademicSemesterId;

        return await context.ProjectPeriods
            .AsNoTracking()
            .Where(pp => pp.AcademicSemesterId == semesterId && pp.Status == "ACTIVE")
            .Select(pp => (long?)pp.Id)
            .FirstOrDefaultAsync(cancellationToken)
            ?? await context.ProjectPeriods
                .AsNoTracking()
                .Where(pp => pp.AcademicSemesterId == semesterId)
                .Select(pp => (long?)pp.Id)
                .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<bool> IsValidProjectPeriodAsync(long projectId, long projectPeriodId, CancellationToken cancellationToken = default)
    {
        var project = await context.Projects
            .AsNoTracking()
            .Include(p => p.Team)
            .FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken);

        if (project is null) return false;

        return await context.ProjectPeriods
            .AsNoTracking()
            .AnyAsync(pp => pp.Id == projectPeriodId && pp.AcademicSemesterId == project.Team.AcademicSemesterId, cancellationToken);
    }

    private static ReportingCycleDto ToDto(ProgressReportPeriod p) =>
        new(
            p.Id,
            p.ProjectId,
            p.ProjectPeriodId,
            p.ReportType,
            p.PeriodStart,
            p.PeriodEnd,
            p.Deadline,
            p.LatePolicy,
            p.CreatedBy,
            p.CreatedAt,
            p.UpdatedAt,
            p.ConcurrencyToken.ToString("N"));

    public Task<bool> HasAdminRoleInDbAsync(long userId, CancellationToken cancellationToken = default)
        => context.UserRoles
            .AsNoTracking()
            .AnyAsync(ur => ur.UserId == userId && ur.User.Status == "ACTIVE" && ur.Role.Code == AppRoles.Admin, cancellationToken);

    public Task<bool> HasStaffRoleInDbAsync(long userId, CancellationToken cancellationToken = default)
        => context.UserRoles
            .AsNoTracking()
            .AnyAsync(ur => ur.UserId == userId && ur.User.Status == "ACTIVE" && ur.Role.Code == AppRoles.DepartmentStaff, cancellationToken);

    public async Task<(long ProjectId, string ReportType, DateTime PeriodStart, DateTime PeriodEnd)?> GetCycleHeaderAsync(
        long cycleId, CancellationToken cancellationToken = default)
    {
        var row = await context.ProgressReportPeriods
            .AsNoTracking()
            .Where(p => p.Id == cycleId)
            .Select(p => new { p.ProjectId, p.ReportType, p.PeriodStart, p.PeriodEnd })
            .FirstOrDefaultAsync(cancellationToken);
        return row is null ? null : (row.ProjectId, row.ReportType, row.PeriodStart, row.PeriodEnd);
    }
}
