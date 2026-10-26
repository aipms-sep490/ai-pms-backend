using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.ProgressReports.Abstractions;
using AIPMS.Application.Features.ProgressReports.DTOs;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Generated.Models;
using AIPMS.Infrastructure.Persistence.Mappers;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Task = System.Threading.Tasks.Task;

namespace AIPMS.Infrastructure.Persistence.Repositories;

public sealed class ProgressReportRepository(AipmsDbContext context) : IProgressReportRepository
{
    public async Task<ProgressReportDto?> GetByIdAsync(long id, CancellationToken cancellationToken)
    {
        var entity = await context.ProgressReports
            .AsNoTracking()
            .Include(r => r.SubmittedByNavigation)
            .Include(r => r.ProgressReportPeriod)
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

        return entity?.ToDto();
    }

    public async Task<ProgressReportDetailDto?> GetDetailByIdAsync(long id, CancellationToken cancellationToken)
    {
        var entity = await context.ProgressReports
            .AsNoTracking()
            .Include(r => r.SubmittedByNavigation)
            .Include(r => r.ProgressReportPeriod)
            .Include(r => r.SupervisorFeedbacks)
                .ThenInclude(f => f.SupervisorAssignment)
                    .ThenInclude(a => a.SupervisorProfile)
                        .ThenInclude(p => p.User)
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

        return entity?.ToDetailDto();
    }

