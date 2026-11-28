using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Projects.Abstractions;
using AIPMS.Infrastructure.Persistence.Generated;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Services.Projects;

internal sealed class ExecutionWindowService(AipmsDbContext context) : IExecutionWindowService
{
    // Milestones use calendar dates; task/period instants are stored as UTC.
    // Match the Vietnam calendar used by the execution forms and schedule services.
    private static DateOnly AcademicDate(DateTime utc) =>
        DateOnly.FromDateTime(DateTime.SpecifyKind(utc, DateTimeKind.Utc).AddHours(7));

    public async Task<ExecutionWindowInfo?> GetExecutionWindowAsync(long projectId, CancellationToken ct = default)
    {
        // 1. Check if the project is associated with an explicit execution period via registration snapshot
        var snapshotPeriodId = await context.Set<AIPMS.Infrastructure.Persistence.Models.ProjectRegistrationSnapshot>()
            .AsNoTracking()
            .Where(s => s.ProjectId == projectId)
            .OrderByDescending(s => s.Id)
            .Select(s => (long?)s.ProjectPeriodId)
            .FirstOrDefaultAsync(ct);

        if (snapshotPeriodId.HasValue)
        {
            var p = await context.ProjectPeriods
                .AsNoTracking()
                .Where(x => x.Id == snapshotPeriodId.Value && x.PeriodType == "EXECUTION" && x.Status != "ARCHIVED" && x.EndAt >= x.StartAt)
                .FirstOrDefaultAsync(ct);
            if (p != null)
            {
                return new ExecutionWindowInfo(p.Id, p.Code, p.Name, p.StartAt, p.EndAt);
            }
        }

        // 2. Check explicit execution period via project topic
        var topicPeriodId = await context.Projects
            .AsNoTracking()
            .Where(p => p.Id == projectId && p.TopicId != null)
            .Join(context.Set<AIPMS.Infrastructure.Persistence.Models.ProjectTopic>(), p => p.TopicId, t => t.Id, (p, t) => (long?)t.ProjectPeriodId)
            .FirstOrDefaultAsync(ct);

        if (topicPeriodId.HasValue)
        {
            var p = await context.ProjectPeriods
                .AsNoTracking()
                .Where(x => x.Id == topicPeriodId.Value && x.PeriodType == "EXECUTION" && x.Status != "ARCHIVED" && x.EndAt >= x.StartAt)
                .FirstOrDefaultAsync(ct);
            if (p != null)
            {
                return new ExecutionWindowInfo(p.Id, p.Code, p.Name, p.StartAt, p.EndAt);
            }
        }

        // 3. Check explicit execution period via existing progress report periods
        var cyclePeriodId = await context.ProgressReportPeriods
            .AsNoTracking()
            .Where(pr => pr.ProjectId == projectId)
            .OrderByDescending(pr => pr.Id)
            .Select(pr => (long?)pr.ProjectPeriodId)
            .FirstOrDefaultAsync(ct);

        if (cyclePeriodId.HasValue)
        {
            var p = await context.ProjectPeriods
                .AsNoTracking()
                .Where(x => x.Id == cyclePeriodId.Value && x.PeriodType == "EXECUTION" && x.Status != "ARCHIVED" && x.EndAt >= x.StartAt)
                .FirstOrDefaultAsync(ct);
            if (p != null)
            {
                return new ExecutionWindowInfo(p.Id, p.Code, p.Name, p.StartAt, p.EndAt);
            }
        }

        // 4. Semester-level fallback: carefully resolve among execution periods in the project's semester
        var projectInfo = await context.Projects
            .AsNoTracking()
            .Where(p => p.Id == projectId)
            .Select(p => new { SemesterId = (long?)p.Team.AcademicSemesterId, RegisteredAt = p.RegisteredAt })
            .FirstOrDefaultAsync(ct);

        if (projectInfo?.SemesterId is null || projectInfo.SemesterId == 0) return null;

        var semId = projectInfo.SemesterId.Value;
        var regAt = projectInfo.RegisteredAt;

        var period = await context.ProjectPeriods
            .AsNoTracking()
            .Where(p => p.AcademicSemesterId == semId
                && p.PeriodType == "EXECUTION"
                && p.Status != "ARCHIVED"
                && p.EndAt >= p.StartAt)
            .OrderByDescending(p => p.Status == "ACTIVE" ? 1 : 0)
            .ThenByDescending(p => (regAt >= p.StartAt && regAt <= p.EndAt) ? 1 : 0)
            .ThenByDescending(p => p.StartAt)
            .FirstOrDefaultAsync(ct);

        if (period is null) return null;

        return new ExecutionWindowInfo(period.Id, period.Code, period.Name, period.StartAt, period.EndAt);
    }

