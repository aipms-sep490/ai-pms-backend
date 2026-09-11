using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.ProgressReports.Abstractions;
using AIPMS.Application.Features.ProgressReports.DTOs;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Generated.Models;
using AIPMS.Infrastructure.Persistence.Mappers;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Persistence.Repositories;

public sealed class ProgressReportRepository(AipmsDbContext context) : IProgressReportRepository
{
    public async Task<ProgressReportDto?> GetByIdAsync(long id, CancellationToken cancellationToken)
    {
        var entity = await context.ProgressReports
            .AsNoTracking()
            .Include(r => r.SubmittedByNavigation)
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

        return entity?.ToDto();
    }

    public async Task<ProgressReportDetailDto?> GetDetailByIdAsync(long id, CancellationToken cancellationToken)
    {
        var entity = await context.ProgressReports
            .AsNoTracking()
            .Include(r => r.SubmittedByNavigation)
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
        CancellationToken cancellationToken)
    {
        var entity = new ProgressReport
        {
            ProjectId = projectId,
            SubmittedBy = submittedBy,
            ReportType = reportType,
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            Summary = summary,
            CompletedWork = completedWork,
            PlannedWork = plannedWork,
            IssuesAndRisks = issuesAndRisks,
            Status = "DRAFT",
            SubmittedAt = null,
            CreatedAt = now,
            UpdatedAt = now
        };

        context.ProgressReports.Add(entity);
        await context.SaveChangesAsync(cancellationToken);

        return (await GetByIdAsync(entity.Id, cancellationToken))!;
    }

    public async Task<ProgressReportDto> UpdateAsync(
        long id,
        string summary,
        string? completedWork,
        string? plannedWork,
        string? issuesAndRisks,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var entity = await context.ProgressReports
            .FirstAsync(r => r.Id == id, cancellationToken);

        entity.Summary = summary;
        entity.CompletedWork = completedWork;
        entity.PlannedWork = plannedWork;
        entity.IssuesAndRisks = issuesAndRisks;
        entity.UpdatedAt = now;

        await context.SaveChangesAsync(cancellationToken);
        return (await GetByIdAsync(id, cancellationToken))!;
    }

    public async Task<ProgressReportDto> SubmitAsync(
        long id,
        long actorId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var entity = await context.ProgressReports
            .FirstAsync(r => r.Id == id, cancellationToken);

        entity.Status = "SUBMITTED";
        entity.SubmittedBy = actorId;
        entity.SubmittedAt = now;
        entity.UpdatedAt = now;

        // In-app notification for active assigned supervisors
        var reviewers = await context.SupervisorAssignments
            .AsNoTracking()
            .Where(a => a.ProjectId == entity.ProjectId && a.EndedAt == null)
            .Select(a => a.SupervisorProfile.UserId)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (reviewers.Count > 0)
        {
            var notification = new Notification
            {
                CreatedBy = actorId,
                NotificationType = "PROGRESS_REPORT_SUBMITTED",
                Title = "Progress report submitted",
                Content = $"A new {entity.ReportType} progress report for project #{entity.ProjectId} has been submitted.",
                RelatedEntityType = "PROGRESS_REPORT",
                RelatedEntityId = entity.Id,
                CreatedAt = now,
                UpdatedAt = now,
                NotificationRecipients = reviewers.Select(u => new NotificationRecipient
                {
                    UserId = u,
                    IsRead = false,
                    CreatedAt = now,
                    UpdatedAt = now
                }).ToList()
            };
            context.Notifications.Add(notification);
        }

        await context.SaveChangesAsync(cancellationToken);
        return (await GetByIdAsync(id, cancellationToken))!;
    }

    public async Task<ProgressReportFeedbackDto> AddFeedbackAsync(
        long reportId,
        long supervisorAssignmentId,
        string feedbackText,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var report = await context.ProgressReports
            .FirstAsync(r => r.Id == reportId, cancellationToken);

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

        await context.SaveChangesAsync(cancellationToken);

        var reloaded = await context.SupervisorFeedbacks
            .AsNoTracking()
            .Include(f => f.SupervisorAssignment)
                .ThenInclude(a => a.SupervisorProfile)
                    .ThenInclude(p => p.User)
            .FirstAsync(f => f.Id == feedback.Id, cancellationToken);

        return reloaded.ToDto();
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
}