    public async Task<PagedResult<ProgressReportDto>> GetReportsAsync(
        long projectId,
        string? reportType,
        string? status,
        DateOnly? from,
        DateOnly? to,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var query = context.ProgressReports
            .AsNoTracking()
            .Include(r => r.SubmittedByNavigation)
            .Include(r => r.ProgressReportPeriod)
            .Where(r => r.ProjectId == projectId);

        if (!string.IsNullOrWhiteSpace(reportType))
        {
            query = query.Where(r => r.ReportType == reportType);
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(r => r.Status == status);
        }

        if (from.HasValue)
        {
            query = query.Where(r => r.PeriodStart >= from.Value);
        }

        if (to.HasValue)
        {
            query = query.Where(r => r.PeriodEnd <= to.Value);
        }

        var totalCount = await query.LongCountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(r => r.PeriodStart)
            .ThenByDescending(r => r.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var dtos = items.Select(r => r.ToDto()).ToList();
        return new PagedResult<ProgressReportDto>(dtos, page, pageSize, totalCount);
    }

    public async Task<bool> ExistsForPeriodAsync(
        long projectId,
        string reportType,
        DateOnly periodStart,
        DateOnly periodEnd,
        long? excludeId,
        CancellationToken cancellationToken)
    {
        return await context.ProgressReports
            .AsNoTracking()
            .AnyAsync(r => r.ProjectId == projectId
                && r.ReportType == reportType
                && r.PeriodStart == periodStart
                && r.PeriodEnd == periodEnd
                && (!excludeId.HasValue || r.Id != excludeId.Value),
                cancellationToken);
    }

    public async Task<bool> ExistsForPeriodIdAsync(
        long periodId,
        long? excludeId = null,
        CancellationToken cancellationToken = default)
    {
        return await context.ProgressReports
            .AsNoTracking()
            .AnyAsync(r => r.ProgressReportPeriodId == periodId
                && (!excludeId.HasValue || r.Id != excludeId.Value),
                cancellationToken);
    }

    public async Task<ProgressReportDto> CreateAsync(
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
        CancellationToken cancellationToken) =>
        await CreateAsync(projectId, submittedBy, reportType, periodStart, periodEnd, summary, completedWork, plannedWork, issuesAndRisks, now, null, null, null, null, null, null, cancellationToken);

    public async Task<ProgressReportDto> CreateAsync(
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
        long? progressReportPeriodId,
        string? inProgressWork,
        string? blockers,
        string? risks,
        string? nextActions,
        Func<ProgressReportDto, Task>? onCreated,
        CancellationToken cancellationToken = default)
    {
        await using var tx = context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(cancellationToken)
            : null;

        if (progressReportPeriodId.HasValue)
        {
            var cycle = await context.ProgressReportPeriods
                .FromSqlInterpolated($"SELECT * FROM dbo.progress_report_periods WITH (UPDLOCK, HOLDLOCK) WHERE id = {progressReportPeriodId.Value}")
                .FirstOrDefaultAsync(cancellationToken)
                ?? throw new NotFoundException("ProgressReportPeriod", progressReportPeriodId.Value);

            if (cycle.ProjectId != projectId)
                throw new NotFoundException("ProgressReportPeriod", progressReportPeriodId.Value);

            if (!string.Equals(cycle.ReportType, reportType?.Trim().ToUpperInvariant(), StringComparison.OrdinalIgnoreCase))
                throw new ValidationException(new Dictionary<string, string[]>
                {
                    ["reportType"] = [$"Report type does not match the cycle's report type '{cycle.ReportType}'."]
                });

            var startDt = periodStart.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            var endDt = periodEnd.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

            if (startDt.Date != cycle.PeriodStart.Date)
                throw new ValidationException(new Dictionary<string, string[]>
                {
                    ["periodStart"] = ["PeriodStart does not match the reporting cycle's period start."]
                });

            if (endDt.Date != cycle.PeriodEnd.Date)
                throw new ValidationException(new Dictionary<string, string[]>
                {
                    ["periodEnd"] = ["PeriodEnd does not match the reporting cycle's period end."]
                });

            if (await ExistsForPeriodIdAsync(progressReportPeriodId.Value, null, cancellationToken))
                throw new ConflictException("A progress report has already been created for this reporting cycle.");
        }

        var entity = new ProgressReport
        {
            ProjectId = projectId,
            SubmittedBy = submittedBy,
            ReportType = reportType ?? "",
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            Summary = summary,
            CompletedWork = completedWork,
            PlannedWork = plannedWork,
            IssuesAndRisks = issuesAndRisks,
            ProgressReportPeriodId = progressReportPeriodId,
            InProgressWork = inProgressWork,
            Blockers = blockers ?? issuesAndRisks,
            Risks = risks,
            NextActions = nextActions ?? plannedWork,
            Status = "DRAFT",
            SubmittedAt = null,
            CreatedAt = now,
            UpdatedAt = now
        };

        context.ProgressReports.Add(entity);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            throw new ConflictException("A progress report for this project, type, and period already exists.");
        }

        var result = (await GetByIdAsync(entity.Id, cancellationToken))!;

        if (onCreated != null)
        {
            await onCreated(result);
        }

        if (tx is not null)
        {
            await tx.CommitAsync(cancellationToken);
        }

        return result;
    }

    public async Task<ProgressReportDto> UpdateAsync(
        long id,
        string summary,
        string? completedWork,
        string? plannedWork,
        string? issuesAndRisks,
        DateTime now,
        CancellationToken cancellationToken) =>
        await UpdateAsync(id, summary, completedWork, plannedWork, issuesAndRisks, now, null, null, null, null, null, null, cancellationToken);

