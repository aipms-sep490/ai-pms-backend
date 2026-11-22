using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Milestones.Abstractions;
using AIPMS.Application.Features.Tasks.Abstractions;
using AIPMS.Application.Features.Tasks.DTOs;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Services.Projects;

internal sealed class TaskCriteriaService(
    AipmsDbContext db,
    ICurrentUser currentUser,
    IProjectAccessService projectAccess,
    IProjectExecutionGuard executionGuard,
    IAuditTrail audit,
    TimeProvider clock) : ITaskCriteriaService
{
    private const int MaxCriteriaPerTask = 30;

    public async Task<TaskCriteriaListDto> ListAsync(long taskId, CancellationToken ct = default)
    {
        var context = await AuthorizeAsync(taskId, ct);
        return await ReadListAsync(context, ct);
    }

    public async Task<TaskCriteriaListDto> ReplaceAsync(long taskId, ReplaceTaskCriteriaRequest request, CancellationToken ct = default)
    {
        if (request?.Items is null) throw new ArgumentException("Danh sách tiêu chí là bắt buộc.");
        var cleaned = request.Items.Select(x => new { Title = x.Title?.Trim() ?? "", x.IsCompleted })
            .Where(x => x.Title.Length > 0).ToArray();
        if (cleaned.Length > MaxCriteriaPerTask) throw new ArgumentException($"Mỗi công việc tối đa {MaxCriteriaPerTask} tiêu chí.");
        if (cleaned.Any(x => x.Title.Length > 500)) throw new ArgumentException("Mỗi tiêu chí tối đa 500 ký tự.");

        var context = await AuthorizeAsync(taskId, ct);
        await executionGuard.MustBeActiveAsync(context.ProjectId, ct);
        if (!context.CanManage) throw new ForbiddenException("Chỉ trưởng nhóm hoặc giảng viên hướng dẫn được chỉnh sửa tiêu chí công việc.");
        if (context.TaskLocked) throw new ConflictException("Không thể sửa tiêu chí khi công việc đã hoàn thành hoặc bị hủy.");

        var now = clock.GetUtcNow().UtcDateTime;
        var existing = await db.TaskCriteria.Where(c => c.TaskId == taskId).ToListAsync(ct);
        var byTitle = existing.ToDictionary(c => c.Title, StringComparer.OrdinalIgnoreCase);

        db.TaskCriteria.RemoveRange(existing);
        var sortOrder = 0;
        foreach (var item in cleaned)
        {
            var prev = byTitle.GetValueOrDefault(item.Title);
            var completed = item.IsCompleted ?? prev?.IsCompleted ?? false;
            db.TaskCriteria.Add(new TaskCriterion
            {
                TaskId = taskId,
                Title = item.Title,
                IsCompleted = completed,
                SortOrder = sortOrder++,
                CompletedAt = completed ? (prev?.CompletedAt ?? now) : null,
                CompletedBy = completed ? (prev?.CompletedBy ?? context.ActorId) : null,
                CreatedBy = prev?.CreatedBy ?? context.ActorId,
                CreatedAt = prev?.CreatedAt ?? now,
                UpdatedAt = now
            });
        }
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(new AuditEntry(context.ActorId, "TASK_CRITERIA_REPLACED", "TASK", taskId,
            new Dictionary<string, object?> { ["projectId"] = context.ProjectId, ["count"] = cleaned.Length }), ct);
        return await ReadListAsync(context, ct);
    }

    public async Task<TaskCriteriaListDto> ToggleAsync(long taskId, long criterionId, ToggleTaskCriterionRequest request, CancellationToken ct = default)
    {
        var context = await AuthorizeAsync(taskId, ct);
        await executionGuard.MustBeActiveAsync(context.ProjectId, ct);
        if (!context.CanToggle) throw new ForbiddenException("Chỉ người phụ trách, trưởng nhóm hoặc giảng viên hướng dẫn được đánh dấu tiêu chí.");
        if (context.TaskLocked) throw new ConflictException("Không thể cập nhật tiêu chí khi công việc đã hoàn thành hoặc bị hủy.");

        var row = await db.TaskCriteria.SingleOrDefaultAsync(c => c.Id == criterionId && c.TaskId == taskId, ct)
            ?? throw new NotFoundException("TaskCriterion", criterionId);
        var now = clock.GetUtcNow().UtcDateTime;
        row.IsCompleted = request.IsCompleted;
        row.CompletedAt = request.IsCompleted ? now : null;
        row.CompletedBy = request.IsCompleted ? context.ActorId : null;
        row.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(new AuditEntry(context.ActorId, "TASK_CRITERION_TOGGLED", "TASK", taskId,
            new Dictionary<string, object?> { ["criterionId"] = criterionId, ["isCompleted"] = request.IsCompleted }), ct);
        return await ReadListAsync(context, ct);
    }

    private async Task<TaskCriteriaListDto> ReadListAsync(TaskAccessContext context, CancellationToken ct)
    {
        var rows = await (from c in db.TaskCriteria.AsNoTracking()
                          where c.TaskId == context.TaskId
                          join u in db.Users.AsNoTracking() on c.CompletedBy equals u.Id into users
                          from u in users.DefaultIfEmpty()
                          orderby c.SortOrder, c.Id
                          select new TaskCriterionDto(c.Id, c.TaskId, c.Title, c.IsCompleted, c.SortOrder,
                              c.CompletedAt, c.CompletedBy, u == null ? null : u.FullName)).ToArrayAsync(ct);
        return new TaskCriteriaListDto(context.TaskId, rows.Length, rows.Count(r => r.IsCompleted),
            context.CanManage && !context.TaskLocked && context.ProjectActive,
            context.CanToggle && !context.TaskLocked && context.ProjectActive,
            rows);
    }

    private async Task<TaskAccessContext> AuthorizeAsync(long taskId, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is not long actorId) throw new UnauthorizedException();
        var task = await db.Tasks.AsNoTracking()
            .Where(t => t.Id == taskId)
            .Select(t => new { t.Id, t.Status, t.Milestone.ProjectId, ProjectStatus = t.Milestone.Project.Status, t.Milestone.Project.TeamId })
            .SingleOrDefaultAsync(ct) ?? throw new NotFoundException("Task", taskId);
        if (!await projectAccess.CanAccessAsync(actorId, task.ProjectId, ct))
            throw new ForbiddenException("Bạn không có quyền truy cập công việc này.");

        var isLeader = await db.TeamMembers.AsNoTracking()
            .AnyAsync(m => m.TeamId == task.TeamId && m.UserId == actorId && m.IsLeader && m.LeftAt == null, ct);
        var isPrimarySupervisor = await db.SupervisorAssignments.AsNoTracking()
            .AnyAsync(a => a.ProjectId == task.ProjectId && a.EndedAt == null && a.SupervisorProfile.UserId == actorId
                && a.AssignmentType == "PRIMARY" && a.IsPrimary, ct);
        var mentorMajors = await db.SupervisorAssignments.AsNoTracking()
            .Where(a => a.ProjectId == task.ProjectId && a.EndedAt == null && a.SupervisorProfile.UserId == actorId
                && a.AssignmentType == "DISCIPLINE_MENTOR" && a.MajorId != null)
            .Select(a => a.MajorId!.Value).ToArrayAsync(ct);
        var isMentorInTaskScope = mentorMajors.Length > 0 && await db.TaskDisciplines.AsNoTracking()
            .AnyAsync(d => d.TaskId == taskId && mentorMajors.Contains(d.MajorId), ct);
        if (mentorMajors.Length > 0 && !isPrimarySupervisor && !isLeader && !isMentorInTaskScope)
        {
            var isTeamMember = await db.TeamMembers.AsNoTracking()
                .AnyAsync(m => m.TeamId == task.TeamId && m.UserId == actorId && m.LeftAt == null, ct);
            if (!isTeamMember) throw new ForbiddenException("Công việc này không thuộc ngành phụ trách của bạn.");
        }
        var isAssignee = await db.TaskAssignees.AsNoTracking().AnyAsync(a => a.TaskId == taskId && a.UserId == actorId, ct);
        var locked = task.Status is "DONE" or "COMPLETED" or "CANCELLED";
        var canManage = isLeader || isPrimarySupervisor || isMentorInTaskScope;
        var canToggle = canManage || isAssignee;
        return new(actorId, task.Id, task.ProjectId, task.ProjectStatus == "ACTIVE", locked, canManage, canToggle);
    }

    private sealed record TaskAccessContext(long ActorId, long TaskId, long ProjectId, bool ProjectActive, bool TaskLocked, bool CanManage, bool CanToggle);
}