    public async Task ValidateTaskDatesAsync(
        long projectId,
        long milestoneId,
        DateTime? startAt,
        DateTime? dueAt,
        CancellationToken ct = default)
    {
        if (startAt.HasValue && dueAt.HasValue && dueAt.Value < startAt.Value)
        {
            throw new ConflictException(
                "Hạn hoàn thành công việc không được sớm hơn ngày bắt đầu.",
                WorkflowErrorCodes.TaskOutsideMilestone);
        }

        var milestone = await context.Milestones
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == milestoneId && m.ProjectId == projectId, ct)
            ?? throw new ConflictException(
                "Mốc đồ án đã chọn không thuộc đồ án này.",
                WorkflowErrorCodes.MilestoneNotInProject);

        // Milestone date boundaries
        if (milestone.StartDate.HasValue)
        {
            var mStart = milestone.StartDate.Value;
            if (startAt.HasValue && AcademicDate(startAt.Value) < mStart)
            {
                throw new ConflictException(
                    $"Ngày bắt đầu công việc ({AcademicDate(startAt.Value):dd/MM/yyyy}) không được sớm hơn ngày bắt đầu của mốc ({mStart:dd/MM/yyyy}).",
                    WorkflowErrorCodes.TaskOutsideMilestone);
            }

            if (dueAt.HasValue && AcademicDate(dueAt.Value) < mStart)
            {
                throw new ConflictException(
                    $"Hạn hoàn thành công việc ({AcademicDate(dueAt.Value):dd/MM/yyyy}) không được sớm hơn ngày bắt đầu của mốc ({mStart:dd/MM/yyyy}).",
                    WorkflowErrorCodes.TaskOutsideMilestone);
            }
        }

        if (milestone.DueDate.HasValue)
        {
            var mDue = milestone.DueDate.Value;
            if (startAt.HasValue && AcademicDate(startAt.Value) > mDue)
            {
                throw new ConflictException(
                    $"Ngày bắt đầu công việc ({AcademicDate(startAt.Value):dd/MM/yyyy}) không được muộn hơn hạn của mốc ({mDue:dd/MM/yyyy}).",
                    WorkflowErrorCodes.TaskOutsideMilestone);
            }

            if (dueAt.HasValue && AcademicDate(dueAt.Value) > mDue)
            {
                throw new ConflictException(
                    $"Hạn hoàn thành công việc ({AcademicDate(dueAt.Value):dd/MM/yyyy}) không được muộn hơn hạn của mốc ({mDue:dd/MM/yyyy}).",
                    WorkflowErrorCodes.TaskOutsideMilestone);
            }
        }