    public async Task<ProgressReportDto> UpdateAsync(
        long id,
        string summary,
        string? completedWork,
        string? plannedWork,
        string? issuesAndRisks,
        DateTime now,
        long? progressReportPeriodId,
        string? inProgressWork,
        string? blockers,
        string? risks,
        string? nextActions,
        Func<ProgressReportDto, Task>? onUpdated,
        CancellationToken cancellationToken = default)
    {
        await using var tx = context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(cancellationToken)
            : null;

        var entity = await context.ProgressReports
            .FromSqlInterpolated($"SELECT * FROM dbo.progress_reports WITH (UPDLOCK, ROWLOCK) WHERE id = {id}")
            .AsTracking()
            .FirstOrDefaultAsync(cancellationToken);

        if (entity is null)
            throw new NotFoundException("ProgressReport", id);

        if (entity.Status != "DRAFT")
            throw new ConflictException("Submitted or reviewed progress reports cannot be modified.");

        if (progressReportPeriodId.HasValue && progressReportPeriodId.Value != entity.ProgressReportPeriodId)
        {
            var cycle = await context.ProgressReportPeriods
                .FromSqlInterpolated($"SELECT * FROM dbo.progress_report_periods WITH (UPDLOCK, HOLDLOCK) WHERE id = {progressReportPeriodId.Value}")
                .FirstOrDefaultAsync(cancellationToken)
                ?? throw new NotFoundException("ProgressReportPeriod", progressReportPeriodId.Value);

            if (cycle.ProjectId != entity.ProjectId)
                throw new NotFoundException("ProgressReportPeriod", progressReportPeriodId.Value);

            if (!string.Equals(cycle.ReportType, entity.ReportType, StringComparison.OrdinalIgnoreCase))
                throw new ValidationException(new Dictionary<string, string[]>
                {
                    ["reportType"] = [$"Report type does not match the cycle's report type '{cycle.ReportType}'."]
                });

            var startDt = entity.PeriodStart.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            var endDt = entity.PeriodEnd.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

            if (startDt.Date != cycle.PeriodStart.Date)
                throw new ValidationException(new Dictionary<string, string[]>
                {
                    ["periodStart"] = ["PeriodStart does not match the reporting cycle's period start."]
                });

            if (endDt.Date != cycle.PeriodEnd.Date)
                throw new ValidationException(new Dictionary<string, string[]>
                {
                    ["periodEnd"] = ["PeriodEnd does not match the reporting cycle's period end."]
                });

            if (await ExistsForPeriodIdAsync(progressReportPeriodId.Value, id, cancellationToken))
                throw new ConflictException("A progress report has already been created for this reporting cycle.");

            entity.ProgressReportPeriodId = progressReportPeriodId.Value;
        }

        entity.Summary = summary;
        entity.CompletedWork = completedWork;
        entity.PlannedWork = plannedWork;
        entity.IssuesAndRisks = issuesAndRisks;
        if (inProgressWork != null) entity.InProgressWork = inProgressWork;
        if (blockers != null) entity.Blockers = blockers;
        if (risks != null) entity.Risks = risks;
        if (nextActions != null) entity.NextActions = nextActions;
        entity.UpdatedAt = now;
        entity.ConcurrencyToken = Guid.NewGuid();

        await context.SaveChangesAsync(cancellationToken);

        var result = (await GetByIdAsync(id, cancellationToken))!;

        if (onUpdated != null)
        {
            await onUpdated(result);
        }

        if (tx is not null)
        {
            await tx.CommitAsync(cancellationToken);
        }

        return result;
    }

    public async Task<ProgressReportDto> SubmitAsync(
        long id,
        long actorId,
        DateTime now,
        CancellationToken cancellationToken) =>
        await SubmitAsync(id, actorId, now, null, cancellationToken);

