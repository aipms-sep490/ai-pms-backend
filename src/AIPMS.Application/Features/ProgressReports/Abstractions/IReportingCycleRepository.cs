using System;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.ProgressReports.DTOs;

namespace AIPMS.Application.Features.ProgressReports.Abstractions;

public interface IReportingCycleRepository
{
    Task<ReportingCycleDto?> GetByIdAsync(long id, CancellationToken cancellationToken = default);

    Task<PagedResult<ReportingCycleDto>> ListAsync(
        long projectId,
        string? reportType,
        DateTime? from,
        DateTime? to,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<bool> ExistsOverlapAsync(
        long projectId,
        string reportType,
        DateTime periodStart,
        DateTime periodEnd,
        long? excludeId = null,
        CancellationToken cancellationToken = default);

    Task<ReportingCycleDto> CreateAsync(
        long projectId,
        long projectPeriodId,
        string reportType,
        DateTime periodStart,
        DateTime periodEnd,
        DateTime deadline,
        string latePolicy,
        long createdBy,
        DateTime now,
        CancellationToken cancellationToken = default);

    Task<ReportingCycleDto> UpdateAsync(
        long id,
        DateTime? periodStart,
        DateTime? periodEnd,
        DateTime? deadline,
        string? latePolicy,
        Guid? expectedToken,
        DateTime now,
        CancellationToken cancellationToken = default);

    Task<bool> HasLinkedReportAsync(long cycleId, CancellationToken cancellationToken = default);

    Task<long?> GetDefaultProjectPeriodIdAsync(long projectId, CancellationToken cancellationToken = default);

    Task<bool> IsValidProjectPeriodAsync(long projectId, long projectPeriodId, CancellationToken cancellationToken = default);
}