        // Project Period Execution Window boundaries: validated whenever startAt or dueAt is specified
        if (startAt.HasValue || dueAt.HasValue)
        {
            var window = await GetExecutionWindowAsync(projectId, ct)
                ?? throw new ConflictException(
                    "Đồ án hiện chưa có đợt thực hiện đang mở trong học kỳ.",
                    WorkflowErrorCodes.ExecutionWindowRequired);

            var winStart = AcademicDate(window.StartAt);
            var winEnd = AcademicDate(window.EndAt);

            if (startAt.HasValue && (startAt.Value < window.StartAt || startAt.Value > window.EndAt))
            {
                throw new ConflictException(
                    $"Ngày bắt đầu công việc ({AcademicDate(startAt.Value):dd/MM/yyyy}) phải nằm trong đợt thực hiện ({winStart:dd/MM/yyyy} – {winEnd:dd/MM/yyyy}).",
                    WorkflowErrorCodes.TaskOutsideExecutionWindow);
            }

            if (dueAt.HasValue && (dueAt.Value < window.StartAt || dueAt.Value > window.EndAt))
            {
                throw new ConflictException(
                    $"Hạn hoàn thành công việc ({AcademicDate(dueAt.Value):dd/MM/yyyy}) phải nằm trong đợt thực hiện ({winStart:dd/MM/yyyy} – {winEnd:dd/MM/yyyy}).",
                    WorkflowErrorCodes.TaskOutsideExecutionWindow);
            }
        }
    }

    public async Task ValidateMilestoneDatesAsync(
        long projectId,
        DateOnly? startDate,
        DateOnly? dueDate,
        long? excludeMilestoneId = null,
        CancellationToken ct = default)
    {
        if (startDate.HasValue && dueDate.HasValue && dueDate.Value < startDate.Value)
        {
            throw new ConflictException(
                "Hạn hoàn thành của mốc không được sớm hơn ngày bắt đầu.",
                WorkflowErrorCodes.MilestoneOutsideExecutionWindow);
        }

        if (startDate.HasValue || dueDate.HasValue)
        {
            var window = await GetExecutionWindowAsync(projectId, ct)
                ?? throw new ConflictException(
                    "Project does not have an active execution window in its academic semester.",
                    WorkflowErrorCodes.ExecutionWindowRequired);

            var winStart = AcademicDate(window.StartAt);
            var winEnd = AcademicDate(window.EndAt);

            if (startDate.HasValue && (startDate.Value < winStart || startDate.Value > winEnd))
            {
                throw new ConflictException(
                    $"Ngày bắt đầu mốc phải nằm trong đợt thực hiện ({winStart:dd/MM/yyyy} – {winEnd:dd/MM/yyyy}).",
                    WorkflowErrorCodes.MilestoneOutsideExecutionWindow);
            }

            if (dueDate.HasValue && (dueDate.Value < winStart || dueDate.Value > winEnd))
            {
                throw new ConflictException(
                    $"Hạn hoàn thành của mốc phải nằm trong đợt thực hiện ({winStart:dd/MM/yyyy} – {winEnd:dd/MM/yyyy}).",
                    WorkflowErrorCodes.MilestoneOutsideExecutionWindow);
            }
        }
    }

    public async Task ValidateMilestoneDateUpdateImpactAsync(
        long milestoneId,
        DateOnly? newStartDate,
        DateOnly? newDueDate,
        CancellationToken ct = default)
    {
        // 1. Check impact on child tasks
        var tasks = await context.Tasks
            .AsNoTracking()
            .Where(t => t.MilestoneId == milestoneId)
            .Select(t => new { t.Id, t.Title, t.StartAt, t.DueAt })
            .ToListAsync(ct);

        var invalidTasks = new List<string>();
        foreach (var t in tasks)
        {
            bool invalid = false;
            if (newStartDate.HasValue)
            {
                if (t.StartAt.HasValue && AcademicDate(t.StartAt.Value) < newStartDate.Value) invalid = true;
                if (t.DueAt.HasValue && AcademicDate(t.DueAt.Value) < newStartDate.Value) invalid = true;
            }
            if (newDueDate.HasValue)
            {
                if (t.StartAt.HasValue && AcademicDate(t.StartAt.Value) > newDueDate.Value) invalid = true;
                if (t.DueAt.HasValue && AcademicDate(t.DueAt.Value) > newDueDate.Value) invalid = true;
            }
            if (invalid)
            {
                var dueStr = t.DueAt.HasValue ? AcademicDate(t.DueAt.Value).ToString("dd/MM/yyyy") : "chưa đặt hạn";
                invalidTasks.Add($"#{t.Id} “{t.Title}” (hạn: {dueStr})");
            }
        }

        if (invalidTasks.Count > 0)
        {
            throw new ConflictException(
                $"Không thể cập nhật ngày mốc vì có {invalidTasks.Count} công việc con nằm ngoài khoảng ngày mới: {string.Join(", ", invalidTasks.Take(5))}.",
                WorkflowErrorCodes.ParentDateConflict);
        }

        // 2. Revalidate incoming dependencies (where this milestone is successor)
        var incomingDeps = await context.MilestoneDependencies
            .AsNoTracking()
            .Where(d => d.MilestoneId == milestoneId && d.DependencyType == "FINISH_TO_START")
            .Include(d => d.DependsOnMilestone)
            .ToListAsync(ct);

        foreach (var dep in incomingDeps)
        {
            if (dep.DependsOnMilestone.DueDate.HasValue && newStartDate.HasValue)
            {
                if (newStartDate.Value < dep.DependsOnMilestone.DueDate.Value)
                {
                    throw new ConflictException(
                        $"Ngày bắt đầu mốc ({newStartDate.Value:dd/MM/yyyy}) không được sớm hơn hạn của mốc tiền nhiệm “{dep.DependsOnMilestone.Title}” ({dep.DependsOnMilestone.DueDate.Value:dd/MM/yyyy}).",
                        WorkflowErrorCodes.MilestoneDependencyOrderViolation);
                }
            }
        }

        // 3. Revalidate outgoing dependencies (where this milestone is predecessor)
        var outgoingDeps = await context.MilestoneDependencies
            .AsNoTracking()
            .Where(d => d.DependsOnMilestoneId == milestoneId && d.DependencyType == "FINISH_TO_START")
            .Include(d => d.Milestone)
            .ToListAsync(ct);

        foreach (var dep in outgoingDeps)
        {
            if (newDueDate.HasValue && dep.Milestone.StartDate.HasValue)
            {
                if (dep.Milestone.StartDate.Value < newDueDate.Value)
                {
                    throw new ConflictException(
                        $"Hạn hoàn thành mốc ({newDueDate.Value:dd/MM/yyyy}) không được muộn hơn ngày bắt đầu của mốc kế tiếp “{dep.Milestone.Title}” ({dep.Milestone.StartDate.Value:dd/MM/yyyy}).",
                        WorkflowErrorCodes.MilestoneDependencyOrderViolation);
                }
            }
        }
    }

    public async Task ValidateExecutionWindowUpdateImpactAsync(
        long projectPeriodId,
        DateTime newStartAt,
        DateTime newEndAt,
        CancellationToken ct = default)
    {
        var period = await context.ProjectPeriods
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == projectPeriodId, ct);

        if (period == null || period.PeriodType != "EXECUTION") return;

        var newStartDate = AcademicDate(newStartAt);
        var newDueDate = AcademicDate(newEndAt);

        var projectIds = await context.Projects
            .AsNoTracking()
            .Where(p => p.Team.AcademicSemesterId == period.AcademicSemesterId)
            .Select(p => p.Id)
            .ToListAsync(ct);

        // Other execution periods in the same semester may serve different projects.
        var affectedProjects = new List<long>();
        foreach (var projectId in projectIds)
            if ((await GetExecutionWindowAsync(projectId, ct))?.ProjectPeriodId == projectPeriodId)
                affectedProjects.Add(projectId);
        projectIds = affectedProjects;
        if (projectIds.Count == 0) return;

        var conflictingMilestones = await context.Milestones
            .AsNoTracking()
            .Where(m => projectIds.Contains(m.ProjectId) &&
                ((m.StartDate.HasValue && (m.StartDate.Value < newStartDate || m.StartDate.Value > newDueDate)) ||
                 (m.DueDate.HasValue && (m.DueDate.Value < newStartDate || m.DueDate.Value > newDueDate))))
            .Select(m => $"#{m.Id} '{m.Title}' ({m.StartDate:yyyy-MM-dd} - {m.DueDate:yyyy-MM-dd})")
            .ToListAsync(ct);

        var conflictingTasks = await context.Tasks
            .AsNoTracking()
            .Where(t => projectIds.Contains(t.Milestone.ProjectId) &&
                ((t.StartAt.HasValue && (t.StartAt.Value < newStartAt || t.StartAt.Value > newEndAt)) ||
                 (t.DueAt.HasValue && (t.DueAt.Value < newStartAt || t.DueAt.Value > newEndAt))))
            .Select(t => $"#{t.Id} '{t.Title}' (due: {t.DueAt:yyyy-MM-dd})")
            .ToListAsync(ct);

        if (conflictingMilestones.Count > 0 || conflictingTasks.Count > 0)
        {
            throw new ConflictException(
                $"Cannot update project execution window because {conflictingMilestones.Count} milestone(s) and {conflictingTasks.Count} task(s) in active projects would fall outside the new window ({newStartAt:yyyy-MM-dd} - {newEndAt:yyyy-MM-dd})." +
                (conflictingMilestones.Count > 0 ? $" Affected milestones: {string.Join(", ", conflictingMilestones.Take(3))}." : "") +
                (conflictingTasks.Count > 0 ? $" Affected tasks: {string.Join(", ", conflictingTasks.Take(3))}." : ""),
                WorkflowErrorCodes.ParentDateConflict);
        }
    }
}