    public async Task<ProgressReportDto> SubmitAsync(
        long id,
        long actorId,
        DateTime now,
        Func<ProgressReportDto, Task>? onSubmitted,
        CancellationToken cancellationToken = default)
    {
        await using var tx = context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(cancellationToken)
            : null;

        var entity = await context.ProgressReports
            .FromSqlInterpolated($"SELECT * FROM dbo.progress_reports WITH (UPDLOCK, ROWLOCK) WHERE id = {id}")
            .FirstOrDefaultAsync(cancellationToken);

        if (entity is null)
            throw new NotFoundException("ProgressReport", id);

        if (entity.Status != "DRAFT")
            throw new ConflictException("Progress report is already submitted.");

        if (!entity.ProgressReportPeriodId.HasValue)
            throw new ConflictException("Progress report must be linked to a reporting cycle before submission.");

        var cycle = await context.ProgressReportPeriods
            .FromSqlInterpolated($"SELECT * FROM dbo.progress_report_periods WITH (UPDLOCK, HOLDLOCK) WHERE id = {entity.ProgressReportPeriodId.Value}")
            .FirstOrDefaultAsync(cancellationToken);

        if (cycle is null)
            throw new NotFoundException("ProgressReportPeriod", entity.ProgressReportPeriodId.Value);

        var isLate = now > cycle.Deadline;
        if (isLate)
        {
            if (string.Equals(cycle.LatePolicy, "BLOCK", StringComparison.OrdinalIgnoreCase))
            {
                throw new ConflictException("Submission deadline has passed for this reporting cycle.");
            }
            entity.IsLate = true;
        }
        else
        {
            entity.IsLate = false;
        }

        // All five canonical sections are required to submit under the new contract.
        // Legacy fields (planned_work, issues_and_risks) are read-only compatibility columns;
        // they must NOT be silently promoted into canonical fields at submit time.
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(entity.Summary))
            errors["summary"] = ["Summary is required to submit a progress report."];
        if (string.IsNullOrWhiteSpace(entity.CompletedWork))
            errors["completedWork"] = ["Completed work is required to submit a progress report."];
        if (string.IsNullOrWhiteSpace(entity.InProgressWork))
            errors["inProgressWork"] = ["In-progress work is required to submit a progress report."];
        if (string.IsNullOrWhiteSpace(entity.Blockers))
            errors["blockers"] = ["Blockers is required to submit a progress report."];
        if (string.IsNullOrWhiteSpace(entity.Risks))
            errors["risks"] = ["Risks is required to submit a progress report."];
        if (string.IsNullOrWhiteSpace(entity.NextActions))
            errors["nextActions"] = ["Next actions is required to submit a progress report."];

        if (errors.Count > 0)
            throw new ValidationException(errors);

        entity.Status = "SUBMITTED";
        entity.SubmittedBy = actorId;
        entity.SubmittedAt = now;
        entity.UpdatedAt = now;
        entity.ConcurrencyToken = Guid.NewGuid();

        await context.SaveChangesAsync(cancellationToken);

        var result = (await GetByIdAsync(id, cancellationToken))!;

        if (onSubmitted != null)
        {
            await onSubmitted(result);
        }

        if (tx is not null)
        {
            await tx.CommitAsync(cancellationToken);
        }

