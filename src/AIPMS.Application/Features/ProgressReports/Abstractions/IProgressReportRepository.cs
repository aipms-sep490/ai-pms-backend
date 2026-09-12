using System;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.ProgressReports.DTOs;

namespace AIPMS.Application.Features.ProgressReports.Abstractions;

public interface IProgressReportRepository
{
    Task<ProgressReportDto?> GetByIdAsync(long id, CancellationToken cancellationToken);

    Task<ProgressReportDetailDto?> GetDetailByIdAsync(long id, CancellationToken cancellationToken);

    Task<PagedResult<ProgressReportDto>> GetReportsAsync(
        long projectId,
        string? reportType,
        string? status,
        DateOnly? from,
        DateOnly? to,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    Task<bool> ExistsForPeriodAsync(
        long projectId,
        string reportType,
        DateOnly periodStart,
        DateOnly periodEnd,
        long? excludeId,
        CancellationToken cancellationToken);

    Task<ProgressReportDto> CreateAsync(
        long projectId,
        long submittedBy,
        string reportType,
        DateOnly periodStart,
        DateOnly periodEnd,
        string summary,
        string? completedWork,
        string? plannedWork,
        string? issuesAndRisks,
        DateTime now,
        CancellationToken cancellationToken);

    Task<ProgressReportDto> UpdateAsync(
        long id,
        string summary,
        string? completedWork,
        string? plannedWork,
        string? issuesAndRisks,
        DateTime now,
        CancellationToken cancellationToken);

    Task<ProgressReportDto> SubmitAsync(
        long id,
        long actorId,
        DateTime now,
        Func<ProgressReportDto, Task>? onSubmitted = null,
        CancellationToken cancellationToken = default);

    Task<ProgressReportFeedbackDto> AddFeedbackAsync(
        long reportId,
        long supervisorAssignmentId,
        string feedbackText,
        DateTime now,
        CancellationToken cancellationToken);

    Task<long?> GetProjectIdAsync(long reportId, CancellationToken cancellationToken);

    Task<string?> GetStatusAsync(long reportId, CancellationToken cancellationToken);

    Task<bool> ProjectExistsAsync(long projectId, CancellationToken cancellationToken);

    Task<bool> IsTeamLeaderAsync(long projectId, long userId, CancellationToken cancellationToken);

    Task<bool> IsActiveTeamMemberAsync(long projectId, long userId, CancellationToken cancellationToken);

    Task<long?> GetActiveSupervisorAssignmentIdAsync(long projectId, long supervisorUserId, CancellationToken cancellationToken);
}