        return result;
    }

    public async Task<ProgressReportFeedbackDto> AddFeedbackAsync(
        long reportId,
        long supervisorAssignmentId,
        string feedbackText,
        DateTime now,
        CancellationToken cancellationToken) =>
        await AddFeedbackAsync(reportId, supervisorAssignmentId, feedbackText, now, null, cancellationToken);

    public async Task<ProgressReportFeedbackDto> AddFeedbackAsync(
        long reportId,
        long supervisorAssignmentId,
        string feedbackText,
        DateTime now,
        Func<ProgressReportFeedbackDto, Task>? onAdded,
        CancellationToken cancellationToken = default)
    {
        await using var tx = context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(cancellationToken)
            : null;

        var report = await context.ProgressReports
            .FromSqlInterpolated($"SELECT * FROM dbo.progress_reports WITH (UPDLOCK, ROWLOCK) WHERE id = {reportId}")
            .AsTracking()
            .FirstOrDefaultAsync(cancellationToken);

        if (report is null)
            throw new NotFoundException("ProgressReport", reportId);

        if (report.Status == "DRAFT")
            throw new ConflictException("Cannot provide feedback on a draft progress report.");

        var feedback = new SupervisorFeedback
        {
            ProjectId = report.ProjectId,
            SupervisorAssignmentId = supervisorAssignmentId,
            ProgressReportId = reportId,
            FeedbackText = feedbackText,
            CreatedAt = now,
            UpdatedAt = now
        };

        context.SupervisorFeedbacks.Add(feedback);
        report.Status = "REVIEWED";
        report.UpdatedAt = now;
        report.ConcurrencyToken = Guid.NewGuid();

        await context.SaveChangesAsync(cancellationToken);

        var reloaded = await context.SupervisorFeedbacks
            .AsNoTracking()
            .Include(f => f.SupervisorAssignment)
                .ThenInclude(a => a.SupervisorProfile)
                    .ThenInclude(p => p.User)
            .FirstAsync(f => f.Id == feedback.Id, cancellationToken);

        var result = reloaded.ToDto();

        if (onAdded != null)
        {
            await onAdded(result);
        }

        if (tx is not null)
        {
            await tx.CommitAsync(cancellationToken);
        }

        return result;
    }

    public async Task<long?> GetProjectIdAsync(long reportId, CancellationToken cancellationToken)
    {
        return await context.ProgressReports
            .AsNoTracking()
            .Where(r => r.Id == reportId)
            .Select(r => (long?)r.ProjectId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<string?> GetStatusAsync(long reportId, CancellationToken cancellationToken)
    {
        return await context.ProgressReports
            .AsNoTracking()
            .Where(r => r.Id == reportId)
            .Select(r => r.Status)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<bool> ProjectExistsAsync(long projectId, CancellationToken cancellationToken)
    {
        return await context.Projects
            .AsNoTracking()
            .AnyAsync(p => p.Id == projectId, cancellationToken);
    }

    public async Task<bool> IsTeamLeaderAsync(long projectId, long userId, CancellationToken cancellationToken)
    {
        return await context.Projects
            .AsNoTracking()
            .Where(p => p.Id == projectId)
            .SelectMany(p => p.Team.TeamMembers)
            .AnyAsync(m => m.UserId == userId && m.IsLeader && m.LeftAt == null, cancellationToken);
    }

    public async Task<bool> IsActiveTeamMemberAsync(long projectId, long userId, CancellationToken cancellationToken)
    {
        return await context.Projects
            .AsNoTracking()
            .Where(p => p.Id == projectId)
            .SelectMany(p => p.Team.TeamMembers)
            .AnyAsync(m => m.UserId == userId && m.LeftAt == null, cancellationToken);
    }

    public async Task<long?> GetActiveSupervisorAssignmentIdAsync(long projectId, long supervisorUserId, CancellationToken cancellationToken)
    {
        return await context.SupervisorAssignments
            .AsNoTracking()
            .Where(a => a.ProjectId == projectId
                && a.EndedAt == null
                && a.SupervisorProfile.UserId == supervisorUserId)
            .Select(a => (long?)a.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex) =>
        ex.InnerException is SqlException { Number: 2601 or 2627 };

    public async Task<ProgressReportPrefillDto> GetPrefillDataAsync(
        long projectId,
        long? progressReportPeriodId,
        DateOnly periodStart,
        DateOnly periodEnd,
        string reportType,
        CancellationToken cancellationToken)
    {
        var startUtc = periodStart.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var endUtc = periodEnd.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);

        var tasks = await context.Tasks
            .AsNoTracking()
            .Where(t => t.Milestone.ProjectId == projectId)
            .Select(t => new
            {
                t.Id,
                t.Title,
                t.Status,
                t.CompletedAt,
                t.UpdatedAt,
                t.DueAt
            })
            .ToListAsync(cancellationToken);

        // 1. Done/Completed tasks in period with deterministic sorting
        var completedTasks = tasks.Where(t =>
            (t.Status == "DONE" || t.Status == "COMPLETED") &&
            ((t.CompletedAt.HasValue && t.CompletedAt.Value >= startUtc && t.CompletedAt.Value <= endUtc)
             || (!t.CompletedAt.HasValue && t.UpdatedAt >= startUtc && t.UpdatedAt <= endUtc)))
            .OrderByDescending(t => t.CompletedAt ?? t.UpdatedAt)
            .ThenByDescending(t => t.Id)
            .ToList();

        // 2. In progress & blocked tasks with deterministic sorting
        var inProgressTasks = tasks.Where(t => t.Status == "IN_PROGRESS")
            .OrderBy(t => t.DueAt.HasValue ? 0 : 1)
            .ThenBy(t => t.DueAt)
            .ThenBy(t => t.Id)
            .ToList();

        var blockedTasks = tasks.Where(t => t.Status == "BLOCKED")
            .OrderBy(t => t.DueAt.HasValue ? 0 : 1)
            .ThenBy(t => t.DueAt)
            .ThenBy(t => t.Id)
            .ToList();

        var nextTasks = tasks.Where(t => t.Status == "TODO" || t.Status == "IN_PROGRESS")
            .OrderBy(t => t.DueAt.HasValue ? 0 : 1)
            .ThenBy(t => t.DueAt)
            .ThenBy(t => t.Id)
            .Take(5)
            .ToList();

        // 3. Query meetings in period with deterministic sorting
        var meetings = await context.Meetings
            .AsNoTracking()
            .Where(m => m.ProjectId == projectId && m.StartAt >= startUtc && m.StartAt <= endUtc)
            .OrderBy(m => m.StartAt)
            .ThenBy(m => m.Id)
            .Select(m => new
            {
                m.Id,
                m.Title,
                m.Status,
                m.StartAt
            })
            .ToListAsync(cancellationToken);

        // 4. Query risk history in period (if any)
        var risks = await context.Set<RiskHistory>()
            .AsNoTracking()
            .Where(r => r.ProjectId == projectId && r.RecordedAt >= startUtc && r.RecordedAt <= endUtc)
            .OrderByDescending(r => r.RecordedAt)
            .ThenByDescending(r => r.Id)
            .Take(5)
            .ToListAsync(cancellationToken);

        var evidenceList = new List<ProgressReportPrefillEvidenceDto>();
        foreach (var t in completedTasks)
        {
            evidenceList.Add(new ProgressReportPrefillEvidenceDto("TASK", t.Id, t.Title, t.Status));
        }
        foreach (var t in inProgressTasks)
        {
            evidenceList.Add(new ProgressReportPrefillEvidenceDto("TASK", t.Id, t.Title, t.Status));
        }
        foreach (var t in blockedTasks)
        {
            evidenceList.Add(new ProgressReportPrefillEvidenceDto("TASK", t.Id, t.Title, t.Status));
        }
        foreach (var m in meetings)
        {
            evidenceList.Add(new ProgressReportPrefillEvidenceDto("MEETING", m.Id, m.Title, m.Status));
        }
        foreach (var r in risks)
        {
            evidenceList.Add(new ProgressReportPrefillEvidenceDto("RISK", r.Id, $"{r.RiskLevel} Risk ({r.RiskScore})", r.RiskLevel));
        }

        var insufficientData = evidenceList.Count == 0;

        string? completedText = completedTasks.Count > 0
            ? string.Join("\n", completedTasks.Select(t => $"- {t.Title}"))
            : null;

        string? inProgressText = inProgressTasks.Count > 0
            ? string.Join("\n", inProgressTasks.Select(t => $"- {t.Title}"))
            : null;

        string? blockersText = blockedTasks.Count > 0
            ? string.Join("\n", blockedTasks.Select(t => $"- {t.Title} (BLOCKED)"))
            : null;

        string? nextActionsText = nextTasks.Count > 0
            ? string.Join("\n", nextTasks.Select(t => $"- {t.Title}"))
            : null;

        string? risksText = risks.Count > 0
            ? string.Join("\n", risks.Select(r => $"- {r.RiskLevel}: {r.OverdueTasks} quá hạn, {r.BlockedTasks} bị chặn (Điểm rủi ro: {r.RiskScore})"))
            : null;

        string? summaryText = (completedTasks.Count > 0 || meetings.Count > 0)
            ? $"Trong kỳ báo cáo, nhóm đã hoàn thành {completedTasks.Count} công việc và tổ chức {meetings.Count} cuộc họp."
            : null;

        var suggested = new ProgressReportSuggestedContentDto(
            CompletedWork: completedText,
            InProgressWork: inProgressText,
            Blockers: blockersText,
            Risks: risksText,
            NextActions: nextActionsText,
            Summary: summaryText);

        return new ProgressReportPrefillDto(
            progressReportPeriodId,
            reportType,
            periodStart,
            periodEnd,
            suggested,
            evidenceList,
            insufficientData);
    }
}
